using ArxisStudio.Markup.Xaml;
using ArxisStudio.Sdk;
using ArxisStudio.Surface;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ArxisStudio.Modules.UiDesigner.Documents;

// Правки строения с холста: клавиши и контекстное меню.
// Часть FormCanvas; общее описание типа — в FormCanvas.cs.
internal sealed partial class FormCanvas
{
    private FormMenu? _menu;

    /// <summary>Идентификатор команды холста «вырезать».</summary>
    public const string CutCommand = "ui-designer.cut";

    /// <summary>Идентификатор команды холста «копировать».</summary>
    public const string CopyCommand = "ui-designer.copy";

    /// <summary>Идентификатор команды холста «вставить».</summary>
    public const string PasteCommand = "ui-designer.paste";

    /// <summary>Идентификатор команды холста «дублировать».</summary>
    public const string DuplicateCommand = "ui-designer.duplicate";

    /// <summary>Правки строения выбранного в форме, с которой работают, когда её документ открыт.</summary>
    public FormCommands? Commands =>
        Active is { Session.Edits: { } edits } active ? new FormCommands(active, edits, Context.Strings, Say) : null;

    /// <summary>Пункты контекстного меню холста — тем же путём, каким их собирает меню; тестам.</summary>
    internal IReadOnlyList<Control> MenuItems() =>
        Commands is { } commands ? Menu.Items(commands, Sheet, Canvas()) : [];

    private FormMenu Menu => _menu ??= new FormMenu(Context.Strings);

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
        new(id, gesture, sheet => Commands?.Run(gesture, sheet) ?? false);

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

        if (!e.TryGetPosition(Sheet, out _))
            ShowMenu(atPointer: false, at: MenuPoint());
    }

    private void ShowMenu(bool atPointer, Point? at)
    {
        if (Commands is { } commands)
            FormMenu.Show(Sheet, Menu.Items(commands, Sheet, Canvas()), atPointer, at);
    }

    /// <summary>Пункты холста: родитель и «показать всё».</summary>
    private CanvasActions Canvas() =>
        new(_selection.Count > 0 && _selection[0].Path is { Steps.IsEmpty: false }, () => SelectParent(), _host.FrameAll);

    /// <summary>
    /// Где встанет меню, попрошенное клавишей: у левого нижнего угла выбранного, в пределах холста; без
    /// выбора — у левого верхнего угла холста.
    /// </summary>
    private Point MenuPoint()
    {
        var size = Sheet.Bounds.Size;

        if (_selection.Count == 0 || Sheet.SelectionBounds is not { Width: > 0, Height: > 0 } bounds)
            return default;

        var zoom = Sheet.ViewportZoom;
        var corner = (bounds.BottomLeft - Sheet.ViewportLocation) * zoom;

        return new Point(Math.Clamp(corner.X, 0, Math.Max(0, size.Width)), Math.Clamp(corner.Y, 0, Math.Max(0, size.Height)));
    }

    private void Say(string message) => Context.GetService<IStudioStatus>()?.Show(message);
}
