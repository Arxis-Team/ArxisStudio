using ArxisStudio.Modules.Terminal;
using ArxisStudio.Modules.Terminal.Pty;
using ArxisStudio.Modules.Terminal.Shells;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Оболочки терминала: что предлагает меню и как собирается команда.
/// </summary>
/// <remarks>
/// Список зависит от платформы, а машина у теста одна, поэтому платформа
/// передаётся параметром: правила для macOS проверяются на Windows и наоборот.
/// </remarks>
public class ShellCatalogTests
{
    private static readonly Func<string, string?> NoEnvironment = _ => null;

    /// <summary>В Windows — PowerShell и cmd, оба из системы, без поиска по диску.</summary>
    [Fact]
    public void Windows_offers_powershell_and_cmd()
    {
        var shells = ShellCatalog.Available(TerminalPlatform.Windows, NoEnvironment);

        Assert.Equal([ShellCatalog.WindowsPowerShellId, ShellCatalog.CommandPromptId], shells.Select(shell => shell.Id));
        Assert.All(shells, shell => Assert.EndsWith(".exe", shell.App, StringComparison.Ordinal));
        Assert.Contains("-NoLogo", shells[0].Arguments);
        Assert.Empty(shells[1].Arguments);
        Assert.All(shells, shell => Assert.False(shell.IsSsh));
    }

    /// <summary>
    /// На macOS оболочка из <c>SHELL</c> запускается входной.
    /// </summary>
    /// <remarks>
    /// Приложение с рабочего стола не наследует профиль пользователя: без
    /// <c>-l</c> в терминале не было бы <c>PATH</c> из Homebrew.
    /// </remarks>
    [Fact]
    public void MacOS_runs_the_login_shell_from_the_environment()
    {
        var shell = Assert.Single(ShellCatalog.Available(TerminalPlatform.MacOS, name => name == "SHELL" ? "/opt/homebrew/bin/fish" : null));

        Assert.Equal("/opt/homebrew/bin/fish", shell.App);
        Assert.Equal("fish", shell.Title);
        Assert.Equal(["-l"], shell.Arguments);
        Assert.Equal(ShellCatalog.DefaultShellId, shell.Id);
    }

    /// <summary>Без <c>SHELL</c> macOS даёт zsh, Linux — bash; Linux входа не требует.</summary>
    [Fact]
    public void Posix_falls_back_to_the_system_shell()
    {
        var mac = Assert.Single(ShellCatalog.Available(TerminalPlatform.MacOS, NoEnvironment));
        var linux = Assert.Single(ShellCatalog.Available(TerminalPlatform.Linux, name => name == "SHELL" ? "" : null));

        Assert.Equal("/bin/zsh", mac.App);
        Assert.Equal("/bin/bash", linux.App);
        Assert.Equal("bash", linux.Title);
        Assert.Empty(linux.Arguments);
    }

    /// <summary>Выбор человека уважается, а незнакомое имя и пустой выбор дают первую оболочку.</summary>
    [Fact]
    public void The_default_shell_is_the_chosen_one_or_the_first()
    {
        var shells = ShellCatalog.Available(TerminalPlatform.Windows, NoEnvironment);

        Assert.Equal(ShellCatalog.CommandPromptId, ShellCatalog.Default(shells, ShellCatalog.CommandPromptId).Id);
        Assert.Equal(ShellCatalog.WindowsPowerShellId, ShellCatalog.Default(shells, "нет.такой").Id);
        Assert.Equal(ShellCatalog.WindowsPowerShellId, ShellCatalog.Default(shells, null).Id);
        Assert.Equal(ShellCatalog.WindowsPowerShellId, ShellCatalog.Default(shells, string.Empty).Id);
    }

    /// <summary>
    /// Чистить экран умеют сами все, кроме <c>cmd</c>.
    /// </summary>
    /// <remarks>
    /// Признак решает, кому уйдёт Ctrl+L, а за кого будет чистить терминал.
    /// Ошибись он в сторону cmd — человек нажимал бы «очистить» и не видел
    /// ничего; в другую сторону — терминал чистил бы свою копию экрана, разойдясь
    /// с той, которую держит ConPTY.
    /// </remarks>
    [Fact]
    public void Every_shell_but_cmd_clears_its_own_screen()
    {
        var windows = ShellCatalog.Available(TerminalPlatform.Windows, NoEnvironment);

        Assert.True(windows.Single(shell => shell.Id == ShellCatalog.WindowsPowerShellId).ClearsItself);
        Assert.False(windows.Single(shell => shell.Id == ShellCatalog.CommandPromptId).ClearsItself);

        Assert.True(Assert.Single(ShellCatalog.Available(TerminalPlatform.MacOS, NoEnvironment)).ClearsItself);
        Assert.True(Assert.Single(ShellCatalog.Available(TerminalPlatform.Linux, NoEnvironment)).ClearsItself);
        Assert.True(ShellCatalog.Ssh("host", null, 22, TerminalPlatform.Linux).ClearsItself);
    }

    /// <summary>Команда SSH: адрес с пользователем, порт только нестандартный, клиент — системный.</summary>
    [Fact]
    public void Ssh_builds_the_system_client_command()
    {
        var plain = ShellCatalog.Ssh(" host.example ", " maxim ", ShellCatalog.DefaultSshPort, TerminalPlatform.Windows);
        var custom = ShellCatalog.Ssh("host.example", null, 2222, TerminalPlatform.Linux);

        Assert.Equal("ssh.exe", plain.App);
        Assert.Equal(["maxim@host.example"], plain.Arguments);
        Assert.Equal("ssh maxim@host.example", plain.Title);
        Assert.True(plain.IsSsh);

        Assert.Equal("ssh", custom.App);
        Assert.Equal(["-p", "2222", "host.example"], custom.Arguments);
    }

    /// <summary>Порт из поля: число в границах, всё остальное — 22.</summary>
    [Theory]
    [InlineData("2222", 2222)]
    [InlineData(" 22 ", 22)]
    [InlineData("", 22)]
    [InlineData(null, 22)]
    [InlineData("abc", 22)]
    [InlineData("0", 22)]
    [InlineData("70000", 22)]
    public void The_port_field_is_read_defensively(string? text, int expected) =>
        Assert.Equal(expected, SshDialog.Port(text));

    /// <summary>
    /// Командная строка Windows разбирается обратно в те же аргументы.
    /// </summary>
    /// <remarks>
    /// Правила <c>CommandLineToArgvW</c>: кавычки только там, где нужны,
    /// косые удваиваются только перед кавычкой, пустой аргумент не пропадает.
    /// </remarks>
    [Theory]
    [InlineData("-NoLogo", "-NoLogo")]
    [InlineData("echo hi", "\"echo hi\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]
    [InlineData("C:\\dir with space\\", "\"C:\\dir with space\\\\\"")]
    [InlineData("C:\\plain\\", "C:\\plain\\")]
    [InlineData("", "\"\"")]
    public void Arguments_are_quoted_the_way_windows_parses_them(string argument, string expected) =>
        Assert.Equal(expected, CommandLine.Quote(argument));

    /// <summary>Аргументы склеиваются пробелом, и <c>cmd /c</c> получает свою команду нетронутой.</summary>
    [Fact]
    public void Arguments_join_with_spaces() =>
        Assert.Equal("/c \"echo hi\" plain", CommandLine.Join(["/c", "echo hi", "plain"]));

    /// <summary>Набранное в диалоге настроек: числа в границах, мусор оставляет прежнее.</summary>
    [Fact]
    public void Settings_typed_by_hand_are_parsed_and_clamped()
    {
        var before = new TerminalSettings("cmd", 13, 5000, true);

        var parsed = SettingsDialog.Parse("16", "1000", ShellCatalog.WindowsPowerShellId, false, before);
        var garbage = SettingsDialog.Parse("abc", "many", "cmd", true, before);
        var extreme = SettingsDialog.Parse("4", "99999999", "cmd", true, before);

        Assert.Equal(new TerminalSettings(ShellCatalog.WindowsPowerShellId, 16, 1000, false), parsed);
        Assert.Equal(before, garbage);
        Assert.Equal(TerminalSettings.MinFontSize, extreme.FontSize);
        Assert.Equal(TerminalSettings.MaxScrollback, extreme.Scrollback);
    }

    /// <summary>Границы настроек: ноль и мусор — умолчание, а не пустой экран.</summary>
    [Fact]
    public void Settings_bounds_protect_against_a_hand_edited_file()
    {
        Assert.Equal(TerminalSettings.DefaultFontSize, TerminalSettings.ClampFontSize(0));
        Assert.Equal(TerminalSettings.DefaultFontSize, TerminalSettings.ClampFontSize(double.NaN));
        Assert.Equal(TerminalSettings.MaxFontSize, TerminalSettings.ClampFontSize(400));
        Assert.Equal(TerminalSettings.DefaultScrollback, TerminalSettings.ClampScrollback(-1));
        Assert.Equal(TerminalSettings.DefaultScrollback, TerminalSettings.ClampScrollback(0));
        Assert.Equal(1000, TerminalSettings.ClampScrollback(1000));
    }
}
