using System.Reflection;

namespace ArxisStudio.Tests;

/// <summary>
/// Код самой студии текстом.
/// </summary>
/// <remarks>
/// Число отступа в коде модуля или плагина при сборке ловит ARX0010. Код студии этого правила не
/// получает — анализатор включён только расширениям, чтобы оставшиеся литералы студии не валили
/// сборку, — и число в нём было некому заметить. Исходники едут в сборке тестов ресурсами, тем же
/// приёмом, что разметка в <see cref="MarkupSources"/>: шаблон в csproj берёт их звёздочкой, и
/// новый файл попадает под счёт сам.
/// </remarks>
internal static class CodeSources
{
    /// <summary>Имя файла и текст, по одному на файл кода.</summary>
    public static IEnumerable<(string Name, string Text)> All() => Read("code/");

    /// <summary>
    /// Код студии и всего, что строит интерфейс на её теме: модулей, плагинов, шаблона плагина,
    /// контролов и значков.
    /// </summary>
    /// <remarks>
    /// Не для счёта отступов — у расширений число ловит ARX0010, а в библиотеках оно дело темы, —
    /// а для вопросов, одинаковых для любого кода: ключ темы, названный строкой, переименованием
    /// ломается молча где угодно.
    /// </remarks>
    public static IEnumerable<(string Name, string Text)> Everywhere() =>
        new[] { "code/", "code-extensions/", "code-controls/", "code-icons/" }.SelectMany(Read);

    private static IEnumerable<(string Name, string Text)> Read(string prefix)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var names = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal);

        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);

            yield return (name[prefix.Length..], reader.ReadToEnd());
        }
    }
}
