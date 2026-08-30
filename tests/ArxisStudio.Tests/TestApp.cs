using ArxisStudio.Docking;
using ArxisStudio.Tests;
using ArxisStudio.Themes.Arxis;
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

    /// <summary>
    /// Ставит те же слои стилей, что и студия.
    /// </summary>
    /// <remarks>
    /// Один Fluent тестам мало: у контролов студии тема живёт в ArxisTheme, и
    /// без неё окно инструментов остаётся без шаблона — а тогда проверка
    /// «вкладка встала на место» доказывала бы только, что объект создан.
    /// Стили докинга идут следом ровно там же, где их подключает приложение.
    /// </remarks>
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new ArxisTheme());
        Styles.Add(new DockingStyles());
    }
}
