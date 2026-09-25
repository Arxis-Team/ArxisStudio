using ArxisStudio.Shell;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ArxisStudio.Settings;

/// <summary>
/// Диалоги страницы плагинов, которые задаёт окно настроек: выбор папки и архива, вопрос, показ
/// пути средствами системы.
/// </summary>
/// <remarks>
/// Владельцем модального вопроса и хранилищем файлов служит окно, но быть самим диалогом ему
/// незачем: у окна своя работа, а четыре метода чужого интерфейса только растягивали его.
/// </remarks>
/// <param name="owner">Окно, над которым встают диалоги.</param>
internal sealed class SettingsDialogs(Window owner) : IPluginDialogs
{
    /// <inheritdoc/>
    public async Task<string?> AskFolderAsync(string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    /// <inheritdoc/>
    public async Task<string?> AskArchiveAsync(string title)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ArxisStudio") { Patterns = ["*.axplugin"] },
            ],
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    /// <inheritdoc/>
    public Task<bool> ConfirmAsync(string title, string message, string confirm, bool danger) =>
        StudioAsk.ConfirmAsync(owner, title, message, confirm, danger);

    /// <inheritdoc/>
    public void Reveal(string path) => StudioOpen.InShell(path);
}
