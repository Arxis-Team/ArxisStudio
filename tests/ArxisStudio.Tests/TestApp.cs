using ArxisStudio.Docking;
using ArxisStudio.Shell;
using ArxisStudio.Tests;
using ArxisStudio.Themes.Arxis;
using Avalonia;
using Avalonia.Headless;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TestApp))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

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
    /// Все три слоя и в том же порядке, что в App.axaml: тема, стили оболочки, стили докинга.
    /// Порядок решает, кто кого перекроет, а слой оболочки сюда когда-то не попал — и проверить
    /// его было нечем.
    ///
    /// Чужой базовой темы здесь нет, как нет её и в студии: окно, окно попапа и простые
    /// контейнеры одеты темой студии. Это же и проверка — тесты, которым нужно живое дерево,
    /// падали бы первыми, подмени тема чужой шаблон своим отсутствием.
    /// </remarks>
    public override void Initialize()
    {
        Styles.Add(new ArxisTheme());
        Styles.Add(new ShellStyles());
        Styles.Add(new DockingStyles());
    }
}
