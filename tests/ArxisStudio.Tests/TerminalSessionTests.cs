using System.Text;
using ArxisStudio.Modules.Terminal;
using ArxisStudio.Modules.Terminal.Shells;
using Avalonia.Input;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Сеанс терминала: дорога байтов между оболочкой и экраном.
/// </summary>
/// <remarks>
/// Оболочки здесь нет — вместо псевдотерминала труба в памяти. Проверяется
/// именно сеанс: что пришедшее попадает на экран, что ответы и нажатия уходят
/// обратно, что выход оболочки замечен и псевдотерминал отпущен. Перенос на
/// поток интерфейса подменён прямым вызовом: диспетчера у этих тестов нет.
/// </remarks>
public class TerminalSessionTests
{
    private const string Esc = "\u001b";
    private static readonly ShellProfile Probe = new("probe", "Проба", "probe", []);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Вывод оболочки оказывается на экране, и сеанс об этом говорит.</summary>
    [Fact]
    public void Output_reaches_the_screen()
    {
        using var pty = new FakePty();
        using var session = Start(pty);
        using var changed = new ManualResetEventSlim();

        session.Changed += (_, _) => changed.Set();
        pty.Emit("hello\r\n");

        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken), "экран не изменился");
        Assert.Equal("hello", session.Terminal.GetLine(0).TrimEnd());
    }

    /// <summary>
    /// Многобайтовый знак, разрезанный границей чтения, доклеивается.
    /// </summary>
    /// <remarks>
    /// Оболочка пишет байтами, а труба режет их где придётся: буква «ё» в
    /// UTF-8 — два байта, и они запросто приходят порознь.
    /// </remarks>
    [Fact]
    public void A_character_split_across_reads_is_glued_back()
    {
        using var pty = new FakePty();
        using var session = Start(pty);
        using var changed = new SemaphoreSlim(0);

        session.Changed += (_, _) => changed.Release();

        var bytes = Encoding.UTF8.GetBytes("ё");

        pty.Emit(bytes[..1]);
        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));
        pty.Emit(bytes[1..]);
        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));

        Assert.Equal("ё", session.Terminal.GetLine(0).TrimEnd());
    }

    /// <summary>Заголовок, назначенный оболочкой, становится заголовком сеанса.</summary>
    [Fact]
    public void The_shell_names_the_session()
    {
        using var pty = new FakePty();
        using var session = Start(pty);
        using var named = new ManualResetEventSlim();
        string? title = null;

        session.TitleChanged += (_, t) =>
        {
            title = t;
            named.Set();
        };

        Assert.Equal("Проба", session.Title);
        pty.Emit(Esc + "]0;Заголовок\u0007");

        Assert.True(named.Wait(Timeout, TestContext.Current.CancellationToken), "заголовок не пришёл");
        Assert.Equal("Заголовок", title);
        Assert.Equal("Заголовок", session.Title);
    }

    /// <summary>
    /// Вопрос оболочки «кто ты» получает ответ в ту же трубу.
    /// </summary>
    /// <remarks>
    /// Без этого программы, спрашивающие терминал о нём самом, ждали бы ответа
    /// секундами: так ведёт себя ConPTY при запуске.
    /// </remarks>
    [Fact]
    public void Device_attributes_are_answered_back_to_the_shell()
    {
        using var pty = new FakePty();
        using var session = Start(pty);

        pty.Emit(Esc + "[c");

        var answer = pty.WaitForWritten(text => text.EndsWith('c'), Timeout);

        Assert.StartsWith(Esc + "[?", answer, StringComparison.Ordinal);
    }

    /// <summary>Клавиши уходят кодами, и код стрелки зависит от режима, который включила оболочка.</summary>
    [Fact]
    public void Keys_are_encoded_by_the_emulator_and_its_modes()
    {
        using var pty = new FakePty();
        using var session = Start(pty);
        using var changed = new ManualResetEventSlim();

        Assert.True(session.SendKey(Key.Up, KeyModifiers.None));
        Assert.Equal(Esc + "[A", pty.WaitForWritten(text => text.Length >= 3, Timeout));

        pty.ClearWritten();
        Assert.False(session.SendKey(Key.A, KeyModifiers.None), "обычная буква приходит текстом, не клавишей");
        Assert.True(session.SendKey(Key.C, KeyModifiers.Control));
        Assert.Equal("\u0003", pty.WaitForWritten(text => text.Length >= 1, Timeout));

        session.Changed += (_, _) => changed.Set();
        pty.Emit(Esc + "[?1h");
        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));

        pty.ClearWritten();
        Assert.True(session.SendKey(Key.Up, KeyModifiers.None));
        Assert.Equal(Esc + "OA", pty.WaitForWritten(text => text.Length >= 3, Timeout));
    }

    /// <summary>Набранный текст уходит как есть.</summary>
    [Fact]
    public void Typed_text_goes_to_the_shell()
    {
        using var pty = new FakePty();
        using var session = Start(pty);

        session.SendText("ls");

        Assert.Equal("ls", pty.WaitForWritten(text => text.Length >= 2, Timeout));
    }

    /// <summary>
    /// Вставка в режиме скобок обёрнута, а переводы строк стали нажатиями Enter.
    /// </summary>
    /// <remarks>
    /// Так вставляет Windows Terminal: оболочка не исполняет вставленное
    /// построчно, а многострочная команда доходит целиком.
    /// </remarks>
    [Fact]
    public void Paste_honours_bracketed_mode()
    {
        using var pty = new FakePty();
        using var session = Start(pty);
        using var changed = new ManualResetEventSlim();

        session.Changed += (_, _) => changed.Set();
        pty.Emit(Esc + "[?2004h");
        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));

        session.Paste("a\nb");

        var expected = Esc + "[200~a\rb" + Esc + "[201~";

        Assert.Equal(expected, pty.WaitForWritten(text => text.Length >= expected.Length, Timeout));
    }

    /// <summary>
    /// Чистить экран просят саму оболочку, если она умеет.
    /// </summary>
    /// <remarks>
    /// Ctrl+L — и оболочка перерисует приглашение, сохранит набранное и
    /// оставит свой экран и наш одним и тем же. Последнее и есть причина:
    /// ConPTY держит свою копию экрана, и уборка только на нашей стороне с ней
    /// разошлась бы — PSReadLine рисует строку ввода по запомненным
    /// координатам, и следующая набранная буква появилась бы не там, где
    /// курсор.
    /// </remarks>
    [Fact]
    public void Clearing_asks_a_capable_shell_to_do_it()
    {
        using var pty = new FakePty();
        using var session = Start(pty);

        Fill(session, pty);

        var before = session.Terminal.GetVisibleLines();

        Assert.True(session.CanClearScreen);

        session.ClearScreen();

        Assert.Equal("\f", pty.WaitForWritten(text => text.Length > 0, Timeout));

        // Экран не тронут: его почистит оболочка, когда получит просьбу.
        Assert.Equal(before, session.Terminal.GetVisibleLines());
    }

    /// <summary>
    /// За оболочку без редактора строки терминал чистит сам, оставляя приглашение.
    /// </summary>
    /// <remarks>
    /// Это <c>cmd</c>: Ctrl+L для него просто символ. Строка курсора остаётся —
    /// на ней приглашение, и стереть её вместе с остальным (а это ровно то, что
    /// делает <c>Clear</c> эмулятора) значит оставить пустой экран без
    /// приглашения, которого оболочка заново не нарисует. Цвета при этом её
    /// собственные: перерисовка текстом их потеряла бы.
    /// </remarks>
    [Fact]
    public void Clearing_a_shell_without_a_line_editor_keeps_the_prompt()
    {
        using var pty = new FakePty();
        using var session = new TerminalSession(
            Probe with { ClearsItself = false },
            pty,
            TerminalSession.Options(TerminalSettings.Default, 40, 10),
            post: action => action());


        Fill(session, pty);

        var buffer = session.Terminal.Buffer;

        Assert.True(buffer.YBase > 0, "истории не набралось — проверять было бы нечего");
        Assert.True(session.CanClearScreen);

        session.ClearScreen();

        Assert.Equal(string.Empty, pty.WrittenText);

        Assert.Equal(0, buffer.YBase);
        Assert.Equal(0, buffer.YDisp);
        Assert.Equal(0, buffer.Y);
        Assert.Equal(session.Terminal.Rows, buffer.Lines.Length);
        Assert.Equal("PS C:>", session.Terminal.GetVisibleLines()[0].TrimEnd());
        var top = buffer.Lines[0];

        Assert.NotNull(top);
        Assert.Equal(2, top![0].Attributes.GetFgColor());
        Assert.All(session.Terminal.GetVisibleLines().Skip(1), line => Assert.Equal(string.Empty, line.TrimEnd()));
    }

    /// <summary>
    /// На альтернативном экране очистка не делает ничего.
    /// </summary>
    /// <remarks>
    /// Там рисует полноэкранная программа по своей модели: подъём её строк был
    /// бы ложью о том, что у неё на экране, а истории, которую можно было бы
    /// убрать, у альтернативного экрана нет вовсе.
    /// </remarks>
    [Fact]
    public void Clearing_leaves_a_full_screen_program_alone()
    {
        using var pty = new FakePty();
        using var session = Start(pty);
        using var changed = new SemaphoreSlim(0);

        session.Changed += (_, _) => changed.Release();

        pty.Emit(Esc + "[?1049h" + "меню программы\r\nвторая строка");
        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));

        Assert.True(session.Terminal.IsAlternateBufferActive);
        Assert.False(session.CanClearScreen);

        var before = session.Terminal.GetVisibleLines();

        session.ClearScreen();

        Assert.Equal(before, session.Terminal.GetVisibleLines());
    }

    /// <summary>Размер уходит и эмулятору, и оболочке — но только когда он изменился.</summary>
    [Fact]
    public void Resize_reaches_both_sides_once()
    {
        using var pty = new FakePty();
        using var session = Start(pty);

        session.Resize(100, 30);
        session.Resize(100, 30);

        Assert.Equal([(100, 30)], pty.Sizes);
        Assert.Equal(100, session.Terminal.Cols);
        Assert.Equal(30, session.Terminal.Rows);
    }

    /// <summary>
    /// При росте высоты строка курсора остаётся на своём месте, если экран держит ConPTY.
    /// </summary>
    /// <remarks>
    /// Расхождение о том, куда растёт окно, и есть та поломка разметки, из-за
    /// которой набранное появлялось посреди списка файлов. ConPTY со своим
    /// экраном растит окно вниз: строка курсора стоит там же, а пустое
    /// добавляется под ней. Эмулятор растит вверх — подтягивает историю сверху
    /// и уводит курсор в самый низ. После этого установка курсора абсолютными
    /// координатами, которой оболочка рисует строку ввода, бьёт мимо ровно на
    /// разницу — и ложится поверх старого вывода.
    /// </remarks>
    [Fact]
    public void Growing_keeps_the_cursor_where_the_console_keeps_it()
    {
        using var pty = new FakePty { KeepsOwnScreen = true };
        using var session = Start(pty);

        Fill(session, pty);

        var buffer = session.Terminal.Buffer;
        var row = buffer.Y;
        var top = buffer.YBase;

        Assert.True(top > 0, "истории не набралось — расхождению негде взяться");

        session.Resize(40, session.Terminal.Rows + 6);

        Assert.Equal(top, buffer.YBase);
        Assert.Equal(row, buffer.Y);
        Assert.True(Prompt(session), "курсор ушёл со строки приглашения");

        // Под приглашением — пустое, как и у ConPTY.
        Assert.All(
            session.Terminal.GetVisibleLines().Skip(row + 1),
            line => Assert.Equal(string.Empty, line.TrimEnd()));
    }

    /// <summary>
    /// При сужении строка ввода получает столько строк, сколько даёт ей ConPTY.
    /// </summary>
    /// <remarks>
    /// Переплетения строк у эмулятора нет вовсе: при сужении он обрезает строку
    /// по новой ширине. ConPTY же переплетает, и строка ввода, не влезшая в
    /// новую ширину, начинается у него выше — на столько строк, сколько добавил
    /// перенос. Не отведи мы их, его перерисовка строки ввода легла бы выше
    /// приглашения, поверх старого вывода: ровно это и оставалось после
    /// первого захода на изменение размера.
    /// </remarks>
    [Fact]
    public void Narrowing_reserves_the_rows_the_console_gives_the_wrapped_input()
    {
        using var pty = new FakePty { KeepsOwnScreen = true };
        using var session = Wide(pty, 60);

        // Пятьдесят девять знаков: при ширине 60 это одна строка, при 50 — две.
        pty.Emit(new string('q', 59));

        Assert.True(SpinWait.SpinUntil(() => session.Terminal.Buffer.X == 59, Timeout), "набранное не дошло");

        var buffer = session.Terminal.Buffer;
        var row = buffer.Y;

        session.Resize(50, session.Terminal.Rows);

        // Снизу было место: перенос ушёл вниз, курсор — за ним.
        Assert.Equal(row + 1, buffer.Y);
        Assert.Equal(9, buffer.X);

        // И знаки ушли вместе с ним: хвост, обрезанный при сужении, на месте.
        Assert.Equal(new string('q', 50), session.Terminal.GetVisibleLines()[row].TrimEnd());
        Assert.Equal(new string('q', 9), session.Terminal.GetVisibleLines()[row + 1].TrimEnd());
    }

    /// <summary>
    /// Строке ввода у самого низа перенос отводят, подняв экран.
    /// </summary>
    /// <remarks>
    /// Вниз некуда — значит наверх уезжает всё, и курсор остаётся на последней
    /// строке. Так же решает и ConPTY: его окно следует за курсором и
    /// двигается лишь тогда, когда иначе курсор из него выпал бы. Это и есть
    /// обычное положение дел: приглашение стоит у нижнего края.
    /// </remarks>
    [Fact]
    public void Narrowing_lifts_the_screen_when_the_input_sits_at_the_bottom()
    {
        using var pty = new FakePty { KeepsOwnScreen = true };
        using var session = Wide(pty, 60);

        // Загоняем курсор на последнюю строку, а потом набираем длинное.
        for (var line = 0; line < session.Terminal.Rows; line++)
            pty.Emit($"строка{line}\r\n");

        pty.Emit(new string('q', 59));

        Assert.True(
            SpinWait.SpinUntil(() => session.Terminal.Buffer.X == 59, Timeout),
            "набранное не дошло");

        var buffer = session.Terminal.Buffer;
        var bottom = session.Terminal.Rows - 1;

        Assert.Equal(bottom, buffer.Y);

        session.Resize(50, session.Terminal.Rows);

        Assert.Equal(bottom, buffer.Y);
        Assert.Equal(9, buffer.X);
        Assert.StartsWith("qqq", session.Terminal.GetVisibleLines()[bottom - 1], StringComparison.Ordinal);
    }

    /// <summary>
    /// Уже перенесённой строке отводят столько строк, сколько нужно ей целиком.
    /// </summary>
    /// <remarks>
    /// Два счёта, которые легко перепутать. Длина считается по всей логической
    /// строке, а не по столбцу курсора: строка уже перенесена, и в столбце
    /// лежит лишь остаток последней её части. А число новых строк считается по
    /// новой ширине, а не «на одну больше»: сужение втрое добавляет не одну
    /// строку, а сколько придётся.
    /// </remarks>
    [Fact]
    public void An_already_wrapped_input_gets_all_the_rows_it_needs()
    {
        using var pty = new FakePty { KeepsOwnScreen = true };
        using var session = Wide(pty, 60);

        // Сто тридцать знаков при ширине 60 — это три строки: 60, 60 и 10.
        pty.Emit(new string('q', 130));

        Assert.True(
            SpinWait.SpinUntil(() => session.Terminal.Buffer is { X: 10, Y: 2 }, Timeout),
            "набранное не легло в три строки");

        var buffer = session.Terminal.Buffer;

        // При ширине 30 те же 130 знаков — уже пять строк: четыре по 30 и 10.
        session.Resize(30, session.Terminal.Rows);

        Assert.Equal(4, buffer.Y);
        Assert.Equal(10, buffer.X);
    }

    /// <summary>Строке, которая и на новой ширине умещается, ничего не отводят.</summary>
    [Fact]
    public void A_short_input_needs_no_reserved_rows()
    {
        using var pty = new FakePty { KeepsOwnScreen = true };
        using var session = Wide(pty, 60);

        pty.Emit(new string('q', 30));

        Assert.True(SpinWait.SpinUntil(() => session.Terminal.Buffer.X == 30, Timeout), "набранное не дошло");

        var buffer = session.Terminal.Buffer;
        var row = buffer.Y;

        session.Resize(50, session.Terminal.Rows);

        Assert.Equal(row, buffer.Y);
        Assert.Equal(30, buffer.X);
    }

    /// <summary>
    /// При расширении перенесённую строку сшивают обратно — как её сшивает ConPTY.
    /// </summary>
    /// <remarks>
    /// Замерено на живой оболочке: после расширения ConPTY считает строку ввода
    /// одной и следующее же нажатие рисует строкой выше. Оставь мы перенос как
    /// был — под сшитой строкой висело бы брошенное продолжение, а курсор
    /// стоял бы строкой ниже, чем целится оболочка.
    /// </remarks>
    [Fact]
    public void Widening_sews_the_wrapped_input_back_together()
    {
        using var pty = new FakePty { KeepsOwnScreen = true };
        using var session = Wide(pty, 50);

        // На ширине 50 эмулятор перенёс строку сам, как ему и положено.
        pty.Emit(new string('q', 59));

        Assert.True(SpinWait.SpinUntil(() => session.Terminal.Buffer.X == 9, Timeout), "набранное не дошло");

        var buffer = session.Terminal.Buffer;
        var row = buffer.Y - 1;

        session.Resize(120, session.Terminal.Rows);

        Assert.Equal(row, buffer.Y);
        Assert.Equal(59, buffer.X);
        Assert.Equal(new string('q', 59), session.Terminal.GetVisibleLines()[row].TrimEnd());
        Assert.Equal(string.Empty, session.Terminal.GetVisibleLines()[row + 1].TrimEnd());
    }

    /// <summary>
    /// Строку длиннее всего экрана раскладывают по его хвосту.
    /// </summary>
    /// <remarks>
    /// Вставить в оболочку большой кусок текста — обычное дело, и при сужении
    /// такая строка требует больше строк, чем есть в окне. Терминал в этом
    /// случае показывает её конец — тот, где стоит курсор.
    /// </remarks>
    [Fact]
    public void An_input_taller_than_the_screen_shows_its_tail()
    {
        using var pty = new FakePty { KeepsOwnScreen = true };
        using var session = Wide(pty, 60);

        // Пятьсот девяносто пять знаков при ширине 60 — ровно десять строк, все,
        // что есть; при ширине 20 их понадобилось бы тридцать.
        pty.Emit(new string('q', 595));

        Assert.True(
            SpinWait.SpinUntil(() => session.Terminal.Buffer is { X: 55, Y: 9 }, Timeout),
            "набранное не легло в десять строк");

        var buffer = session.Terminal.Buffer;

        session.Resize(20, session.Terminal.Rows);

        // Курсор на последней строке окна, а перед ним — конец набранного.
        Assert.Equal(session.Terminal.Rows - 1, buffer.Y);
        Assert.Equal(15, buffer.X);
        Assert.Equal(new string('q', 15), session.Terminal.GetVisibleLines()[^1].TrimEnd());

        // Показать хвост — не то же самое, что прогнать экран в историю:
        // недостающие строки не отматывают, их просто не показывают.
        Assert.Equal(0, buffer.YBase);
    }

    /// <summary>
    /// Знак, вставший ровно в конец строки, курсор на следующую не переводит.
    /// </summary>
    /// <remarks>
    /// Терминал в этом месте ждёт: курсор остаётся на последнем столбце, и
    /// перенос случится со следующим знаком. Так же считает и ConPTY — а
    /// поставь мы курсор в начало следующей строки, его перерисовка строки
    /// ввода легла бы строкой ниже.
    /// </remarks>
    [Fact]
    public void An_input_filling_the_row_exactly_keeps_the_cursor_on_it()
    {
        using var pty = new FakePty { KeepsOwnScreen = true };
        using var session = Wide(pty, 60);

        pty.Emit(new string('q', 40));

        Assert.True(SpinWait.SpinUntil(() => session.Terminal.Buffer.X == 40, Timeout), "набранное не дошло");

        var buffer = session.Terminal.Buffer;
        var row = buffer.Y;

        // Сорок знаков при ширине 40 занимают строку целиком и ровно.
        session.Resize(40, session.Terminal.Rows);

        Assert.Equal(row, buffer.Y);
        Assert.Equal(39, buffer.X);

        // И следующей строки под них не отводят: продолжения тут нет.
        Assert.False(
            buffer.Lines[buffer.YBase + row + 1]?.IsWrapped,
            "под полной строкой осталось пустое продолжение");
    }

    /// <summary>
    /// Перенос, сделанный нами, живёт дальше: следующее сужение его видит.
    /// </summary>
    /// <remarks>
    /// Признак переноса — единственный след, по которому логическая строка
    /// собирается обратно. Не поставь мы его на новые строки, второе сужение
    /// сочло бы продолжение отдельной строкой и порвало бы набранное надвое.
    /// </remarks>
    [Fact]
    public void The_rows_we_wrapped_stay_marked_for_the_next_resize()
    {
        using var pty = new FakePty { KeepsOwnScreen = true };
        using var session = Wide(pty, 90);

        pty.Emit(new string('q', 80));

        Assert.True(SpinWait.SpinUntil(() => session.Terminal.Buffer.X == 80, Timeout), "набранное не дошло");

        var buffer = session.Terminal.Buffer;
        var row = buffer.Y;

        // Восемьдесят знаков — это две строки при ширине 60 и три при 30.
        session.Resize(60, session.Terminal.Rows);

        Assert.Equal(20, buffer.X);
        Assert.True(buffer.Lines[buffer.YBase + row + 1]?.IsWrapped, "продолжение не помечено переносом");

        session.Resize(30, session.Terminal.Rows);

        Assert.Equal(row + 2, buffer.Y);
        Assert.Equal(20, buffer.X);
        Assert.Equal(new string('q', 20), session.Terminal.GetVisibleLines()[row + 2].TrimEnd());
    }

    /// <summary>
    /// Курсор за концом набранного остаётся за концом и на новой ширине.
    /// </summary>
    /// <remarks>
    /// Пробелы в конце строки — знаки, которых на экране не видно: эмулятор о
    /// них не помнит, а оболочка помнит и рисует по ним. Считай мы длину
    /// строки по одному видимому, курсор после сужения уехал бы к концу
    /// текста, а перерисовка оболочки легла бы мимо него.
    /// </remarks>
    [Fact]
    public void A_cursor_past_the_typed_text_keeps_its_place()
    {
        using var pty = new FakePty { KeepsOwnScreen = true };
        using var session = Wide(pty, 60);

        pty.Emit(new string('q', 30) + new string(' ', 10));

        Assert.True(SpinWait.SpinUntil(() => session.Terminal.Buffer.X == 40, Timeout), "набранное не дошло");

        var buffer = session.Terminal.Buffer;
        var row = buffer.Y;

        // Сорок знаков при ширине 20 — ровно две строки, и курсор ждёт в конце.
        session.Resize(20, session.Terminal.Rows);

        Assert.Equal(row + 1, buffer.Y);
        Assert.Equal(19, buffer.X);
    }

    /// <summary>
    /// Изменение одной высоты строку ввода не трогает — со всем, что в ней есть.
    /// </summary>
    /// <remarks>
    /// Перенос считает ширина: та же ширина — та же раскладка, и перекладывать
    /// нечего. А перекладка не бесплатна: клетки копируются со знаками и
    /// цветом, но без ссылок, которыми оболочки размечают вывод и приглашение.
    /// </remarks>
    [Fact]
    public void A_height_change_leaves_the_input_line_untouched()
    {
        using var pty = new FakePty { KeepsOwnScreen = true };
        using var session = Wide(pty, 60);

        pty.Emit(Esc + "]8;;https://arxis.devссылка" + Esc + "]8;; хвост");

        Assert.True(SpinWait.SpinUntil(() => session.Terminal.Buffer.X == 12, Timeout), "вывод не дошёл");

        var buffer = session.Terminal.Buffer;

        session.Resize(session.Terminal.Cols, session.Terminal.Rows + 4);

        Assert.True(
            buffer.Lines[buffer.YBase + buffer.Y]?.TryGetLinkAt(1, out _),
            "ссылка пропала из строки ввода");
    }

    /// <summary>Псевдотерминалу без своего экрана строки под перенос не отводят.</summary>
    [Fact]
    public void A_plain_pseudo_terminal_gets_no_reserved_rows()
    {
        using var pty = new FakePty();
        using var session = Wide(pty, 60);

        pty.Emit(new string('q', 59));

        Assert.True(SpinWait.SpinUntil(() => session.Terminal.Buffer.X == 59, Timeout), "набранное не дошло");

        var buffer = session.Terminal.Buffer;
        var row = buffer.Y;

        session.Resize(50, session.Terminal.Rows);

        Assert.Equal(row, buffer.Y);
        Assert.Equal(49, buffer.X);
    }

    /// <summary>
    /// Псевдотерминалу без своего экрана окно не поправляют.
    /// </summary>
    /// <remarks>
    /// На POSIX за псевдотерминалом стоит ядро, счёт строк ведёт только
    /// терминал, и подтянутая сверху история — то, чего человек там и ждёт:
    /// растянул окно — увидел больше. Поправка была бы порчей.
    /// </remarks>
    [Fact]
    public void A_plain_pseudo_terminal_keeps_the_emulator_behaviour()
    {
        using var pty = new FakePty();
        using var session = Start(pty);

        Fill(session, pty);

        var buffer = session.Terminal.Buffer;
        var top = buffer.YBase;

        session.Resize(40, session.Terminal.Rows + 6);

        Assert.True(buffer.YBase < top, "историю сверху не подтянули");
        Assert.True(Prompt(session), "курсор ушёл со строки приглашения");
    }

    /// <summary>
    /// Всё прочитанное попадает на экран прежнего размера, а не нового.
    /// </summary>
    /// <remarks>
    /// Байты копятся на фоновом потоке, а размер меняется на потоке
    /// интерфейса: в накопленном есть установки курсора абсолютными
    /// координатами прежнего экрана, и применить их к новому — та же промашка
    /// мимо строки. Поэтому размер меняется только после разбора накопленного.
    /// </remarks>
    [Fact]
    public void Everything_read_lands_on_the_screen_it_was_written_for()
    {
        using var pty = new FakePty { KeepsOwnScreen = true };
        var queue = new List<Action>();

        using var session = new TerminalSession(
            Probe,
            pty,
            TerminalSession.Options(TerminalSettings.Default, 20, 10),
            post: queue.Add);

        // «В последний столбец» — это столбец 19 на экране шириной 20 и столбец
        // 59 на экране шириной 60. Куда встал знак, там байты и применили. А
        // по переплетению строк, в отличие от этого, размера не узнать: строку
        // под курсором эмулятор не переплетает вовсе.
        pty.Emit(Esc + "[999GМ");

        Assert.True(
            SpinWait.SpinUntil(() => queue.Count > 0, Timeout),
            "чтение не дошло до потока интерфейса");

        session.Resize(60, 10);

        Assert.Equal(new string(' ', 19) + "М", session.Terminal.GetVisibleLines()[0].TrimEnd());
        Assert.Equal(20, session.Terminal.Buffer.X);
    }

    /// <summary>
    /// Выход оболочки замечен сразу, а псевдотерминал отпущен чуть позже — после хвоста вывода.
    /// </summary>
    [Fact]
    public void Exit_is_reported_and_the_pty_is_released_after_the_tail()
    {
        using var pty = new FakePty();
        using var session = Start(pty);
        int? exited = null;

        session.Exited += (_, code) => exited = code;

        pty.Exit(3);

        Assert.Equal(3, exited);
        Assert.Equal(3, session.ExitCode);
        Assert.False(session.IsRunning);
        Assert.False(pty.Disposed, "псевдотерминал закрыт до того, как дошёл хвост вывода");

        Assert.True(SpinWait.SpinUntil(() => pty.Disposed, TerminalSession.TailGrace + Timeout), "псевдотерминал не отпущен");
    }

    /// <summary>После закрытия сеанс молчит: ни в трубу, ни на экран.</summary>
    [Fact]
    public void A_disposed_session_sends_nothing()
    {
        var pty = new FakePty();
        var session = Start(pty);

        session.Dispose();

        Assert.True(pty.Disposed);
        Assert.False(session.IsRunning);

        session.SendText("ls");
        session.SendKey(Key.Enter, KeyModifiers.None);
        session.Paste("dir");

        Assert.Equal(string.Empty, pty.WaitForWritten(text => text.Length > 0, TimeSpan.FromMilliseconds(300)));
    }

    /// <summary>
    /// Насыпает истории и ставит приглашение, дожидаясь, пока всё дойдёт.
    /// </summary>
    /// <remarks>
    /// Ждать первого сигнала об изменении нельзя: порций тридцать одна, а
    /// приглашение приходит последним — проверка началась бы на половине.
    /// </remarks>
    private static void Fill(TerminalSession session, FakePty pty)
    {
        for (var i = 0; i < 30; i++)
            pty.Emit($"строка{i}\r\n");

        pty.Emit(Esc + "[32mPS" + Esc + "[0m C:> ");

        Assert.True(
            SpinWait.SpinUntil(() => session.Terminal.Buffer.Y > 0 && Prompt(session), Timeout),
            "приглашение не дошло до экрана");
    }

    /// <summary>Приглашение стоит на строке курсора.</summary>
    private static bool Prompt(TerminalSession session) =>
        session.Terminal.GetVisibleLines()[session.Terminal.Buffer.Y].StartsWith("PS C:>", StringComparison.Ordinal);

    /// <summary>Сеанс заданной ширины: изменение размера проверяется на ширине.</summary>
    private static TerminalSession Wide(FakePty pty, int columns) =>
        new(Probe, pty, TerminalSession.Options(TerminalSettings.Default, columns, 10), post: action => action());

    private static TerminalSession Start(FakePty pty) =>
        new(Probe, pty, TerminalSession.Options(TerminalSettings.Default, 40, 10), post: action => action());
}
