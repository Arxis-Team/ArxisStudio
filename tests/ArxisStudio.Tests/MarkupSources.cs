using System.Reflection;

namespace ArxisStudio.Tests;

/// <summary>
/// Разметка студии текстом.
/// </summary>
/// <remarks>
/// Почти всё здесь проверяется на живом дереве контролов: так видно не то, что
/// написано, а то, что вышло. Но вопрос «сколько отступов всё ещё написано
/// числом» задаётся именно к написанному, а скомпилированная разметка чисел уже
/// не показывает. Поэтому исходники едут в сборке тестов ресурсами: шаблон в
/// csproj берёт их звёздочкой, чтобы новый экран попадал под счёт сам.
/// <para>
/// Тот же приём заведён в подмодуле темы — там он появился раньше, и по тем же
/// причинам.
/// </para>
/// </remarks>
internal static class MarkupSources
{
    /// <summary>Имя файла и текст, по одному на файл разметки.</summary>
    public static IEnumerable<(string Name, string Text)> All()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var names = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("axaml/", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal);

        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);

            yield return (Short(name), reader.ReadToEnd());
        }
    }

    // Путь внутри ресурса собирает MSBuild, и разделитель там свой для каждой
    // системы сборки. Имя файла от него не зависит.
    /// <summary>
    /// Разметка самой студии: приложение, оболочка и движок докинга.
    /// </summary>
    /// <remarks>
    /// Модуль и плагин — расширения, и ширина их канвы их дело: правило о числах держит у них
    /// анализатор, и ширину он не спрашивает — «канва плагина в 137 пикселей его дело». Студия
    /// своей теме не расширение, и её экраны живут по её же правилу.
    /// </remarks>
    public static IEnumerable<(string Name, string Text)> Own() =>
        Named().Where(source => Studio(source.Path)).Select(source => (source.Name, source.Text));

    /// <summary>
    /// Картинка релиза заставки: рисунок, а не вёрстка.
    /// </summary>
    /// <remarks>
    /// Её числа — координаты внутри картинки: где стоит буква, куда уходит стойка. Ступенью шкалы
    /// они не выражаются, и ключом темы им быть незачем — файл меняют целиком к новой версии.
    /// </remarks>
    public static bool IsSplashArt(string name) =>
        name.Equals("Splash2026.axaml", StringComparison.Ordinal);

    /// <summary>Имя, путь и текст — по одному на файл разметки.</summary>
    private static IEnumerable<(string Name, string Path, string Text)> Named()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var names = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("axaml/", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal);

        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);

            yield return (Short(name), name["axaml/".Length..].Replace('\\', '/'), reader.ReadToEnd());
        }
    }

    private static bool Studio(string path) =>
        path.StartsWith("ArxisStudio/", StringComparison.Ordinal) ||
        path.StartsWith("ArxisStudio.Shell/", StringComparison.Ordinal) ||
        path.StartsWith("ArxisStudio.Docking/", StringComparison.Ordinal);

    private static string Short(string name) => name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
}
