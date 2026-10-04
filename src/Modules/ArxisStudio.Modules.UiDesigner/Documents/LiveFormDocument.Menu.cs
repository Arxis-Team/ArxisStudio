using ArxisStudio.Markup.Xaml;
using ArxisStudio.Surface;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ArxisStudio.Modules.UiDesigner.Documents;

// Правки строения с холста: клавиши и контекстное меню.
// Часть LiveFormDocument; общее описание типа — в LiveFormDocument.cs.
internal sealed partial class LiveFormDocument
{
    private FormCommands? _commands;
    private FormMenu? _menu;

    /// <summary>Правки строения выбранного, когда документ открыт, — тестам.</summary>
    internal FormCommands? Commands => _commands;

    /// <summary>Идентификатор команды холста «вырезать».</summary>
    public const string CutCommand = "ui-designer.cut";

    /// <summary>Идентификатор команды холста «копировать».</summary>
    public const string CopyCommand = "ui-designer.copy";

    /// <summary>Идентификатор команды холста «вставить».</summary>
    public const string PasteCommand = "ui-designer.paste";

    /// <summary>Идентификатор команды холста «дублировать».</summary>
    public const string DuplicateCommand = "ui-designer.duplicate";

    /// <summary>Пункты контекстного меню холста — тем же путём, каким их собирает меню; тестам.</summary>
    internal IReadOnlyList<Control> MenuItems() =>
        _commands is { } commands ? Menu.Items(commands, _view.Sheet, Canvas()) : [];

    private FormMenu Menu => _menu ??= new FormMenu(_context.Strings);

    /// <summary>
    /// Ставит холсту клавиши правок строения и меню: правой кнопкой его просит ядро, клавишей меню и
    /// Shift+F10 — платформа.
    /// </summary>
    /// <remarks>
    /// Delete холст ловит сам и просит удаления (<see cref="FormGestures"/>); здесь — то, чего ядро не знает.
    /// Команда, которой нечего делать, клавишу не берёт: Ctrl+C без выбора уходит дальше.
    /// </remarks>
    private void WireStructure(SurfaceView sheet)
    {
        sheet.KeyCommands.Add(KeyCommand(CutCommand, FormKeys.Cut));
        sheet.KeyCommands.Add(KeyCommand(CopyCommand, FormKeys.Copy));
        sheet.KeyCommands.Add(KeyCommand(PasteCommand, FormKeys.Paste));
        sheet.KeyCommands.Add(KeyCommand(DuplicateCommand, FormKeys.Duplicate));

        sheet.ContextMenuRequesting += OnContextMenuRequesting;
        sheet.AddHandler(Control.ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    }

    private void UnwireStructure(SurfaceView sheet)
    {
        sheet.ContextMenuRequesting -= OnContextMenuRequesting;
        sheet.RemoveHandler(Control.ContextRequestedEvent, OnContextRequested);
    }

    private SurfaceKeyCommand KeyCommand(string id, KeyGesture gesture) =>
        new(id, gesture, sheet => _commands?.Run(gesture, sheet) ?? false);

    /// <summary>Правый щелчок: ядро уже перевело выбор под указатель и спрашивает, что показать.</summary>
    private void OnContextMenuRequesting(object? sender, SurfaceContextRequestingEventArgs e)
    {
        e.Handled = true;
        ShowMenu(atPointer: e.Request.Source == SurfaceContextSource.Pointer, at: null);
    }

    /// <summary>
    /// Меню, попрошенное платформой: клавишей — у выбранного; мышью — ничего, его уже показало ядро.
    /// </summary>
    /// <remarks>
    /// Платформа просит меню и на отпускание правой кнопки — у того контрола формы, над которым её
    /// отпустили, — и поле формы открыло бы своё меню поверх меню холста. Поэтому просьба гасится на пути
    /// вниз, раньше, чем дойдёт до контрола.
    /// </remarks>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Handled)
            return;

        e.Handled = true;

        if (!e.TryGetPosition(_view.Sheet, out _))
            ShowMenu(atPointer: false, at: MenuPoint());
    }

    private void ShowMenu(bool atPointer, Point? at)
    {
        if (_commands is { } commands)
            FormMenu.Show(_view.Sheet, Menu.Items(commands, _view.Sheet, Canvas()), atPointer, at);
    }

    /// <summary>Пункты холста: родитель и «показать всё».</summary>
    private CanvasActions Canvas() =>
        new(_selection.FirstOrDefault() is { Steps.IsEmpty: false }, () => SelectParent(), _controls.FrameAll);

    /// <summary>
    /// Где встанет меню, попрошенное клавишей: у левого нижнего угла выбранного, в пределах холста; без
    /// выбора — у левого верхнего угла холста.
    /// </summary>
    private Point MenuPoint()
    {
        var sheet = _view.Sheet;
        var size = sheet.Bounds.Size;

        if (_selection.Count == 0 || sheet.SelectionBounds is not { Width: > 0, Height: > 0 } bounds)
            return default;

        var zoom = sheet.ViewportZoom;
        var corner = (bounds.BottomLeft - sheet.ViewportLocation) * zoom;

        return new Point(Math.Clamp(corner.X, 0, Math.Max(0, size.Width)), Math.Clamp(corner.Y, 0, Math.Max(0, size.Height)));
    }

    /// <summary>Правки строения появляются с документом: им нужны его текст и правки.</summary>
    private void TakeCommands(FormEdits edits) =>
        _commands = new FormCommands(this, edits, _context.Strings, Say);
}
