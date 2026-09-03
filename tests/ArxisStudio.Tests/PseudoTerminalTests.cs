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
/// чужая, и её договор проверяется опытом: окружение наследуется, аргументы
/// доходят нетронутыми, выход замечен, чтение отпущено. Идёт только там, где
/// есть <c>cmd.exe</c>; на POSIX его вариант появится вместе с машиной, на
/// которой его можно прогнать.
/// </remarks>
public class PseudoTerminalTests
{
    /// <summary>Переменная, которую тест ставит себе и ищет в выводе оболочки.</summary>
    private const string Marker = "ARXIS_TERMINAL_PROBE";

    /// <summary>Есть ли на этой машине cmd — то есть Windows.</summary>
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>
    /// Оболочка отвечает, видит окружение студии и наши переменные, и кончается с кодом.
    /// </summary>
    /// <remarks>
    /// Окружение проверяется с двух сторон, и обе важны. Переменные студии
    /// оболочка наследует — это делает библиотека, и от этого зависит всё, от
    /// <c>PATH</c> до домашней папки; библиотека молодая, и договор с ней стоит
    /// держать пришитым. <c>COLORTERM</c> добавляем мы: по нему программы
    /// решают, слать ли цвет.
    /// </remarks>
    [Fact(Skip = "Нужен cmd.exe: тест идёт только в Windows", SkipUnless = nameof(IsWindows), SkipType = typeof(PseudoTerminalTests))]
    public async Task A_real_shell_answers_through_the_pseudo_terminal()
    {
        Environment.SetEnvironmentVariable(Marker, "terminal-probe");

        var profile = new ShellProfile(
            "probe", "Проба", "cmd.exe", ["/c", "echo", $"%{Marker}%-%COLORTERM%", "&", "exit", "7"]);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var pty = await PortaPseudoTerminal.StartAsync(profile, Path.GetTempPath(), 80, 24, cancellation.Token);

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

        // Оболочка подставила обе переменные: нашу — унаследовав окружение
        // студии, COLORTERM — из того, что терминал добавляет от себя.
        Assert.Contains("terminal-probe-truecolor", text, StringComparison.Ordinal);
        Assert.DoesNotContain($"%{Marker}%", text, StringComparison.Ordinal);
    }
}
