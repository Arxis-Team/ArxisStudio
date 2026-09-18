using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Спрашивает у человека решение, которое открыть.
/// </summary>
/// <remarks>
/// Те же фильтры, что у окна приветствия: решения и проекты всех трёх языков и «все файлы». Путь
/// уходит службе проектов, а список недавних обновит сама студия — она следит за открытым.
/// </remarks>
internal static class SolutionPicker
{
    private static readonly string[] Solutions = ["*.sln", "*.slnx", "*.csproj", "*.fsproj", "*.vbproj"];

    /// <summary>Подмена для тестов: вместо окна выбора — готовый ответ.</summary>
    internal static Func<Task<string?>>? Override { get; set; }

    /// <summary>Путь выбранного решения; пусто — человек передумал.</summary>
    /// <param name="owner">Контрол, от окна которого открывается выбор.</param>
    /// <param name="strings">Словари модуля.</param>
    public static async Task<string?> Ask(Control owner, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(strings);

        if (Override is { } answer)
            return await answer();

        if (TopLevel.GetTopLevel(owner)?.StorageProvider is not { CanOpen: true } storage)
            return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = strings["project.picker.title"],
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(strings["project.picker.solutions"]) { Patterns = Solutions },
                new FilePickerFileType(strings["project.picker.all"]) { Patterns = ["*"] },
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
