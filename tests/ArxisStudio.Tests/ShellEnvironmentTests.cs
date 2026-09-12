using ArxisStudio.Modules.Terminal.Pty;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Окружение оболочки — снимок студии, а не то, что студия сделала со своим окружением потом.
/// </summary>
/// <remarks>
/// Разность считается на готовых наборах, без процесса и без его окружения. Что из неё выходит
/// на настоящей оболочке, проверяет <c>PseudoTerminalTests</c>: убирает ли переменную пустое
/// значение, решает библиотека псевдотерминала.
/// </remarks>
public class ShellEnvironmentTests
{
    /// <summary>
    /// Появившееся после снимка убирается, изменённое и пропавшее возвращается, нетронутое не упоминается.
    /// </summary>
    [Fact]
    public void The_shell_gets_the_snapshot_back_over_what_the_studio_changed_since()
    {
        var snapshot = ShellEnvironment.From(new Dictionary<string, string>
        {
            ["PATH"] = "before",
            ["HOME"] = "home",
            ["GONE"] = "was",
        });

        // Так окружение студии выглядит после регистрации MSBuild и чужих правок.
        var overlay = snapshot.Overlay(new Dictionary<string, string>
        {
            ["PATH"] = "after",
            ["HOME"] = "home",
            ["MSBuildSDKsPath"] = "sdk",
        });

        Assert.Equal(
            [("GONE", "was"), ("MSBuildSDKsPath", ""), ("PATH", "before")],
            overlay.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value)));
    }
}
