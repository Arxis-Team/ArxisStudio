using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Terminal;
using ArxisStudio.Modules.Terminal.Dialogs;
using ArxisStudio.Modules.Terminal.Panels;
using ArxisStudio.Modules.Terminal.Sessions;
using ArxisStudio.Modules.Terminal.Shells;
using ArxisStudio.Services;
using Avalonia;
using ArxisStudio.Icons;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
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

    /// <summary>
    /// Оболочка узнаёт один размер — тот, на котором раскладка остановилась.
    /// </summary>
    /// <remarks>
    /// Тяга границы даёт десятки размеров в секунду, и каждый, доехавший до
    /// оболочки, — это её перерисовка строки ввода по абсолютным координатам
    /// того экрана, который она считает нынешним. Наш экран после своего
    /// пересчёта строк держит на этом месте другую строку, и перерисовка
    /// ложится поверх старого вывода: приглашение оказывается посреди списка
    /// файлов, а набранное — не там, где курсор. Заново прошлый экран никто не
    /// пришлёт, и починить это потом нечем.
    /// </remarks>
    [AvaloniaFact]
    public void The_shell_hears_one_size_and_not_every_layout_pass()
    {
        var (window, view, pty, _) = Show();

        var first = Assert.Single(pty.Sizes);

        // Тяга границы: высота идёт шагами, как от мыши.
        for (var height = 560; height >= 300; height -= 20)
        {
            window.Height = height;
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal([first], pty.Sizes);

        view.Settle();

        var settled = Assert.Single(pty.Sizes.Skip(1));

        Assert.NotEqual(first, settled);
        Assert.Equal((view.Columns, view.Rows), settled);
    }

    /// <summary>
    /// Вырожденный размер до оболочки не доезжает вовсе.
    /// </summary>
    /// <remarks>
    /// Панель, переезжающая в своё окно, успевает встать шириной в пару знаков.
    /// Экран, пересчитанный под такую щель, обратно уже не собрать: строки
    /// перевёрнуты, а прошлый вывод никто не пришлёт заново.
    /// </remarks>
    [AvaloniaFact]
    public void A_degenerate_size_never_reaches_the_shell()
    {
        var (window, view, pty, _) = Show();

        var good = Assert.Single(pty.Sizes);
        var columns = view.Columns;
        var rows = view.Rows;

        window.Width = 24;
        window.Height = 18;
        Dispatcher.UIThread.RunJobs();
        view.Settle();

        Assert.Equal([good], pty.Sizes);
        Assert.Equal(columns, view.Columns);
        Assert.Equal(rows, view.Rows);
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

    /// <summary>
    /// Доли щелчка колеса копятся: медленная прокрутка тачпадом листает историю.
    /// </summary>
    /// <remarks>
    /// Тачпад шлёт щелчок долями — по десятой, — и округление каждой роняло их все: двумя
    /// пальцами история не листалась вовсе. Десять десятых — один щелчок, три строки, как у колеса
    /// мыши.
    /// </remarks>
    [AvaloniaFact]
    public void Wheel_fractions_add_up_to_a_notch()
    {
        var (window, view, pty, session) = Show();

        view.Focus();
        History(view, pty, session);

        var buffer = session.Terminal.Buffer;
        var bottom = buffer.YDisp;

        for (var step = 0; step < 10; step++)
            window.MouseWheel(new Point(100, 100), new Vector(0, 0.1), RawInputModifiers.None);

        Assert.Equal(bottom - 3, buffer.YDisp);
    }

    /// <summary>
    /// Программе с мышью сообщается отпускание той кнопки, что была нажата; с Shift мышь остаётся у
    /// человека.
    /// </summary>
    /// <remarks>
    /// В режиме SGR отпускание несёт номер кнопки. Пока здесь всегда стояла левая, tmux и vim
    /// видели нажатие правой и отпускание левой, и их счёт кнопок расходился.
    /// </remarks>
    [AvaloniaFact]
    public void A_program_with_the_mouse_is_told_which_button_was_released()
    {
        var (window, view, pty, session) = Show();

        view.Focus();

        // Программа просит мышь: отслеживание нажатий и запись SGR.
        pty.Emit("\u001b[?1000h\u001b[?1006h");
        Until(() => session.Terminal.MouseTrackingMode != XTerm.Input.MouseTrackingMode.None);

        var cell = view.CellSize;
        var point = new Point(TerminalView.Inset + (2.5 * cell.Width), TerminalView.Inset + (0.5 * cell.Height));

        window.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Right, RawInputModifiers.None);

        // Правая кнопка в записи SGR — номер 2: «M» на нажатии, «m» на отпускании.
        Until(() => pty.WrittenText.Contains('m'));

        Assert.Contains("\u001b[<2;3;1M", pty.WrittenText, StringComparison.Ordinal);
        Assert.Contains("\u001b[<2;3;1m", pty.WrittenText, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[<0;", pty.WrittenText, StringComparison.Ordinal);

        // С Shift мышь остаётся у человека, как в xterm и Windows Terminal: протяжка выделяет, а
        // программа о ней не слышит.
        var start = new Point(TerminalView.Inset + (0.5 * cell.Width), point.Y);

        pty.ClearWritten();
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseMove(point, RawInputModifiers.Shift);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);

        Assert.True(view.HasSelection, "протяжка с Shift ушла программе, а не выделила");
        Assert.DoesNotContain("\u001b[<", pty.WrittenText, StringComparison.Ordinal);
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

    /// <summary>
    /// Shift+Escape выпускает клавиатуру из терминала.
    /// </summary>
    /// <remarks>
    /// Tab отсюда не уводит и уводить не должен — он нужен оболочке для
    /// дополнения имён. Без отдельного сочетания человек, пришедший сюда
    /// клавишами, остался бы в терминале до самой мыши.
    /// </remarks>
    [AvaloniaFact]
    public void Shift_escape_lets_the_keyboard_out()
    {
        var pty = new FakePty();
        var session = new TerminalSession(Probe, pty, TerminalSession.Options(TerminalSettings.Default, 40, 10));
        var view = new TerminalView();
        var neighbour = new AxButton { Content = "рядом" };
        var window = new Window { Width = 800, Height = 600, Content = new StackPanel { Children = { view, neighbour } } };

        window.Show();
        view.Session = session;
        Dispatcher.UIThread.RunJobs();

        view.Focus();
        Assert.True(view.IsFocused, "вид не взял фокус");

        // Обычный Tab уходит оболочке — это и есть повод для отдельного выхода.
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
        Assert.True(view.IsFocused, "Tab увёл фокус — оболочка его не получит");

        window.KeyPress(Key.Escape, RawInputModifiers.Shift, PhysicalKey.Escape, "");

        Assert.False(view.IsFocused, "Shift+Escape не выпустил клавиатуру");
        Assert.True(neighbour.IsFocused, "фокус ушёл не к соседу");

        session.Dispose();
    }

    /// <summary>
    /// Программа, просившая о фокусе, слышит, как экран его получил и потерял.
    /// </summary>
    /// <remarks>
    /// Так vim и tmux узнают, что к ним вернулись. Слышит только просившая: без просьбы фокус не
    /// стоит оболочке ни байта — это держит <see cref="TerminalSessionTests"/>.
    /// </remarks>
    [AvaloniaFact]
    public void A_program_that_asked_hears_the_screen_gain_and_lose_focus()
    {
        var pty = new FakePty();
        var session = new TerminalSession(Probe, pty, TerminalSession.Options(TerminalSettings.Default, 40, 10));
        var view = new TerminalView();
        var neighbour = new AxButton { Content = "рядом" };
        var window = new Window { Width = 800, Height = 600, Content = new StackPanel { Children = { view, neighbour } } };

        window.Show();
        view.Session = session;
        Dispatcher.UIThread.RunJobs();

        pty.Emit("\u001b[?1004h");
        Until(() => session.Terminal.SendFocusEvents);

        view.Focus();
        Until(() => pty.WrittenText.Contains("\u001b[I", StringComparison.Ordinal));

        neighbour.Focus();
        Until(() => pty.WrittenText.Contains("\u001b[O", StringComparison.Ordinal));

        session.Dispose();
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
            var panel = Panel();
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

    /// <summary>
    /// Курсор идёт за человеком, а не за появлением панели.
    /// </summary>
    /// <remarks>
    /// Панель встаёт в раскладку при подъёме студии и заводит себе первый
    /// сеанс — человек в этот миг у терминала ничего не просил, и отобранный
    /// курсор увёл бы его набор в чужую оболочку. Сеанс, открытый нажатием,
    /// курсор берёт: за этим и нажимали.
    /// </remarks>
    [AvaloniaFact]
    public void Focus_follows_the_person_and_not_the_panel()
    {
        TerminalHub.Reset();

        var panel = Panel();

        try
        {
            var elsewhere = new AxButton { Content = "рядом" };
            var window = new Window
            {
                Width = 900,
                Height = 500,
                Content = new StackPanel { Children = { elsewhere, panel.Content } },
            };

            window.Show();
            elsewhere.Focus();
            Dispatcher.UIThread.RunJobs();

            // Ждём, пока панель заведёт себе сеанс сама: проверять до этого
            // значило бы проверять «ещё не успела», а не «не забирает».
            Wait(() => panel.Sessions.Count > 0);
            Dispatcher.UIThread.RunJobs();

            Assert.True(elsewhere.IsFocused, "панель забрала курсор, хотя её никто не просил");

            // Оболочки с таким именем нет, и это к лучшему: проверяется курсор,
            // а не запуск — вид появляется раньше, чем сеанс не заводится.
            panel.Open(new ShellProfile("probe", "Проба", "arxis-нет-такой-оболочки", []));
            Dispatcher.UIThread.RunJobs();

            var opened = window.GetLogicalDescendants().OfType<TerminalView>().Last();

            Assert.True(opened.IsFocused, "сеанс, открытый по требованию, курсор не взял");
        }
        finally
        {
            foreach (var session in panel.Sessions)
                panel.Close(session);

            TerminalHub.Reset();
        }
    }

    /// <summary>
    /// Каретка, которую студия отдаёт панели, приходит в экран выбранного сеанса, а без сеансов — на
    /// кнопку, которая его открывает.
    /// </summary>
    /// <remarks>
    /// Цели у панели не было, и студия отдавала каретку первому, кто её возьмёт, — «+» над полосой
    /// сеансов: F6 приводил на кнопку, и набранное уходило в никуда.
    /// </remarks>
    [AvaloniaFact]
    public void The_caret_given_to_the_panel_lands_in_the_session()
    {
        TerminalHub.Reset();

        var panel = Panel();

        try
        {
            var elsewhere = new AxButton { Content = "рядом" };
            var window = new Window
            {
                Width = 900,
                Height = 500,
                Content = new StackPanel { Children = { elsewhere, panel.Content } },
            };

            window.Show();
            Wait(() => panel.Sessions.Count > 0);
            Dispatcher.UIThread.RunJobs();

            // Той же дорогой, какой студия отдаёт каретку панели: цель — от панели, запоминание — от
            // раскладки, когда каретка уходит.
            var content = panel.Content;

            DockFocus.SetTarget(content, panel.FocusTarget);
            elsewhere.Focus();

            Assert.True(DockFocus.Restore(content), "каретку в панель не отдать");
            Assert.True(window.GetLogicalDescendants().OfType<TerminalView>().Single().IsFocused, "каретка не пришла в экран сеанса");

            DockFocus.Remember(content);

            foreach (var session in panel.Sessions)
                panel.Close(session);

            Dispatcher.UIThread.RunJobs();
            elsewhere.Focus();
            DockFocus.Restore(content);

            var start = window.GetLogicalDescendants().OfType<AxButton>().Single(button => button.Name == "Start");

            Assert.True(start.IsFocused, "без сеансов каретка не пришла на кнопку, открывающую сеанс");
        }
        finally
        {
            foreach (var session in panel.Sessions)
                panel.Close(session);

            TerminalHub.Reset();
        }
    }

    /// <summary>
    /// Меню «⋮» действует над открытым сеансом, а без него молчит.
    /// </summary>
    /// <remarks>
    /// Пункт, которому не над чем работать, выключен, а не делает вид, что
    /// сработал: человек, нажавший «закрыть сеанс» в пустой панели, вправе
    /// увидеть, что закрывать нечего.
    /// </remarks>
    [AvaloniaFact]
    public void The_session_menu_acts_on_the_open_session()
    {
        TerminalHub.Reset();

        var panel = Panel();

        try
        {
            var window = new Window { Width = 900, Height = 500, Content = panel.Content };

            window.Show();
            Dispatcher.UIThread.RunJobs();

            var menu = Assert.IsType<AxMenuFlyout>(Header(panel, AxIcons.MoreVertical).Flyout);
            var items = menu.Items.OfType<AxMenuItem>().ToList();

            Assert.Equal(3, items.Count);
            Assert.All(items, item => Assert.False(string.IsNullOrEmpty(item.Header as string), "пункт без подписи"));

            // Панель могла успеть завести себе сеанс сама: закрываем всё, чтобы
            // проверять именно пустоту.
            while (panel.Sessions.Count > 0)
                panel.Close(panel.Sessions[0]);

            Wait(() => Tabs(panel).Count == 0);
            Open(menu, panel);

            Assert.All(items, item => Assert.False(item.IsEnabled, $"«{item.Header}» доступен без сеанса"));

            // Оболочки с таким именем нет, но вкладка есть — над ней и действуют.
            panel.Open(new ShellProfile("probe", "Проба", "arxis-нет-такой-оболочки", []));
            Dispatcher.UIThread.RunJobs();
            Open(menu, panel);

            Assert.True(items[0].IsEnabled, "переименование выключено при открытой вкладке");
            Assert.True(items[^1].IsEnabled, "закрытие выключено при открытой вкладке");

            // А чистить нечего: оболочки за этой вкладкой нет, экран пуст, и
            // пункт об этом говорит вместо того, чтобы сработать вхолостую.
            Assert.False(items[1].IsEnabled, "очистка предложена для вкладки без оболочки");
            Assert.Single(Tabs(panel));

            items[^1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(Tabs(panel));

            window.Close();
        }
        finally
        {
            foreach (var session in panel.Sessions)
                panel.Close(session);

            TerminalHub.Reset();
        }
    }

    /// <summary>
    /// Прощание панели закрывает вкладки, оболочки за ними и отпускает хаб.
    /// </summary>
    /// <remarks>
    /// Зовёт его студия, когда выключают модуль: у панели для этого есть
    /// <see cref="ToolWindow.Release"/>. Прежде такой точки в контракте не
    /// было, и модулю приходилось тянуться к своей панели статикой — другой
    /// дороги у него не существовало.
    /// </remarks>
    [AvaloniaFact]
    public void The_panel_closes_everything_it_opened_when_it_is_asked_to_go()
    {
        TerminalHub.Reset();

        var panel = Panel();

        try
        {
            var window = new Window { Width = 900, Height = 500, Content = panel.Content };

            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Оболочка панели по умолчанию — настоящая, и на этой машине она
            // может подняться, а может и нет: вкладка есть в обоих случаях.
            // Поднявшуюся ждут, чтобы прощание закрывало живой сеанс.
            Wait(() => panel.Sessions.Count > 0);

            panel.Open(new ShellProfile("probe", "Проба", "arxis-нет-такой-оболочки", []));
            Dispatcher.UIThread.RunJobs();

            Assert.NotEmpty(Tabs(panel));

            // Прощание зовёт студия — так же, как позвала бы при выключении
            // модуля.
            panel.Release();
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(Tabs(panel));

            // Сеансы — это вкладки с поднявшейся оболочкой. Оболочку, поднявшуюся
            // уже после прощания, панель закрывает сама: её вкладки больше нет. Ждать
            // её здесь было бы нечего — прежде тест тратил на такое ожидание пять
            // секунд каждого прогона.
            Assert.Empty(panel.Sessions);

            // И хаб отпущен: пока в его статическом поле лежит эта панель, жива
            // и она, и сборка модуля за ней.
            var received = new List<TerminalRequest>();

            TerminalHub.Open(new TerminalRequest(TerminalRequestKind.Open));
            TerminalHub.Attach(received.Add);

            Assert.Single(received);

            window.Close();
        }
        finally
        {
            foreach (var session in panel.Sessions)
                panel.Close(session);

            TerminalHub.Reset();
        }
    }

    /// <summary>
    /// Номера у одинаковых вкладок не повторяются.
    /// </summary>
    /// <remarks>
    /// Номер прежде считался по числу сеансов той же оболочки, и обычная
    /// дорога давала повтор: открыть три, закрыть средний — и следующий снова
    /// назвался бы «(2)» рядом с уже стоящей «(2)». По подписи вкладки её и
    /// находят; две одинаковые не находит никто.
    /// </remarks>
    [AvaloniaFact]
    public void Two_tabs_never_wear_the_same_name()
    {
        TerminalHub.Reset();

        var panel = Panel();
        var probe = new ShellProfile("probe", "Проба", "arxis-нет-такой-оболочки", []);

        try
        {
            var window = new Window { Width = 900, Height = 500, Content = panel.Content };

            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Панель могла завести себе сеанс сама: считать номера надо с пустой
            // полосы, иначе первым именем окажется имя её оболочки.
            foreach (var tab in Tabs(panel).OfType<AxTabItem>().ToList())
                tab.RaiseEvent(new RoutedEventArgs(AxTabItem.CloseRequestedEvent));

            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Tabs(panel));

            for (var i = 0; i < 3; i++)
            {
                panel.Open(probe);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(["Проба", "Проба (2)", "Проба (3)"], Names(panel));

            // Закрыли средний — освободившееся имя и достаётся следующему.
            Tabs(panel).OfType<AxTabItem>().ElementAt(1)
                .RaiseEvent(new RoutedEventArgs(AxTabItem.CloseRequestedEvent));

            Dispatcher.UIThread.RunJobs();
            panel.Open(probe);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["Проба", "Проба (3)", "Проба (2)"], Names(panel));
            Assert.Equal(3, Names(panel).Distinct(StringComparer.Ordinal).Count());

            window.Close();
        }
        finally
        {
            foreach (var session in panel.Sessions)
                panel.Close(session);

            TerminalHub.Reset();
        }
    }

    /// <summary>Подписи вкладок панели по порядку.</summary>
    private static IReadOnlyList<string> Names(TerminalPanel panel) =>
        [.. Tabs(panel).OfType<AxTabItem>().Select(tab => tab.Content as string ?? string.Empty)];

    /// <summary>Имя вкладки чистится перед тем, как встать: без пробелов, без пустоты, без простыни.</summary>
    [Theory]
    [InlineData(" сборка ", "сборка")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void A_tab_name_is_cleaned_before_it_lands(string? typed, string? expected) =>
        Assert.Equal(expected, RenameDialog.Clean(typed));

    /// <summary>Слишком длинное имя обрезается, а не выдавливает соседние вкладки.</summary>
    [Fact]
    public void A_long_tab_name_is_cut_to_size() =>
        Assert.Equal(RenameDialog.MaxLength, RenameDialog.Clean(new string('я', 200))!.Length);

    /// <summary>
    /// Смена темы студии перекрашивает и терминал.
    /// </summary>
    /// <remarks>
    /// Цвета берутся из темы один раз — при постановке на экран, — и этого
    /// хватало ровно до первого переключения: студия становилась светлой, а
    /// панель терминала оставалась тёмной дырой посреди неё. Спрашивается
    /// эмулятор, а не поле вида: в его палитру цвета и уезжают, и по ней он
    /// отвечает программам на вопрос «какой у тебя фон».
    /// </remarks>
    [AvaloniaFact]
    public void The_terminal_follows_the_studio_theme()
    {
        var (window, _, _, session) = Show();

        window.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();

        var dark = session.Terminal.Colors.Background;

        window.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();

        Assert.NotEqual(dark, session.Terminal.Colors.Background);
        Assert.Equal(0xFFFFFF, session.Terminal.Colors.Background);
    }

    /// <summary>
    /// Снятый с экрана вид не досказывает размер оболочке.
    /// </summary>
    /// <remarks>
    /// Размер уходит по тишине в раскладке, то есть таймером. Вид, ушедший с
    /// экрана с заведённым таймером, будит сеанс уже никому не видимого
    /// экрана — и держит себя и его в памяти, пока таймер идёт. Переключение
    /// вкладок терминала — это и есть снятие с экрана.
    /// </remarks>
    [AvaloniaFact]
    public void A_view_taken_off_the_screen_stops_asking_for_a_resize()
    {
        var (window, view, pty, session) = Show();

        var first = Assert.Single(pty.Sizes);

        window.Height = 320;
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.IsSettling, "новый размер не пошёл в отсчёт");

        // Ушёл с экрана посреди отсчёта — часам здесь больше нечего отмерять.
        window.Content = null;
        Dispatcher.UIThread.RunJobs();

        Assert.False(view.IsSettling, "снятый с экрана вид продолжает отсчёт");
        Assert.Equal([first], pty.Sizes);

        // Но и потерять несказанный размер нельзя: вернулся на экран — досказал.
        window.Content = view;
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.IsSettling, "вернувшийся вид забыл досказать размер");

        view.Settle();

        var settled = Assert.Single(pty.Sizes.Skip(1));

        Assert.Equal((view.Columns, view.Rows), settled);
        Assert.Equal(view.Rows, session.Terminal.Rows);
    }

    /// <summary>Кнопка шапки с этим значком.</summary>
    private static AxButton Header(TerminalPanel panel, Geometry icon) =>
        panel.Content.GetLogicalDescendants()
            .OfType<AxButton>()
            .Single(button => button.Content is AxIcon glyph && ReferenceEquals(glyph.Data, icon));

    /// <summary>Вкладки сеансов панели.</summary>
    private static IReadOnlyList<object> Tabs(TerminalPanel panel) =>
        [.. panel.Content.GetLogicalDescendants().OfType<AxTabStrip>().First().Items.OfType<object>()];

    /// <summary>Открывает меню и закрывает: пункты решают о доступности в этот миг.</summary>
    private static void Open(AxMenuFlyout menu, TerminalPanel panel)
    {
        menu.ShowAt(Header(panel, AxIcons.MoreVertical));
        Dispatcher.UIThread.RunJobs();
        menu.Hide();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Панель терминала с настоящим контекстом студии, но без дока.</summary>
    private static TerminalPanel Panel()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(TerminalModule).Assembly);

        Assert.Null(error);

        // Папка — та же, какую считает студия: над bin сборки модуля. Корень выхода сюда не
        // годится, хотя раньше и годился: словарь студии лежит именно там, и подмена была бы не
        // видна — панель показывала бы ключи, а тест сверял бы их сам с собой.
        var plugin = new InstalledPlugin(
            ModuleManifest.FolderOf(typeof(TerminalModule).Assembly),
            manifest,
            null,
            IsEnabled: true,
            IsBuiltIn: true);
        var context = new StudioContextFactory(new StudioLog(), new StudioCommands(), null).Create(plugin);
        var panel = new TerminalPanel();

        panel.Attach(context);

        return panel;
    }

    /// <summary>
    /// Бегунок полосы прокрутки тянется мышью.
    /// </summary>
    /// <remarks>
    /// Полоса была рисунком, а не контролом: колесо листало историю, а нажатие на полосу попадало
    /// в текст — там начиналось выделение последнего столбца. Человек видел полосу, брал её и не
    /// получал ничего.
    /// </remarks>
    [AvaloniaFact]
    public void The_scrollbar_thumb_drags_the_history()
    {
        var (window, view, pty, session) = Show();

        History(view, pty, session);

        var buffer = session.Terminal.Buffer;

        Assert.Equal(buffer.YBase, buffer.YDisp);

        // Внизу истории бегунок стоит в самом низу дорожки: берём его там и тянем к верху.
        window.MouseDown(Bar(view, view.Bounds.Height - TerminalView.Inset - 1), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(Bar(view, TerminalView.Inset), RawInputModifiers.None);
        window.MouseUp(Bar(view, TerminalView.Inset), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(0, buffer.YDisp);
        Assert.False(view.HasSelection, "полоса выделила текст, которого под ней нет");
        Assert.Empty(pty.WrittenText);
    }

    /// <summary>
    /// Нажатие на дорожку листает страницу, а не прыгает на место указателя.
    /// </summary>
    /// <remarks>
    /// Так ведут себя полосы Windows, Rider и VS Code. Заодно это тот вход к прокрутке, который не
    /// требует тянуть мышь, — WCAG 2.2 просит его у всякого перетаскивания.
    /// </remarks>
    [AvaloniaFact]
    public void Pressing_the_track_turns_a_page_of_history()
    {
        var (window, view, pty, session) = Show();

        History(view, pty, session);

        var buffer = session.Terminal.Buffer;
        var was = buffer.YDisp;

        // Верх дорожки: бегунок внизу, значит выше него — дорожка.
        window.MouseDown(Bar(view, TerminalView.Inset + 1), MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(Bar(view, TerminalView.Inset + 1), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(Math.Max(0, was - view.Rows), buffer.YDisp);
        Assert.False(view.HasSelection, "нажатие на дорожку выделило текст");
        Assert.Empty(pty.WrittenText);
    }

    /// <summary>
    /// Shift+PageUp листает историю, а оболочка об этом не слышит.
    /// </summary>
    /// <remarks>
    /// Сочетания у Windows Terminal, и без них история листалась только мышью. Оболочке эти
    /// клавиши не нужны: страницу собственной истории терминала не посылает никто.
    /// </remarks>
    [AvaloniaFact]
    public void Shift_page_up_scrolls_the_history_and_the_shell_hears_nothing()
    {
        var (window, view, pty, session) = Show();

        view.Focus();
        History(view, pty, session);

        var buffer = session.Terminal.Buffer;
        var was = buffer.YDisp;

        window.KeyPress(Key.PageUp, RawInputModifiers.Shift, PhysicalKey.PageUp, null);

        Assert.Equal(Math.Max(0, was - view.Rows), buffer.YDisp);
        Assert.Empty(pty.WrittenText);

        window.KeyPress(Key.PageDown, RawInputModifiers.Shift, PhysicalKey.PageDown, null);

        Assert.Equal(was, buffer.YDisp);

        window.KeyPress(Key.Home, RawInputModifiers.Shift | RawInputModifiers.Control, PhysicalKey.Home, null);

        Assert.Equal(0, buffer.YDisp);

        window.KeyPress(Key.End, RawInputModifiers.Shift | RawInputModifiers.Control, PhysicalKey.End, null);

        Assert.Equal(buffer.YBase, buffer.YDisp);
        Assert.Empty(pty.WrittenText);
    }

    /// <summary>Набивает историю, которую есть чем листать.</summary>
    private static void History(TerminalView view, FakePty pty, TerminalSession session)
    {
        for (var i = 0; i < 200; i++)
            pty.Emit($"line{i}\r\n");

        Until(() => session.Terminal.Buffer.YBase > view.Rows
                    && session.Terminal.GetVisibleLines().Any(line => line.StartsWith("line199", StringComparison.Ordinal)));
    }

    /// <summary>Точка на дорожке полосы прокрутки — у правого края вида.</summary>
    private static Point Bar(TerminalView view, double y) =>
        new(view.Bounds.Width - (TerminalView.ScrollBarWidth / 2), y);

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
        Assert.True(Wait(ready), "не дождались");
    }

    /// <summary>
    /// Крутит диспетчер, пока условие не выполнится, и говорит, дождался ли.
    /// </summary>
    /// <remarks>
    /// Без утверждения: бывает, что ожидаемое зависит от машины — например,
    /// поднимется ли на ней оболочка, — а проверяемое от этого не зависит.
    /// </remarks>
    private static bool Wait(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow + Timeout;

        while (!ready() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        return ready();
    }
}
