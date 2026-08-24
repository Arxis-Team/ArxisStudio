using ArxisStudio.Sdk;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Designer;

/// <summary>
/// Вкладка дизайнера: канва с живой формой, линейки, зум и текст разметки.
/// </summary>
/// <remarks>
/// У каждого документа своё представление со своей канвой: общая канва означала
/// бы, что переключение вкладки пересаживает чужую форму в чужой зум и чужую
/// прокрутку. Панели — иерархия, палитра, инспектор — общие, и с ними
/// представление говорит через <see cref="DesignerState"/>.
/// </remarks>
public sealed class DesignerDocumentView : DocumentView
{
    private readonly DesignerViewHost _host;

    /// <summary>Создаёт представление открытого документа.</summary>
    /// <param name="document">Открытый документ дизайнера.</param>
    public DesignerDocumentView(DesignDocument document)
    {
        Document = document;
        _host = new DesignerViewHost(document);

        document.Reloaded += OnReloaded;
        document.Changed += OnChanged;
    }

    /// <summary>Открытый документ.</summary>
    public DesignDocument Document { get; }

    /// <inheritdoc/>
    public override Control Content => _host;

    /// <inheritdoc/>
    public override string Title => Document.FileName;

    /// <inheritdoc/>
    public override void OnActivated() => DesignerState.Instance.SetActive(this);

    /// <inheritdoc/>
    public override void OnDeactivated()
    {
        if (ReferenceEquals(DesignerState.Instance.Active, this))
            DesignerState.Instance.SetActive(null);
    }

    /// <summary>Показывает выделение узла на канве.</summary>
    /// <param name="node">Узел, чей контрол выделяется.</param>
    internal void ShowSelection(HierarchyNode? node) => _host.ShowSelection(node);

    /// <summary>Вставляет контрол палитры в элемент документа.</summary>
    /// <param name="item">Контрол палитры.</param>
    /// <param name="parent">Куда вставлять; null — в корень.</param>
    internal Task InsertFromToolboxAsync(ToolboxItem item, HierarchyNode? parent) =>
        _host.InsertAsync(item, parent ?? Document.Nodes.FirstOrDefault(), placement: "");

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        Document.Reloaded -= OnReloaded;
        Document.Changed -= OnChanged;

        if (ReferenceEquals(DesignerState.Instance.Active, this))
            DesignerState.Instance.SetActive(null);

        await Document.DisposeAsync();
    }

    private void OnReloaded(object? sender, EventArgs e)
    {
        _host.ShowDocument();

        if (ReferenceEquals(DesignerState.Instance.Active, this))
            DesignerState.Instance.NotifyReloaded();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        _host.ShowXamlIfVisible();

        if (ReferenceEquals(DesignerState.Instance.Active, this))
            DesignerState.Instance.NotifyMutated();
    }
}
