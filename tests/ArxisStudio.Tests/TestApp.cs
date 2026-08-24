using ArxisStudio.Tests;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace ArxisStudio.Tests;

/// <summary>Headless-приложение для тестов, которым нужно живое дерево контролов.</summary>
public class TestApp : Application
{
    /// <summary>Собирает headless-приложение.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<TestApp>()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    /// <inheritdoc/>
    public override void Initialize() => Styles.Add(new FluentTheme());
}
