using System.Globalization;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.Sdk;
using ArxisStudio.Surface;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>
/// Вкладка одной формы: её рамка на своём холсте.
/// </summary>
/// <remarks>
/// Холст свой у каждой вкладки, а органы у него те же, что у доски (<see cref="SheetControls"/>): кадр,
/// «100 %», сетка той же настройкой. При первом показе вкладка вписывает форму — не крупнее ста
/// процентов, так что окно 800 × 450 стоит в натуральную величину, а широкое ужимается до холста.
/// <para>
/// Корень перечитывается, когда вкладку показывают снова: разметку правят в другом редакторе, а служба
/// проектов о правке содержимого не сообщает — она следит за составом. Изменился размер — форма
/// вписывается заново; не изменилось ничего — масштаб, выбранный человеком, остаётся.
/// </para>
/// </remarks>
internal sealed class FormDocument : DocumentView
{
    private readonly IStudioContext _context;
    private readonly FormView _view;
    private readonly FormSheet _sheet;
    private readonly SheetControls _controls;
    private bool _framed;

    /// <summary>Строит вкладку.</summary>
    /// <param name="context">Контекст модуля.</param>
    /// <param name="path">Файл формы.</param>
    /// <param name="root">Корень, уже прочитанный редактором.</param>
    public FormDocument(IStudioContext context, string path, FormRoot root)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _view = new FormView();

        var fallback = new Size(
            SheetControls.LengthOf(_view, "AxFormFrameWidth"),
            SheetControls.LengthOf(_view, "AxFormFrameHeight"));

        _sheet = new FormSheet(path, root, fallback, context.Strings);
        _view.DataContext = _sheet;
        _view.Sheet.ItemsSource = new[] { _sheet };
        _controls = new SheetControls(context, _view.Sheet, _view.Fit, _view.Actual, _view.GridToggle, Everything);

        _view.Sheet.KeyCommands.Remove(SurfaceKeyCommands.Delete);
        _view.Sheet.KeyCommands.Remove(SurfaceKeyCommands.Resize);
        _view.Sheet.KeyCommands.Add(new SurfaceKeyCommand("ui-designer.frame", BoardMenu.FrameKey, _ =>
        {
            _controls.FrameAll();

            return true;
        }));

        _view.Sheet.Loaded += OnLoaded;
    }

    /// <inheritdoc/>
    public override Control Content => _view;

    /// <inheritdoc/>
    public override string Title => _sheet.Name;

    /// <inheritdoc/>
    /// <remarks>Холст — с него работают: F, стрелки и зум с клавиатуры.</remarks>
    public override Control? FocusTarget => _view.Sheet;

    /// <summary>Рамка — тестам.</summary>
    internal FormSheet Sheet => _sheet;

    /// <summary>Разметка — тестам.</summary>
    internal FormView View => _view;

    /// <inheritdoc/>
    public override void OnActivated() => Refresh();

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        _view.Sheet.Loaded -= OnLoaded;
        _controls.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Перечитывает корень с диска; изменился размер — вписывает форму заново.
    /// </summary>
    internal void Refresh()
    {
        var root = FormRoot.Read(_sheet.FilePath) ?? NotForm();
        var size = (_sheet.Width, _sheet.Height);

        if (_sheet.Update(root) && size != (_sheet.Width, _sheet.Height) && _framed)
            Dispatcher.UIThread.Post(_controls.FrameAll, DispatcherPriority.Loaded);
    }

    /// <summary>Файл перестал быть формой — стал словарём стилей или приложением.</summary>
    private FormRoot NotForm() => new(
        FormKind.Unreadable,
        string.Empty,
        null,
        null,
        null,
        string.Format(CultureInfo.CurrentCulture, _context.Strings["form.notForm"], _sheet.Name));

    /// <summary>
    /// Первый показ: форма вписывается, когда холст и рамка измерены, — до раскладки их размер нулевой.
    /// </summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_framed)
            return;

        _framed = true;
        Dispatcher.UIThread.Post(_controls.FrameAll, DispatcherPriority.Loaded);
    }

    /// <summary>Рамка вместе с подписью: размер — у контейнера, если он измерен, иначе у самой рамки.</summary>
    private Rect? Everything() =>
        _view.Sheet.ContainerFromItem(_sheet) is { Bounds.Size: { Width: > 0 } measured }
            ? new Rect(measured)
            : new Rect(0, 0, _sheet.Width, _sheet.Height);
}
