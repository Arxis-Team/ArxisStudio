namespace ArxisStudio.Tests;

/// <summary>
/// Временные папки тестов: имя, заведение и уборка.
/// </summary>
/// <remarks>
/// Сорок с лишним наборов заводили папку сами и убирали её тремя разными способами — кто молча
/// пропускал занятый файл, кто падал на нём, кто проверял, есть ли папка вовсе. Здесь способов два,
/// и выбирают их словом: мягкая уборка пропускает занятое — его держит прошлый прогон или
/// антивирус, а не проверяемый код; строгая падает — у наборов, которые держат её проверкой.
/// </remarks>
internal static class TempFolder
{
    /// <summary>Заводит папку и отдаёт путь к ней.</summary>
    /// <param name="name">Чья папка — часть её имени.</param>
    public static string Create(string name) => Directory.CreateDirectory(Reserve(name)).FullName;

    /// <summary>Путь новой папки — не заводя её: её заведёт сам тест, копией или установкой.</summary>
    /// <param name="name">Чья папка — часть её имени.</param>
    public static string Reserve(string name) =>
        Path.Combine(Path.GetTempPath(), $"arxis-{name}-{Guid.NewGuid():N}");

    /// <summary>Стирает папку, если она есть.</summary>
    /// <param name="path">Папка.</param>
    /// <param name="strict">Падать ли на занятом файле; иначе он остаётся до следующей уборки системы.</param>
    public static void Erase(string path, bool strict = false)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception e) when (!strict && e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
