using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Terminal;
using ArxisStudio.Modules.Terminal.Shells;
using ArxisStudio.Services;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Экран терминала: размер, клавиатура, мышь и то, что он вообще рисуется.
/// </summary>
/// <remarks>
/// Ввод идёт настоящими событиями платформы через headless-окно — туда же,
/// куда попал бы курсор человека. Оболочка подменена трубой в памяти: важно,
/// что нажатие стало байтами, а не что cmd на них ответил.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class TerminalViewTests
{
    private const string Esc = "\u001b";
    private static readonly ShellProfile Probe = new("probe", "Проба", "probe", []);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Экран получает столько знаков, сколько влезает в вид, и оболочка узнаёт о размере.</summary>
    [AvaloniaFact]
    public void The_screen_takes_the_size_of_the_view()
    {
        var (_, view, pty, session) = Show();

        Assert.InRange(view.Columns, 41, 200);
        Assert.InRange(view.Rows, 11, 100);
        Assert.Contains((view.Columns, view.Rows), pty.Sizes);
        Assert.Equal(view.Columns, session.Terminal.Cols);
        Assert.Equal(view.Rows, session.Terminal.Rows);
    }

    /// <summary>Набранное уходит текстом, Enter и Ctrl+C — кодами.</summary>
    [AvaloniaFact]
    public void Typing_goes_to_the_shell_and_special_keys_go_as_codes()
    {
        var (window, view, pty, _) = Show();

        view.Focus();
        Assert.True(view.IsFocused, "вид не взял фокус");

        window.KeyTextInput("ls");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");

        Until(() => pty.WrittenText == "ls\r");

        window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");

        Until(() => pty.WrittenText.EndsWith('\u0003'));
    }

    /// <summary>Вывод рисуется, а колесо листает историю — и возвращается нажатием клавиши.</summary>
    [AvaloniaFact]
    public void Output_is_drawn_and_the_wheel_scrolls_history()
    {
        var (window, view, pty, session) = Show();

        view.Focus();

        for (var i = 0; i < 80; i++)
            pty.Emit($"line{i}\r\n");

        // Видимое окно, а не буфер целиком: GetLine адресует буфер, и после
        // прокрутки его строка 34 — давно уехавшая наверх.
        Until(() => session.Terminal.Buffer.YBase > 0
                    && session.Terminal.GetVisibleLines().Any(line => line.StartsWith("line79", StringComparison.Ordinal)));

        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);

        window.MouseWheel(new Point(100, 100), new Vector(0, 3), RawInputModifiers.None);

        Assert.True(session.Terminal.Buffer.YDisp < session.Terminal.Buffer.YBase, "история не пролисталась");

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");

        Assert.Equal(session.Terminal.Buffer.YBase, session.Terminal.Buffer.YDisp);
    }

    /// <summary>Протяжка мышью выделяет текст; щелчок без протяжки — нет; Ctrl+C с выделением не прерывает оболочку.</summary>
    [AvaloniaFact]
    public void Dragging_selects_text_and_ctrl_c_copies_instead_of_interrupting()
    {
        var (window, view, pty, session) = Show();

        view.Focus();
        pty.Emit("hello world");
        Until(() => session.Terminal.GetVisibleLines()[0].StartsWith("hello world", StringComparison.Ordinal));

        var cell = view.CellSize;

        Point At(int column) => new(TerminalView.Inset + ((column + 0.5) * cell.Width), TerminalView.Inset + (0.5 * cell.Height));

        window.MouseDown(At(0), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(At(4), RawInputModifiers.None);
        window.MouseUp(At(4), MouseButton.Left, RawInputModifiers.None);

        Assert.True(view.HasSelection, "протяжка не выделила");
        Assert.Equal("hello", session.Terminal.Selection.GetSelectionText().TrimEnd());

        window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");

        Assert.False(view.HasSelection, "выделение осталось после копирования");
        Assert.DoesNotContain('\u0003', pty.WrittenText);

        window.MouseDown(At(2), MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(At(2), MouseButton.Left, RawInputModifiers.None);

        Assert.False(view.HasSelection, "щелчок без протяжки выделил");
    }

    /// <summary>Вид называет себя для средств доступности, а не остаётся безымянным контролом.</summary>
    [AvaloniaFact]
    public void The_view_names_itself_for_accessibility()
    {
        var view = new TerminalView();

        view.Describe("Терминал: Проба");

        Assert.Equal("Терминал: Проба", AutomationProperties.GetName(view));
    }

    /// <summary>
    /// У кнопок панели есть имена для средств доступности, и панель строится на контролах студии.
    /// </summary>
    /// <remarks>
    /// Кнопки — значки 16×16, и без имени о них нельзя узнать ничего ни
    /// подсказкой, ни программой чтения с экрана. Панель здесь только строится,
    /// не показывается: показ открыл бы настоящую оболочку.
    /// </remarks>
    [AvaloniaFact]
    public void The_panel_names_its_buttons()
    {
        TerminalHub.Reset();

        try
        {
            var (manifest, error) = ModuleManifest.Load(typeof(TerminalModule).Assembly);

            Assert.Null(error);

            var plugin = new InstalledPlugin(AppContext.BaseDirectory, manifest, null, IsEnabled: true, IsBuiltIn: true);
            var context = new StudioContextFactory(new StudioLog(), new StudioCommands(), null).Create(plugin);
            var panel = new TerminalPanel();

            panel.Attach(context);

            var content = panel.Content;
            var buttons = content.GetLogicalDescendants().OfType<AxButton>().ToList();

            Assert.InRange(buttons.Count, 3, 10);
            Assert.All(buttons, button => Assert.False(
                string.IsNullOrEmpty(AutomationProperties.GetName(button)) && button.Content is not string,
                "кнопка без имени"));
            Assert.Empty(panel.Sessions);
        }
        finally
        {
            TerminalHub.Reset();
        }
    }

    private static (Window Window, TerminalView View, FakePty Pty, TerminalSession Session) Show()
    {
        var pty = new FakePty();
        var session = new TerminalSession(Probe, pty, TerminalSession.Options(TerminalSettings.Default, 40, 10));
        var view = new TerminalView();
        var window = new Window { Width = 800, Height = 600, Content = view };

        window.Show();
        view.Session = session;
        Dispatcher.UIThread.RunJobs();

        return (window, view, pty, session);
    }

    /// <summary>Крутит диспетчер, пока условие не выполнится; иначе — падает с объяснением.</summary>
    private static void Until(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow + Timeout;

        while (!ready() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Assert.True(ready(), "не дождались");
    }
}
