using System.Runtime.CompilerServices;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.UiDesigner.Workbench;

/// <summary>
/// Верстак дизайнера: живая форма впереди и её выбор — для панелей модуля, иерархии, инспектора и палитры.
/// </summary>
/// <remarks>
/// <para>
/// Выбор — данные формы, пути элементов (<see cref="LiveFormDocument.Selection"/>), а не контролы: путь
/// переживает правку, замену поколения и перезапуск, контрол — ничего из этого. Холст, иерархия, XAML и
/// инспектор только показывают его и просят сменить через форму.
/// </para>
/// <para>
/// Верстак один на контекст модуля: панели и редактор документов студия строит порознь и подключает к
/// одному контексту, и по нему они находят один и тот же верстак, — без статики, которая пережила бы
/// контекст и смешала бы две студии одного процесса.
/// </para>
/// <para>
/// Впереди — форма, которую студия показала последней (<see cref="DocumentView.OnActivated"/>). Щелчок по
/// панели документ не прячет, поэтому инспектор, которым правят, остаётся при своей форме; показ другого
/// документа или закрытие формы оставляют верстак без формы.
/// </para>
/// </remarks>
internal sealed class DesignerWorkbench
{
    private static readonly ConditionalWeakTable<IStudioContext, DesignerWorkbench> Benches = [];

    private DesignerWorkbench()
    {
    }

    /// <summary>Форма впереди; null — впереди не живая форма или ничего.</summary>
    public LiveFormDocument? Form { get; private set; }

    /// <summary>Впереди другая форма или никакой.</summary>
    public event EventHandler? FormChanged;

    /// <summary>У формы впереди сменился выбор.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>У формы впереди сменился текст или то, что из него построено.</summary>
    public event EventHandler? ContentChanged;

    /// <summary>Верстак контекста модуля.</summary>
    /// <param name="context">Контекст модуля.</param>
    public static DesignerWorkbench Of(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Benches.GetValue(context, static _ => new DesignerWorkbench());
    }

    /// <summary>Форму показали.</summary>
    internal void Activated(LiveFormDocument form)
    {
        if (ReferenceEquals(Form, form))
            return;

        Form = form;
        FormChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Форму спрятали, показав другой документ, или закрыли.</summary>
    internal void Deactivated(LiveFormDocument form)
    {
        if (!ReferenceEquals(Form, form))
            return;

        Form = null;
        FormChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>У формы сменился выбор.</summary>
    internal void Selected(LiveFormDocument form)
    {
        if (ReferenceEquals(Form, form))
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>У формы сменился текст или построенное.</summary>
    internal void Changed(LiveFormDocument form)
    {
        if (ReferenceEquals(Form, form))
            ContentChanged?.Invoke(this, EventArgs.Empty);
    }
}
