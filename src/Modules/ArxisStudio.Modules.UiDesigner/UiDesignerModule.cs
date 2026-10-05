using ArxisStudio.Modules.UiDesigner.Snapshots;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.UiDesigner;

/// <summary>
/// Точка входа дизайнера интерфейса: заявляет команду показа доски форм и ставит поставщика превью форм.
/// </summary>
/// <remarks>
/// Всё остальное делает панель: решение ей отдаёт служба проектов, а подписывается на неё панель при
/// постройке — в <c>Activate</c> модуля служба может быть ещё не поднята. Превью форм — снимки, которые
/// оставляет живая вкладка или фоновая съёмка (<see cref="FormSnapshots"/>, <see cref="FormCaptures"/>):
/// их показывает всякий, кто показывает файлы плитками, через службу студии <see cref="IStudioFilePreviews"/>.
/// </remarks>
public sealed class UiDesignerModule : StudioPlugin
{
    /// <summary>Показать дизайнер и отдать ему клавиатуру.</summary>
    public const string ShowCommand = "ui-designer.show";

    /// <summary>Идентификатор панели: он же объявлен в манифесте.</summary>
    public const string PanelId = "ui-designer.board";

    /// <summary>Настройка: сетка на холсте доски и вкладок формы.</summary>
    public const string GridKey = "ui-designer.grid";

    /// <summary>
    /// Настройка: режим дизайнера — каждая форма в своей вкладке, а не все на одной доске.
    /// </summary>
    /// <remarks>
    /// Режим решает, где открывается форма: во вкладках — своей вкладкой, на доске — на доске, выбранной
    /// целиком и в кадре; решает это редактор документов модуля (<see cref="Documents.FormEditor"/>).
    /// Доска при этом стоит в обоих режимах.
    /// </remarks>
    public const string TabsKey = "ui-designer.tabs";

    /// <summary>
    /// Настройка: живая вкладка формы сохраняет сама — при уходе из окна студии, при закрытии вкладки и
    /// после паузы в правках.
    /// </summary>
    public const string AutoSaveKey = "ui-designer.autoSave";

    /// <summary>
    /// Настройка: что показывает новая живая вкладка формы — <c>design</c>, <c>xaml</c> или <c>split</c>.
    /// </summary>
    /// <remarks>Её пишет выбор вида на полосе вкладки: следующая открывается так, как работали в прошлой.</remarks>
    public const string ViewKey = "ui-designer.view";

    /// <summary>
    /// Настройка: снимать в фоне формы, которые ещё не открывали, — когда их плитка на виду и студия
    /// свободна (<see cref="FormCaptures"/>).
    /// </summary>
    /// <remarks>
    /// Выключают её те, кому сборка дизайна, которую поднимает первая такая форма, не нужна, пока форму
    /// не открыли: снимок тогда оставляет только живая вкладка.
    /// </remarks>
    public const string PreviewsKey = "ui-designer.previews";

    private IStudioContext? _context;
    private IDisposable? _previews;
    private FormCaptures? _captures;

    /// <inheritdoc/>
    public override void Activate(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;

        context.Commands.Register(ShowCommand, Show);

        if (FormSnapshots.For(context) is { } snapshots && context.GetService<IStudioFilePreviews>() is { } previews)
        {
            _captures = new FormCaptures(context, snapshots, context.GetService<UiDesignerOptions>() ?? UiDesignerOptions.Default);
            _previews = previews.Register(new FormPreviewProvider(snapshots, _captures));
        }
    }

    /// <inheritdoc/>
    public override void Deactivate()
    {
        _previews?.Dispose();
        _previews = null;
        _captures?.Dispose();
        _captures = null;
        _context = null;
    }

    private void Show()
    {
        if (_context is null)
            return;

        _context.GetService<IStudioToolWindows>()?.Show(PanelId);
        _context.GetService<IStudioFocus>()?.Focus(PanelId);
    }
}
