using System.Security;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Project.Model;

/// <summary>
/// Спрашивает диск, какие из элементов, которые дерево показало бы, на самом деле есть.
/// </summary>
/// <remarks>
/// Вне потока интерфейса и с отменой: решение вычисляется в тысячи элементов, и тысяча обращений к
/// файловой системе между двумя кадрами — это окно, которое перестаёт отвечать, пока сетевой диск
/// думает. Спрашиваются только те, что дерево показало бы, — у снимка полно кандидатов из соседних
/// папок, и ответ о них выбрасывался бы.
/// <para>
/// Диск спрашивается по папкам, а не по файлам: опись папки читается один раз, и ответ о каждом
/// её элементе берётся из описи. Элементов, чьи имена MSBuild достраивает до путей, — платформы,
/// возможности проекта, версии пакетов — в снимке больше, чем файлов, и лежат они в немногих
/// папках: на решении самой студии это 4 386 вопросов о 107 папках, и вся проверка идёт около
/// 30 мс там, где вопросы по одному шли 115–225. Папку, которую перечислить нельзя, спрашивают
/// по-старому — файл за файлом.
/// </para>
/// </remarks>
public static class DiskProbe
{
    /// <summary>
    /// Как сравнивать имена в описи — как сравнивает их файловая система: Windows и macOS имена без
    /// учёта регистра, остальные — с учётом.
    /// </summary>
    private static readonly StringComparer Names =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Пути, которые есть на диске: файлы и явно объявленные папки.</summary>
    /// <param name="snapshot">Снимок решения.</param>
    /// <param name="cancellation">Отмена: пришёл новый снимок, и этот ответ уже никому не нужен.</param>
    public static HashSet<CanonicalPath> Present(SolutionSnapshot snapshot, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var present = new HashSet<CanonicalPath>();
        var asked = new HashSet<CanonicalPath>();
        var folders = new Dictionary<CanonicalPath, Listing>();

        foreach (var project in snapshot.Projects)
        {
            var filter = new ItemFilter(project);

            foreach (var item in project.Items)
            {
                cancellation.ThrowIfCancellationRequested();

                if (!filter.Shows(item, out _) || !asked.Add(item.FullPath))
                    continue;

                var folder = item.FullPath.Directory;

                if (!folders.TryGetValue(folder, out var listing))
                    folders[folder] = listing = Listing.Of(folder);

                if (listing.Holds(item.FullPath, ItemFilter.IsFolder(item)))
                    present.Add(item.FullPath);
            }
        }

        return present;
    }

    /// <summary>Опись одной папки: имена и то, папка ли под именем.</summary>
    private sealed class Listing
    {
        private readonly Dictionary<string, bool>? _entries;
        private readonly bool _unreadable;

        private Listing(Dictionary<string, bool>? entries, bool unreadable)
        {
            _entries = entries;
            _unreadable = unreadable;
        }

        /// <summary>Читает опись; папки нет — опись пуста, прочесть нельзя — вопросы пойдут по одному.</summary>
        public static Listing Of(CanonicalPath folder)
        {
            try
            {
                var directory = new DirectoryInfo(folder.Value);

                if (!directory.Exists)
                    return new Listing(null, unreadable: false);

                var entries = new Dictionary<string, bool>(Names);

                foreach (var entry in directory.EnumerateFileSystemInfos())
                    entries[entry.Name] = (entry.Attributes & FileAttributes.Directory) != 0;

                return new Listing(entries, unreadable: false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
            {
                return new Listing(null, unreadable: true);
            }
        }

        /// <summary>Есть ли путь — файлом или папкой, как его объявил проект.</summary>
        public bool Holds(CanonicalPath path, bool folder)
        {
            if (_unreadable)
                return folder ? Directory.Exists(path.Value) : File.Exists(path.Value);

            return _entries is not null && _entries.TryGetValue(path.FileName, out var directory) && directory == folder;
        }
    }
}
