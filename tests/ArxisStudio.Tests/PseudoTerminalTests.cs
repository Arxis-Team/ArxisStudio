using System.Text;
using ArxisStudio.Modules.Terminal.Pty;
using ArxisStudio.Modules.Terminal.Shells;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Настоящий псевдотерминал с настоящей оболочкой.
/// </summary>
/// <remarks>
/// Единственный тест модуля, который запускает процесс: всё остальное живёт
/// на трубе в памяти. Он нужен, потому что библиотека псевдотерминала —
/// чужая, и её договор проверяется опытом: окружение доходит снимком,
/// аргументы — нетронутыми, выход замечен, чтение отпущено. Идёт только там,
/// где есть <c>cmd.exe</c>; на POSIX его вариант появится вместе с машиной, на
/// которой его можно прогнать.
/// </remarks>
public class PseudoTerminalTests
{
    /// <summary>Переменная, которую тест ставит себе и ищет в выводе оболочки.</summary>
    private const string Marker = "ARXIS_TERMINAL_PROBE";

    /// <summary>Переменная, которую студия ставит себе после снимка, — как регистрация MSBuild.</summary>
    private const string Late = "ARXIS_TERMINAL_LATE";

    /// <summary>Переменная, которую студия меняет после снимка.</summary>
    private const string Changed = "ARXIS_TERMINAL_CHANGED";

    /// <summary>Есть ли на этой машине cmd — то есть Windows.</summary>
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>
    /// Оболочка отвечает, получает снимок окружения студии и наши переменные, и кончается с кодом.
    /// </summary>
    /// <remarks>
    /// Окружение проверяется с трёх сторон, и все три важны. Снятое окружение
    /// студии оболочка получает — от этого зависит всё, от <c>PATH</c> до
    /// домашней папки. Поставленного студией после снимка не получает, а
    /// изменённое получает прежним: так она уберегается от регистрации MSBuild.
    /// Держится это на поведении библиотеки — окружение она собирает из процесса
    /// сама, а переменную, отданную ей пустой, убирает, — и библиотека молодая:
    /// договор с ней стоит держать пришитым. <c>COLORTERM</c> добавляем мы: по
    /// нему программы решают, слать ли цвет.
    /// </remarks>
    [Fact(Skip = "Нужен cmd.exe: тест идёт только в Windows", SkipUnless = nameof(IsWindows), SkipType = typeof(PseudoTerminalTests))]
    public async Task A_real_shell_answers_through_the_pseudo_terminal()
    {
        Environment.SetEnvironmentVariable(Marker, "terminal-probe");
        Environment.SetEnvironmentVariable(Changed, "before");

        var snapshot = ShellEnvironment.Capture();

        // После снимка студия меняет своё окружение — так его меняет регистрация MSBuild.
        Environment.SetEnvironmentVariable(Late, "studio-only");
        Environment.SetEnvironmentVariable(Changed, "after");

        var profile = new ShellProfile(
            "probe", "Проба", "cmd.exe", ["/c", "echo", $"%{Marker}%-%COLORTERM%-[%{Late}%]-[%{Changed}%]", "&", "exit", "7"]);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var pty = await PortaPseudoTerminal.StartAsync(profile, Path.GetTempPath(), snapshot, 80, 24, cancellation.Token);

        // Оболочка уже со своим окружением, и студии пробные переменные больше не нужны.
        Environment.SetEnvironmentVariable(Late, null);
        Environment.SetEnvironmentVariable(Changed, null);

        // За псевдотерминалом Windows стоит консоль со своим буфером, и от
        // этого зависит поправка окна при изменении размера: без признака
        // терминал растил бы экран не туда, куда его растит ConPTY.
        Assert.True(pty.KeepsOwnScreen, "ConPTY не признан хозяином своего экрана");

        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new StringBuilder();

        pty.Exited += (_, code) => exited.TrySetResult(code);

        var reader = Task.Run(() =>
        {
            var buffer = new byte[4096];

            try
            {
                while (true)
                {
                    var read = pty.Output.Read(buffer, 0, buffer.Length);

                    if (read <= 0)
                        return;

                    lock (output)
                        output.Append(Encoding.UTF8.GetString(buffer, 0, read));
                }
            }
            catch (IOException)
            {
                // Псевдотерминал закрыт — так и кончается чтение.
            }
        }, cancellation.Token);

        var code = await exited.Task.WaitAsync(cancellation.Token);

        Assert.Equal(7, code);
        Assert.Equal(7, pty.ExitCode);

        // Хвост вывода приходит после события о выходе — ждём его, как сеанс.
        await Task.Delay(300, cancellation.Token);

        // Ушедшей оболочке ни размер, ни байты не нужны, но и падать из-за неё нельзя.
        pty.Resize(100, 30);
        pty.Write("dir\r"u8);

        pty.Dispose();

        await reader.WaitAsync(cancellation.Token);

        string text;

        lock (output)
            text = output.ToString();

        // Оболочка подставила пробу из снятого окружения студии и COLORTERM из
        // того, что терминал добавляет от себя. Поставленной после снимка
        // переменной у неё нет вовсе — cmd оставил её имя как есть, — а
        // изменённая пришла такой, какой была при снимке.
        Assert.Contains($"terminal-probe-truecolor-[%{Late}%]-[before]", text, StringComparison.Ordinal);
        Assert.DoesNotContain($"%{Marker}%", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Запись, застрявшая в трубе, не держит ни смену размера, ни закрытие.
    /// </summary>
    /// <remarks>
    /// Запись шла под тем же замком, что смена размера и закрытие, а пишет она блокирующе: оболочка, которая не
    /// читает ввод, — долгая сборка, <c>ping -t</c>, — останавливает её на заполненной трубе. Вставка большого
    /// текста, затем тяга границы панели или закрытие вкладки — и поток интерфейса стоял на замке, пока оболочка
    /// не прочтёт (запись 386). На Linux очередь ввода терминала — четыре килобайта; в Windows трубу вычерпывает
    /// conhost, поэтому застрявшую запись ставит подставное соединение — процесса тест не запускает.
    /// </remarks>
    [Fact]
    public async Task A_write_stuck_in_the_pipe_holds_neither_resize_nor_close()
    {
        var cancel = TestContext.Current.CancellationToken;
        var connection = new StuckConnection();
        var pty = new PortaPseudoTerminal(connection);
        var writing = Task.Run(() => pty.Write("вставка"u8), cancel);

        try
        {
            Assert.True(connection.Entered.Wait(TimeSpan.FromSeconds(10), cancel), "запись не дошла до трубы");
            Assert.True(await Finishes(Task.Run(() => pty.Resize(100, 30), cancel), 2), "смена размера ждёт застрявшую запись");
            Assert.Equal(1, connection.Resized);
            Assert.True(await Finishes(Task.Run(pty.Dispose, cancel), 2), "закрытие ждёт застрявшую запись");
            Assert.True(await Finishes(writing, 10), "застрявшая запись не вышла с закрытием соединения");
        }
        finally
        {
            connection.Release.Set();
        }
    }

    /// <summary>Кончилось ли дело за столько секунд.</summary>
    private static async Task<bool> Finishes(Task task, int seconds)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(seconds), TestContext.Current.CancellationToken);

            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Соединение, чья труба ввода не читается: запись в неё стоит, пока соединение не закроют.</summary>
    private sealed class StuckConnection : Porta.Pty.IPtyConnection
    {
        public StuckConnection() => WriterStream = new StuckStream(this);

        /// <summary>Запись дошла до трубы и встала.</summary>
        public ManualResetEventSlim Entered { get; } = new();

        /// <summary>Отпускает стоящую запись — как закрытая труба.</summary>
        public ManualResetEventSlim Release { get; } = new();

        /// <summary>Сколько раз меняли размер.</summary>
        public int Resized { get; private set; }

        public Stream ReaderStream { get; } = new MemoryStream();

        public Stream WriterStream { get; }

        public bool SupportsCancellableRead => false;

        public int Pid => 0;

        public int ExitCode => 0;

        public event EventHandler<Porta.Pty.PtyExitedEventArgs>? ProcessExited
        {
            add { }
            remove { }
        }

        public bool WaitForExit(int milliseconds) => false;

        public void Kill()
        {
        }

        public void Resize(int cols, int rows) => Resized++;

        public void Dispose() => Release.Set();
    }

    /// <summary>Труба ввода, которую никто не читает: запись встаёт и падает, когда трубу закрыли.</summary>
    private sealed class StuckStream(StuckConnection owner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            owner.Entered.Set();
            owner.Release.Wait();

            throw new IOException("труба закрыта");
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
