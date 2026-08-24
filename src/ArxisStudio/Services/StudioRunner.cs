using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Threading;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Services;

/// <summary>
/// Собирает и запускает открытый проект, пересказывая его вывод в журнал.
/// </summary>
/// <remarks>
/// Модель решения умеет вычислять проект, но не собирать его: сборка — работа
/// самого SDK, и делается она запуском <c>dotnet</c>. Зато что запускать после
/// сборки, модель знает точно — путь к выходной сборке лежит в снапшоте, и
/// угадывать его по папкам не приходится.
/// <para>
/// Вывод читается построчно и уходит в журнал по мере появления: показывать его
/// целиком в конце значило бы оставить человека без единого признака жизни на
/// всё время сборки.
/// </para>
/// </remarks>
public sealed class StudioRunner(StudioLog log) : IDisposable
{
    private Process? _running;

    /// <summary>Запущенный процесс жив.</summary>
    public bool IsRunning => _running is { HasExited: false };

    /// <summary>Состояние запуска изменилось.</summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Собирает проект.
    /// </summary>
    /// <param name="projectPath">Путь к решению или проекту.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Удалась ли сборка.</returns>
    public async Task<bool> BuildAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        log.Write(StudioLogLevel.Info, "Build", $"Сборка {Path.GetFileName(projectPath)}…");

        var exitCode = await RunToEndAsync(
            "dotnet",
            $"build \"{projectPath}\" --nologo",
            Path.GetDirectoryName(projectPath)!,
            "Build",
            cancellationToken);

        if (exitCode == 0)
            log.Write(StudioLogLevel.Info, "Build", "Сборка успешна");
        else
            log.Write(StudioLogLevel.Error, "Build", $"Сборка не удалась (код {exitCode})");

        return exitCode == 0;
    }

    /// <summary>
    /// Собирает и запускает проект; вывод приложения идёт в журнал.
    /// </summary>
    /// <param name="snapshot">Снапшот решения.</param>
    /// <param name="projectPath">Путь к решению или проекту.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Запустилось ли приложение.</returns>
    public async Task<bool> RunAsync(
        SolutionSnapshot snapshot,
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (IsRunning)
        {
            log.Write(StudioLogLevel.Warning, "Run", "Приложение уже запущено");
            return false;
        }

        if (!await BuildAsync(projectPath, cancellationToken))
            return false;

        if (FindStartup(snapshot) is not { } assembly)
        {
            log.Write(StudioLogLevel.Error, "Run", "В решении нет проекта, который можно запустить");
            return false;
        }

        log.Write(StudioLogLevel.Info, "Run", $"Запуск {Path.GetFileName(assembly)}");

        var process = Create("dotnet", $"\"{assembly}\"", Path.GetDirectoryName(assembly)!);

        process.OutputDataReceived += (_, e) => Line(StudioLogLevel.Info, "App", e.Data);
        process.ErrorDataReceived += (_, e) => Line(StudioLogLevel.Error, "App", e.Data);
        // О завершении сообщает поток пула, а слушают его журнал и кнопки —
        // и то и другое живёт на потоке интерфейса.
        process.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            log.Write(StudioLogLevel.Info, "Run", $"Приложение завершилось (код {ExitCodeOf(process)})");
            _running = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        });

        process.EnableRaisingEvents = true;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _running = process;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Останавливает запущенное приложение.</summary>
    public void Stop()
    {
        if (_running is not { HasExited: false } process)
            return;

        try
        {
            process.Kill(entireProcessTree: true);
            log.Write(StudioLogLevel.Info, "Run", "Приложение остановлено");
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            log.Write(StudioLogLevel.Warning, "Run", $"Остановить не удалось: {e.Message}");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Stop();
        _running?.Dispose();
        _running = null;
    }

    /// <summary>
    /// Находит сборку, которую имеет смысл запускать.
    /// </summary>
    /// <remarks>
    /// Запускаемым считается проект, для которого SDK выпустил исполняемый файл;
    /// если таких несколько, берётся первый — выбор стартового проекта придёт
    /// вместе с конфигурациями запуска.
    /// </remarks>
    private static string? FindStartup(SolutionSnapshot snapshot) =>
        snapshot.Projects
            .Select(project => project.Outputs
                .FirstOrDefault(output => output.Kind == OutputArtifactKind.Assembly)?.Path.Value)
            .FirstOrDefault(path => path is { Length: > 0 } && File.Exists(path));

    private async Task<int> RunToEndAsync(
        string file,
        string arguments,
        string workingDirectory,
        string source,
        CancellationToken cancellationToken)
    {
        using var process = Create(file, arguments, workingDirectory);

        process.OutputDataReceived += (_, e) => Line(StudioLogLevel.Info, source, e.Data);
        process.ErrorDataReceived += (_, e) => Line(StudioLogLevel.Error, source, e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private void Line(StudioLogLevel level, string source, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Читается вывод не на потоке интерфейса, а журнал показывает панель.
        Dispatcher.UIThread.Post(() => log.Write(Classify(level, text), source, text));
    }

    /// <summary>
    /// Разбирает уровень строки сборки: <c>dotnet</c> пишет и ошибки, и
    /// предупреждения в обычный поток вывода.
    /// </summary>
    private static StudioLogLevel Classify(StudioLogLevel level, string text) =>
        text.Contains(" error ", StringComparison.OrdinalIgnoreCase) ? StudioLogLevel.Error
        : text.Contains(" warning ", StringComparison.OrdinalIgnoreCase) ? StudioLogLevel.Warning
        : level;

    /// <summary>
    /// Готовит процесс, чей вывод мы собираемся читать.
    /// </summary>
    /// <remarks>
    /// Кодировка задаётся явно: и <c>dotnet</c>, и приложение пишут в UTF-8, а
    /// консоль Windows по умолчанию читается однобайтовой кодовой страницей — и
    /// русские сообщения сборки превращаются в мусор.
    /// </remarks>
    private static Process Create(string file, string arguments, string workingDirectory) => new()
    {
        StartInfo = new ProcessStartInfo(file, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment = { ["DOTNET_CLI_UI_LANGUAGE"] = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName },
        },
    };

    private static string ExitCodeOf(Process process)
    {
        try
        {
            return process.ExitCode.ToString();
        }
        catch (InvalidOperationException)
        {
            return "?";
        }
    }
}
