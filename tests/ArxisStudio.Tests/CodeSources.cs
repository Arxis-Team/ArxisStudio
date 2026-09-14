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
    public static IEnumerable<(string Name, string Text)> All()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var names = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("code/", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal);

        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);

            yield return (name["code/".Length..], reader.ReadToEnd());
        }
    }
}
