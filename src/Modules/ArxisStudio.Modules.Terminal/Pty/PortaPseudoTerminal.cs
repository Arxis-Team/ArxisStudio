using System.Text;
using ArxisStudio.Modules.Terminal.Shells;
using Porta.Pty;

namespace ArxisStudio.Modules.Terminal.Pty;

/// <summary>
/// Псевдотерминал на Porta.Pty: ConPTY в Windows, <c>forkpty</c> на Linux и macOS.
/// </summary>
/// <remarks>
/// Библиотека взята, а не написана, ради POSIX: <c>fork</c> из многопоточного
/// процесса .NET небезопасен, и честный псевдотерминал там требует нативной
/// прослойки под каждую платформу. У Porta.Pty она есть под четыре, а в
/// Windows она идёт через официальный пакет ConPTY от Microsoft — консоль
/// едет рядом со студией и не зависит от версии системы.
/// <para>
/// Две особенности библиотеки, узнанные опытом, а не из документации. В
/// Windows аргументы уходят одной строкой, и цитировать их надо самим:
/// цитирование библиотеки ломает <c>cmd /c</c>. И код выхода нужно забрать до
/// <see cref="Dispose"/> — после него процесса уже нет.
/// </para>
/// </remarks>
public sealed class PortaPseudoTerminal : IPseudoTerminal
{
    /// <summary>Что терминал говорит о себе оболочке: имя из базы terminfo.</summary>
    public const string TerminalName = "xterm-256color";

    private readonly IPtyConnection _connection;
    private readonly Lock _gate = new();
    private int? _exitCode;
    private bool _disposed;

    private PortaPseudoTerminal(IPtyConnection connection)
    {
        _connection = connection;
        _connection.ProcessExited += OnExited;
    }

    /// <inheritdoc/>
    public Stream Output => _connection.ReaderStream;

    /// <inheritdoc/>
    public int? ExitCode => _exitCode;

    /// <inheritdoc/>
    public event EventHandler<int>? Exited;

    /// <summary>
    /// Запускает оболочку в новом псевдотерминале.
    /// </summary>
    /// <param name="profile">Какую оболочку.</param>
    /// <param name="workingDirectory">Где: папка проекта или домашняя.</param>
    /// <param name="columns">Ширина окна в знаках.</param>
    /// <param name="rows">Высота в строках.</param>
    /// <param name="cancellationToken">Отмена запуска.</param>
    public static async Task<PortaPseudoTerminal> StartAsync(
        ShellProfile profile, string workingDirectory, int columns, int rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var windows = OperatingSystem.IsWindows();

        var options = new PtyOptions
        {
            Name = TerminalName,
            App = profile.App,
            // В Windows аргументы уходят одной строкой, собранной здесь;
            // на POSIX — массивом, как их и принимает execvp.
            CommandLine = windows ? [CommandLine.Join(profile.Arguments)] : [.. profile.Arguments],
            VerbatimCommandLine = windows,
            Cwd = workingDirectory,
            Cols = Math.Max(1, columns),
            Rows = Math.Max(1, rows),
            Environment = TerminalEnvironment(windows),
        };

        var connection = await PtyProvider.SpawnAsync(options, cancellationToken).ConfigureAwait(false);

        return new PortaPseudoTerminal(connection);
    }

    /// <summary>
    /// Что терминал добавляет к окружению оболочки.
    /// </summary>
    /// <remarks>
    /// Только добавляет: окружение студии библиотека копирует сама и кладёт
    /// этот словарь поверх — на всех трёх платформах. Переписывать её работу
    /// значило бы держать вторую копию правил о том, что оболочка должна
    /// унаследовать.
    /// <para>
    /// <c>TERM</c> и <c>COLORTERM</c> — по ним программы решают, слать ли цвета
    /// и какие: без них <c>ls</c> на удалённой машине был бы серым, а <c>vim</c>
    /// не знал бы, что курсорные клавиши — это xterm. <c>TERM_PROGRAM</c> —
    /// вежливость: по нему оболочка узнаёт, в чьём окне идёт.
    /// </para>
    /// </remarks>
    private static Dictionary<string, string> TerminalEnvironment(bool windows) =>
        new(windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        {
            ["TERM"] = TerminalName,
            ["COLORTERM"] = "truecolor",
            ["TERM_PROGRAM"] = "ArxisStudio",
        };

    /// <inheritdoc/>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            if (_disposed || _exitCode is not null)
                return;

            try
            {
                _connection.WriterStream.Write(bytes);
                _connection.WriterStream.Flush();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                // Оболочка ушла между проверкой и записью — писать больше некому.
            }
        }
    }

    /// <inheritdoc/>
    public void Resize(int columns, int rows)
    {
        lock (_gate)
        {
            if (_disposed || _exitCode is not null)
                return;

            try
            {
                _connection.Resize(Math.Max(1, columns), Math.Max(1, rows));
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
            {
                // Размер ушедшей оболочке не нужен.
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _connection.ProcessExited -= OnExited;

        // Закрытие убивает и оболочку, и всё, что она породила: в Windows за
        // это отвечает объект задания, на POSIX — закрытый управляющий
        // дескриптор. Одновременно освобождается читатель вывода.
        _connection.Dispose();
    }

    private void OnExited(object? sender, PtyExitedEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _exitCode = e.ExitCode;
        }

        Exited?.Invoke(this, e.ExitCode);
    }
}

/// <summary>
/// Командная строка Windows из списка аргументов.
/// </summary>
/// <remarks>
/// Правила те же, что у <c>CommandLineToArgvW</c>: аргумент с пробелом или
/// кавычкой берётся в кавычки, кавычка внутри экранируется, а обратные косые
/// удваиваются только перед кавычкой. Пустой аргумент — пара кавычек, иначе он
/// пропал бы вовсе.
/// </remarks>
public static class CommandLine
{
    /// <summary>Склеивает аргументы в строку, которую программа разберёт обратно в те же аргументы.</summary>
    /// <param name="arguments">Что склеивать.</param>
    public static string Join(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return string.Join(' ', arguments.Select(Quote));
    }

    /// <summary>Один аргумент в виде, пригодном для командной строки.</summary>
    /// <param name="argument">Аргумент.</param>
    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        if (argument.Length > 0 && !argument.Any(c => c is ' ' or '\t' or '\n' or '"'))
            return argument;

        var result = new StringBuilder("\"");
        var backslashes = 0;

        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                // Косые перед кавычкой удваиваются, сама кавычка экранируется.
                result.Append('\\', (backslashes * 2) + 1);
                result.Append('"');
                backslashes = 0;
                continue;
            }

            result.Append('\\', backslashes);
            result.Append(c);
            backslashes = 0;
        }

        // Косые перед закрывающей кавычкой — тоже удвоить, иначе они съедят её.
        result.Append('\\', backslashes * 2);
        result.Append('"');

        return result.ToString();
    }
}
