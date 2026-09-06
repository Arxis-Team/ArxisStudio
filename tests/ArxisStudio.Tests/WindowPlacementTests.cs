using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Где открываются окна студии.
/// </summary>
/// <remarks>
/// Заставка своё место назвала сразу, а Welcome и окно студии — нет, и вставали
/// они туда, куда их клала система: у левого верхнего угла, со сдвигом на
/// каждое следующее. Заметил это человек, а не сборка, и иначе быть не могло:
/// свойство, которого нет, ничем себя не выдаёт — ни предупреждением, ни
/// упавшим тестом.
/// <para>
/// Поэтому сторож читает саму разметку. Построить окно и спросить его нельзя:
/// главное окно поднимает раскладку, читает папку плагинов и цепляется к
/// обработчикам платформы — во всём наборе его не создаёт никто, и заводить
/// это ради одного свойства дороже, чем оно стоит. Разметка же и есть то
/// место, где место окна объявлено.
/// </para>
/// <para>
/// Оторванные окна доков сюда не попадают: они встают под курсор, говорят об
/// этом кодом (<c>DockFloat</c>), и центр экрана им не нужен.
/// </para>
/// </remarks>
public class WindowPlacementTests
{
    /// <summary>
    /// Каждое окно студии открывается посреди экрана.
    /// </summary>
    /// <remarks>
    /// Проверяются все окна разом, а не три по именам: окно, добавленное
    /// завтра, обязано ответить на тот же вопрос — и лучше здесь, чем у
    /// человека.
    /// </remarks>
    [Fact]
    public void Every_window_of_the_studio_opens_in_the_middle_of_the_screen()
    {
        var windows = Windows();

        // Пустой список прошёл бы молча: сначала убеждаемся, что нашли окна.
        Assert.NotEmpty(windows);

        foreach (var (name, markup) in windows)
            Assert.True(
                markup.Contains("WindowStartupLocation=\"CenterScreen\"", StringComparison.Ordinal),
                $"{name}: окно не сказало, где открываться — нет WindowStartupLocation=\"CenterScreen\"");
    }

    /// <summary>Разметка окон приложения: имя файла и его текст.</summary>
    private static IReadOnlyList<(string Name, string Markup)> Windows() =>
        Directory
            .EnumerateFiles(Application(), "*.axaml", SearchOption.AllDirectories)
            .Where(path => !Built(path))
            .Select(path => (Name: Path.GetFileName(path), Markup: File.ReadAllText(path)))
            .Where(file => IsWindow(file.Markup))
            .ToList();

    /// <summary>Не лежит ли файл в выходе сборки — там те же имена.</summary>
    private static bool Built(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>
    /// Окно ли это — по имени корневого элемента.
    /// </summary>
    /// <remarks>
    /// По имени, а не по списку файлов: список стареет молча, а корень
    /// разметки о себе говорит сам. Под правило попадают и <c>Window</c>, и
    /// <c>ax:AxWindow</c>, а панели, знак и оформление заставки — нет.
    /// </remarks>
    private static bool IsWindow(string markup)
    {
        var start = markup.IndexOf('<', StringComparison.Ordinal);

        if (start < 0)
            return false;

        var root = new string(markup[(start + 1)..]
            .TakeWhile(letter => !char.IsWhiteSpace(letter) && letter != '>')
            .ToArray());

        return root.EndsWith("Window", StringComparison.Ordinal);
    }

    /// <summary>Папка приложения-оболочки в репозитории.</summary>
    private static string Application()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "ArxisStudio");

            if (File.Exists(Path.Combine(candidate, "ArxisStudio.csproj")))
                return candidate;
        }

        throw new InvalidOperationException("Не найдено приложение src/ArxisStudio");
    }
}
