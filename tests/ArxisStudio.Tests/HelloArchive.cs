using System.IO.Compression;
using System.Text.Json;

namespace ArxisStudio.Tests;

/// <summary>
/// Архив примера — настоящий <c>.axplugin</c>, собранный сборкой решения.
/// </summary>
/// <remarks>
/// Лежит рядом с проектом примера: его оставляет таргет упаковки. Не нашёлся — значит, пример не
/// собран, и об этом говорит первое же обращение, а не установка, отказавшая неизвестно почему.
/// </remarks>
internal static class HelloArchive
{
    /// <summary>Полный путь к <c>arxis.hello.axplugin</c>.</summary>
    public static string Path { get; } = Repository.File("src", "Plugins", "Arxis.HelloPlugin", "arxis.hello.axplugin");

    /// <summary>
    /// Ставит пример под другим именем: сборка та же, манифест свой.
    /// </summary>
    /// <param name="root">Папка плагинов.</param>
    /// <param name="id">Идентификатор клона — он же имя его папки.</param>
    /// <param name="dependencies">Зависимости записью манифеста; null — ни от кого.</param>
    /// <param name="activation">Активация записью манифеста; null — при старте.</param>
    /// <param name="contract">Контракт, который клон объявляет, — путь от его папки; null — никакого.</param>
    /// <returns>Папку клона.</returns>
    /// <remarks>
    /// Манифест переписывается целиком: панелей у клона нет — их типы объявлены атрибутом на общей
    /// сборке, и каждый клон тащил бы одну и ту же панель в окно.
    /// </remarks>
    public static string Clone(
        string root,
        string id,
        string? dependencies = null,
        string? activation = null,
        string? contract = null)
    {
        var target = System.IO.Path.Combine(root, id);

        ZipFile.ExtractToDirectory(Path, target);

        var fields = new List<string>
        {
            $"\"id\": \"{id}\"",
            $"\"name\": \"{id}\"",
            "\"version\": \"1.0.0\"",
            "\"entry\": \"bin/Arxis.HelloPlugin.dll\"",
            $"\"dependencies\": {dependencies ?? "[]"}",
            $"\"activation\": {activation ?? """[ "onStartup" ]"""}",
        };

        if (contract is not null)
            fields.Add($"\"provides\": {{ \"contracts\": [ {JsonSerializer.Serialize(contract)} ] }}");

        File.WriteAllText(
            System.IO.Path.Combine(target, "plugin.json"),
            "{\n  " + string.Join(",\n  ", fields) + "\n}\n");

        return target;
    }
}
