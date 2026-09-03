using System.Globalization;

namespace ArxisStudio.Modules.Terminal.Shells;

/// <summary>
/// Оболочка, которую можно открыть во вкладке: чем её запустить и как назвать.
/// </summary>
/// <remarks>
/// Аргументы хранятся списком, а не строкой: на POSIX они уходят в
/// <c>execvp</c> массивом, и склеивать их, чтобы потом разрезать обратно,
/// значило бы сломать аргумент с пробелом. Строку из них собирает только
/// Windows — там иначе нельзя.
/// </remarks>
/// <param name="Id">Имя профиля: по нему хранится выбор по умолчанию.</param>
/// <param name="Title">Подпись вкладки.</param>
/// <param name="App">Что запускать: имя из <c>PATH</c> или полный путь.</param>
/// <param name="Arguments">С чем запускать.</param>
public sealed record ShellProfile(string Id, string Title, string App, IReadOnlyList<string> Arguments)
{
    /// <summary>Имя профиля SSH: у него нет своего пункта в списке оболочек, он собирается диалогом.</summary>
    public const string SshId = "ssh";

    /// <summary>Это сеанс SSH, а не локальная оболочка.</summary>
    public bool IsSsh => string.Equals(Id, SshId, StringComparison.Ordinal);
}

/// <summary>Платформа, под которую подбираются оболочки.</summary>
public enum TerminalPlatform
{
    /// <summary>Windows: PowerShell и cmd.</summary>
    Windows,

    /// <summary>macOS: оболочка из <c>SHELL</c>, входная.</summary>
    MacOS,

    /// <summary>Linux и прочие POSIX: оболочка из <c>SHELL</c>.</summary>
    Linux,
}

/// <summary>
/// Какие оболочки предлагает меню «новый сеанс» и как собрать команду SSH.
/// </summary>
/// <remarks>
/// Список зависит от платформы, а не от того, что нашлось на диске:
/// Windows PowerShell и cmd есть в любой Windows, а на POSIX оболочка одна —
/// та, что человек выбрал себе в системе. Платформа передаётся параметром,
/// чтобы список для чужой ОС проверялся тестом на этой.
/// </remarks>
public static class ShellCatalog
{
    /// <summary>Профиль Windows PowerShell.</summary>
    public const string WindowsPowerShellId = "windows-powershell";

    /// <summary>Профиль командной строки cmd.</summary>
    public const string CommandPromptId = "cmd";

    /// <summary>Профиль оболочки по умолчанию на POSIX.</summary>
    public const string DefaultShellId = "default";

    /// <summary>Порт SSH, при котором ключ <c>-p</c> не нужен.</summary>
    public const int DefaultSshPort = 22;

    /// <summary>Платформа, на которой идёт студия.</summary>
    public static TerminalPlatform CurrentPlatform =>
        OperatingSystem.IsWindows() ? TerminalPlatform.Windows
        : OperatingSystem.IsMacOS() ? TerminalPlatform.MacOS
        : TerminalPlatform.Linux;

    /// <summary>Оболочки этой машины.</summary>
    public static IReadOnlyList<ShellProfile> Available() =>
        Available(CurrentPlatform, Environment.GetEnvironmentVariable);

    /// <summary>
    /// Оболочки для платформы.
    /// </summary>
    /// <param name="platform">Под какую ОС.</param>
    /// <param name="environment">Откуда брать переменные окружения — <c>SHELL</c>.</param>
    /// <remarks>
    /// На macOS оболочка запускается входной (<c>-l</c>): приложение с рабочего
    /// стола не наследует профиль пользователя, и без этого в терминале не было
    /// бы ни <c>PATH</c> из Homebrew, ни его алиасов. На Linux графическая
    /// сессия профиль уже прочитала, и повторный вход только замедлил бы запуск.
    /// </remarks>
    public static IReadOnlyList<ShellProfile> Available(TerminalPlatform platform, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        switch (platform)
        {
            case TerminalPlatform.Windows:
                return
                [
                    new ShellProfile(WindowsPowerShellId, "Windows PowerShell", "powershell.exe", ["-NoLogo"]),
                    new ShellProfile(CommandPromptId, "Command Prompt", "cmd.exe", []),
                ];

            case TerminalPlatform.MacOS:
            {
                var shell = Shell(environment, "/bin/zsh");

                return [new ShellProfile(DefaultShellId, Path.GetFileName(shell), shell, ["-l"])];
            }

            default:
            {
                var shell = Shell(environment, "/bin/bash");

                return [new ShellProfile(DefaultShellId, Path.GetFileName(shell), shell, [])];
            }
        }
    }

    /// <summary>Оболочка по умолчанию: выбранная человеком, а без выбора — первая в списке.</summary>
    /// <param name="available">Из чего выбирать.</param>
    /// <param name="preferredId">Имя профиля из настроек; пусто — выбора нет.</param>
    public static ShellProfile Default(IReadOnlyList<ShellProfile> available, string? preferredId)
    {
        ArgumentNullException.ThrowIfNull(available);

        if (available.Count == 0)
            throw new ArgumentException("Список оболочек пуст", nameof(available));

        return available.FirstOrDefault(profile => string.Equals(profile.Id, preferredId, StringComparison.Ordinal))
               ?? available[0];
    }

    /// <summary>
    /// Сеанс SSH через системный клиент OpenSSH.
    /// </summary>
    /// <param name="host">Куда подключаться.</param>
    /// <param name="user">От чьего имени; пусто — как решит клиент.</param>
    /// <param name="port">Порт; <see cref="DefaultSshPort"/> в команду не попадает.</param>
    /// <param name="platform">Под какую ОС: в Windows клиент зовётся <c>ssh.exe</c>.</param>
    /// <remarks>
    /// Свой клиент SSH модулю не нужен: OpenSSH стоит в Windows с 2018 года и
    /// есть в каждой POSIX-системе, а ключи, агент и <c>known_hosts</c> у
    /// человека уже настроены именно под него.
    /// </remarks>
    public static ShellProfile Ssh(string host, string? user, int port, TerminalPlatform platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var target = string.IsNullOrWhiteSpace(user) ? host.Trim() : $"{user.Trim()}@{host.Trim()}";
        var arguments = new List<string>();

        if (port > 0 && port != DefaultSshPort)
        {
            arguments.Add("-p");
            arguments.Add(port.ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add(target);

        return new ShellProfile(
            ShellProfile.SshId,
            $"ssh {target}",
            platform == TerminalPlatform.Windows ? "ssh.exe" : "ssh",
            arguments);
    }

    private static string Shell(Func<string, string?> environment, string fallback) =>
        environment("SHELL") is { Length: > 0 } shell ? shell : fallback;
}
