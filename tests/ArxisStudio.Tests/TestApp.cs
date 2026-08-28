using ArxisStudio.Tests;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace ArxisStudio.Tests;

/// <summary>Headless-приложение для тестов, которым нужно живое дерево контролов.</summary>
public class TestApp : Application
{
    /// <summary>
    /// Собирает headless-приложение.
    /// </summary>
    /// <remarks>
    /// Рисование настоящее, а не headless-заглушка: заглушка не зовёт декодер
    /// картинок и на любой файл отвечает болванкой нужного размера — тест
    /// «значок прочитался» проходил бы и на текстовом файле.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<TestApp>()
        .WithInterFont()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    /// <inheritdoc/>
    public override void Initialize() => Styles.Add(new FluentTheme());
}
