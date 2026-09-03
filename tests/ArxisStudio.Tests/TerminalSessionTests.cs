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

    private static TerminalSession Start(FakePty pty) =>
        new(Probe, pty, TerminalSession.Options(TerminalSettings.Default, 40, 10), post: action => action());
}
