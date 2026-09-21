using System.Security;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Project.Model;

/// <summary>Что дерево узнало от диска.</summary>
/// <param name="Present">Пути элементов, которые есть на диске: файлы и явно объявленные папки.</param>
/// <param name="Empty">
/// Пустые папки — без единого файла ни в них, ни глубже, — стоящие там, где дерево их показало бы. В
/// снимке их нет: MSBuild перечисляет файлы, а папок, в которых файлов нет, не называет.
/// </param>
public sealed record DiskAnswer(IReadOnlySet<CanonicalPath> Present, IReadOnlySet<CanonicalPath> Empty);

/// <summary>
/// Спрашивает диск, какие из элементов, которые дерево показало бы, на самом деле есть, и какие
/// папки лежат пустыми.
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
/// <para>
/// <b>Пустая папка стоит, как у Rider:</b> папка SDK-проекта — то, что лежит на диске, и
/// <c>Assets</c>, опустевшая после удаления последнего файла, не пропадает. Ищется она только под
/// папками, которые дерево уже показывает, — проектом и папками его элементов, — теми же описями.
/// Папка, в которой лежат одни файлы, исключённые проектом, пустой не считается: проект её прячет,
/// и пустая папка в ней не вытаскивает спрятанное на свет.
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

    /// <summary>Спрашивает диск о решении.</summary>
    /// <param name="snapshot">Снимок решения.</param>
    /// <param name="cancellation">Отмена: пришёл новый снимок, и этот ответ уже никому не нужен.</param>
    public static DiskAnswer Probe(SolutionSnapshot snapshot, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var present = new HashSet<CanonicalPath>();
        var empty = new HashSet<CanonicalPath>();
        var asked = new HashSet<CanonicalPath>();
        var listings = new Dictionary<CanonicalPath, Listing>();

        foreach (var project in snapshot.Projects)
        {
            var filter = new ItemFilter(project);
            var shown = new HashSet<string>(Names) { string.Empty };

            foreach (var item in project.Items)
            {
                cancellation.ThrowIfCancellationRequested();

                if (!filter.Shows(item, out var relative))
                    continue;

                var folder = ItemFilter.IsFolder(item);

                if (asked.Add(item.FullPath) && Listed(listings, item.FullPath.Directory).Holds(item.FullPath, folder))
                    present.Add(item.FullPath);

                if (present.Contains(item.FullPath))
                    Show(shown, folder ? relative : Parent(relative));
            }

            Bare(project, filter, shown, listings, empty, cancellation);
        }

        return new DiskAnswer(present, empty);
    }

    /// <summary>
    /// Пустые папки проекта: под каждой папкой, которую дерево показывает, — её папки, в которых нет
    /// ни одного файла.
    /// </summary>
    private static void Bare(
        ProjectSnapshot project,
        ItemFilter filter,
        HashSet<string> shown,
        Dictionary<CanonicalPath, Listing> listings,
        HashSet<CanonicalPath> empty,
        CancellationToken cancellation)
    {
        var home = project.ProjectDirectory;

        if (home.IsEmpty)
            return;

        foreach (var relative in shown)
        {
            var folder = relative.Length == 0 ? home : home.Combine(relative.Replace('/', Path.DirectorySeparatorChar));

            foreach (var name in Listed(listings, folder).Folders)
            {
                cancellation.ThrowIfCancellationRequested();

                var path = folder.Combine(name);

                if (shown.Contains(relative.Length == 0 ? name : $"{relative}/{name}") || empty.Contains(path) || !filter.ShowsFolder(path, out _))
                    continue;

                var found = new List<CanonicalPath>();

                if (Empty(path, found, cancellation))
                    empty.UnionWith(found);
            }
        }
    }

    /// <summary>
    /// Нет ли в папке ни одного файла — ни в ней, ни глубже; сама она и её папки, кроме служебных, —
    /// в <paramref name="found"/>.
    /// </summary>
    /// <remarks>
    /// Ссылка — точка соединения или символическая — считается содержимым, и куда она ведёт, не
    /// проверяется: обход не уходит по кругу, а папка, пустая только с виду, пустой не показывается.
    /// Папка, которую прочесть нельзя, тоже не пуста — показывать то, чего не видно, нельзя.
    /// </remarks>
    private static bool Empty(CanonicalPath path, List<CanonicalPath> found, CancellationToken cancellation)
    {
        var pending = new Stack<(DirectoryInfo Directory, CanonicalPath Path, bool Shown)>();

        pending.Push((new DirectoryInfo(path.Value), path, true));

        try
        {
            while (pending.TryPop(out var next))
            {
                cancellation.ThrowIfCancellationRequested();

                if (next.Shown)
                    found.Add(next.Path);

                foreach (var entry in next.Directory.EnumerateFileSystemInfos())
                {
                    if (entry is not DirectoryInfo directory || (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        return false;

                    pending.Push((directory, next.Path.Combine(entry.Name), next.Shown && !entry.Name.StartsWith('.')));
                }
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    /// <summary>Папка и все её предки — в показанных.</summary>
    private static void Show(HashSet<string> shown, string relative)
    {
        var at = relative;

        // Предок, который уже в показанных, пришёл туда со своими предками.
        while (at.Length > 0 && shown.Add(at))
            at = Parent(at);
    }

    /// <summary>Путь родителя через прямую черту; у верхнего — пусто.</summary>
    private static string Parent(string relative)
    {
        var slash = relative.LastIndexOf('/');

        return slash < 0 ? string.Empty : relative[..slash];
    }

    private static Listing Listed(Dictionary<CanonicalPath, Listing> listings, CanonicalPath folder)
    {
        if (!listings.TryGetValue(folder, out var listing))
            listings[folder] = listing = Listing.Of(folder);

        return listing;
    }

    /// <summary>Опись одной папки: имена и их признаки.</summary>
    private sealed class Listing
    {
        private readonly Dictionary<string, FileAttributes>? _entries;
        private readonly bool _unreadable;

        private Listing(Dictionary<string, FileAttributes>? entries, bool unreadable)
        {
            _entries = entries;
            _unreadable = unreadable;
        }

        /// <summary>Папки описи, кроме ссылок; опись не прочлась — ни одной.</summary>
        public IEnumerable<string> Folders => _entries is null
            ? []
            : _entries
                .Where(entry => (entry.Value & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory)
                .Select(entry => entry.Key);

        /// <summary>Читает опись; папки нет — опись пуста, прочесть нельзя — вопросы пойдут по одному.</summary>
        public static Listing Of(CanonicalPath folder)
        {
            try
            {
                var directory = new DirectoryInfo(folder.Value);

                if (!directory.Exists)
                    return new Listing(null, unreadable: false);

                var entries = new Dictionary<string, FileAttributes>(Names);

                foreach (var entry in directory.EnumerateFileSystemInfos())
                    entries[entry.Name] = entry.Attributes;

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

            return _entries is not null
                   && _entries.TryGetValue(path.FileName, out var attributes)
                   && ((attributes & FileAttributes.Directory) != 0) == folder;
        }
    }
}
