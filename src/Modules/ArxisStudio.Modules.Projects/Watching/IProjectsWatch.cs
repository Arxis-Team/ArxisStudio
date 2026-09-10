using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.Watching;

/// <summary>Слежение за диском одной сессии.</summary>
internal interface IProjectsWatch : IDisposable
{
    /// <summary>
    /// Следит за тем, из чего собран снимок, и за папками его проектов.
    /// </summary>
    /// <param name="snapshot">Снимок, опубликованный последним.</param>
    /// <remarks>Зовётся после каждой публикации: набор входов меняется вместе с моделью.</remarks>
    void Follow(SolutionSnapshot snapshot);
}

/// <summary>
/// Диск глазами проверки состава: что сейчас лежит по пути.
/// </summary>
/// <remarks>
/// Шов ради тестов разбора: состав проверяется по диску в момент разбора пачки, и проверять это
/// правило на настоящих файлах значило бы проверять ещё и файловую систему.
/// </remarks>
internal interface IDiskView
{
    /// <summary>По пути лежит файл.</summary>
    /// <param name="path">Путь.</param>
    bool IsFile(CanonicalPath path);

    /// <summary>По пути лежит папка.</summary>
    /// <param name="path">Путь.</param>
    bool IsDirectory(CanonicalPath path);

    /// <summary>Файлы в папке и во всех вложенных.</summary>
    /// <param name="directory">Папка.</param>
    IEnumerable<CanonicalPath> FilesUnder(CanonicalPath directory);
}

/// <summary>Настоящий диск.</summary>
internal sealed class DiskView : IDiskView
{
    /// <summary>Единственный экземпляр: состояния у него нет.</summary>
    public static DiskView Instance { get; } = new();

    /// <inheritdoc/>
    public bool IsFile(CanonicalPath path) => File.Exists(path.Value);

    /// <inheritdoc/>
    public bool IsDirectory(CanonicalPath path) => Directory.Exists(path.Value);

    /// <inheritdoc/>
    /// <remarks>
    /// Папка может пропасть посреди обхода — её переименовали следом за созданием. Это не сбой, а
    /// ответ «файлов больше нет»: следующая пачка принесёт новое имя.
    /// </remarks>
    public IEnumerable<CanonicalPath> FilesUnder(CanonicalPath directory)
    {
        IEnumerator<string> files;

        try
        {
            files = Directory.EnumerateFiles(
                directory.Value,
                "*",
                new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).GetEnumerator();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            yield break;
        }

        using (files)
        {
            while (true)
            {
                string file;

                try
                {
                    if (!files.MoveNext())
                        yield break;

                    file = files.Current;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    yield break;
                }

                if (CanonicalPath.TryCreate(file, out var path))
                    yield return path;
            }
        }
    }
}
