using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.Watching;

/// <summary>
/// Наблюдатель файловой системы над папкой — заведённый одинаково у слежения за составом и у истории.
/// </summary>
/// <remarks>
/// Двое заводили наблюдателя своими копиями: буфер, подписки, включение и уборка на отказе. Разное у
/// них только то, за чем следить и куда говорить, — это они и передают.
/// </remarks>
internal static class FolderWatchers
{
    /// <summary>Буфер событий папки: запас дешевле перечитывания, которым кончается переполнение.</summary>
    private const int BufferSize = 64 * 1024;

    /// <summary>Заводит наблюдателя над папкой; null — папку убрали или читать её нельзя.</summary>
    /// <param name="root">Папка.</param>
    /// <param name="deep">Следить ли и за вложенными папками.</param>
    /// <param name="filter">За чем следить.</param>
    /// <param name="wire">Подписывает наблюдателя на его события до включения.</param>
    /// <remarks>
    /// Отказ не беда того, кто просил: папку убрали между снимком и слежением, а остальные папки
    /// следятся по-прежнему. Следующий снимок попробует снова.
    /// </remarks>
    public static FileSystemWatcher? Start(CanonicalPath root, bool deep, NotifyFilters filter, Action<FileSystemWatcher> wire)
    {
        FileSystemWatcher? watcher = null;

        try
        {
            watcher = new FileSystemWatcher(root.Value)
            {
                IncludeSubdirectories = deep,
                InternalBufferSize = BufferSize,
                NotifyFilter = filter,
            };

            wire(watcher);
            watcher.EnableRaisingEvents = true;

            return watcher;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            watcher?.Dispose();

            return null;
        }
    }

    /// <summary>Останавливает наблюдателя: сперва события, потом он сам.</summary>
    /// <param name="watcher">Наблюдатель.</param>
    public static void Stop(FileSystemWatcher watcher)
    {
        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
    }
}
