namespace ArxisStudio.Tests;

/// <summary>
/// Архив примера — настоящий <c>.axplugin</c>, собранный сборкой решения.
/// </summary>
/// <remarks>
/// Путь ищется вверх от папки тестов, а не задаётся константой: рабочий
/// каталог прогона зависит от того, кто его запустил. Найденное запоминается —
/// подъём по дереву стоит нескольких обращений к диску, а ответ один на весь
/// прогон.
/// </remarks>
internal static class HelloArchive
{
    /// <summary>Полный путь к <c>arxis.hello.axplugin</c>.</summary>
    public static string Path { get; } = Find();

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = System.IO.Path.Combine(
                directory.FullName, "src", "Plugins", "Arxis.HelloPlugin", "arxis.hello.axplugin");

            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException("Не найден архив примера arxis.hello.axplugin");
    }
}
