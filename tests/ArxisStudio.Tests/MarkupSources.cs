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
    private static string Short(string name) => name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
}
