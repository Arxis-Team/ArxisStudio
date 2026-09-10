using System.Collections.Immutable;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.Watching;

/// <summary>
/// Что считать переменой состава проекта.
/// </summary>
/// <remarks>
/// <para>
/// Файл, появившийся под маской, не меняет ни одного входа оценки: <c>**/*.cs</c> как было, так и
/// осталось, и <see cref="SolutionSnapshot.Invalidate"/> о нём промолчит. Но проект после
/// перезагрузки назовёт его, и дерево решения обязано это увидеть. Поэтому за папками проектов
/// служба следит сама — библиотека оставляет это хозяину и говорит об этом в своих ограничениях.
/// </para>
/// <para>
/// <b>Отбор в два шага.</b> На событии — дёшево и без диска: выход сборки, служебные папки и имена,
/// которых MSBuild в проект не берёт, отбрасываются сразу, и сборка проекта не будит перезагрузку.
/// На пачке — по диску: перемена есть, только если лежащее на диске расходится с тем, что проект
/// перечисляет. Так атомарное сохранение — переименование старого файла, запись нового, удаление
/// временного — не значит ничего: к разбору пачки файл на месте и в проекте он уже был.
/// </para>
/// <para>
/// Файл принадлежит ближайшему проекту. Вложенный проект обычно исключён из внешнего, и сравнение
/// с внешним будило бы перезагрузку на каждом сохранении во вложенном.
/// </para>
/// </remarks>
internal static class MembershipFilter
{
    private static readonly string[] OutputProperties = ["BaseIntermediateOutputPath", "IntermediateOutputPath", "OutputPath"];

    /// <summary>
    /// Папки, за которыми следить: верхние папки проектов.
    /// </summary>
    /// <param name="snapshot">Снимок.</param>
    /// <remarks>Папка, лежащая внутри другой папки проекта, отдельного наблюдателя не получает.</remarks>
    public static ImmutableArray<CanonicalPath> Roots(SolutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var roots = new List<CanonicalPath>();

        foreach (var directory in snapshot.Projects
                     .Select(project => project.ProjectDirectory)
                     .Where(directory => !directory.IsEmpty)
                     .Distinct()
                     .OrderBy(directory => directory.Value.Length))
        {
            if (!roots.Exists(root => directory.StartsWith(root)))
                roots.Add(directory);
        }

        return [.. roots.Order()];
    }

    /// <summary>
    /// Стоит ли событие разбора: лежит под проектом и не отброшено сразу.
    /// </summary>
    /// <param name="snapshot">Снимок.</param>
    /// <param name="path">Путь из события.</param>
    public static bool IsCandidate(SolutionSnapshot snapshot, CanonicalPath path) =>
        Owner(snapshot, path) is { } project && !IsExcluded(project, path);

    /// <summary>
    /// Пути пачки, которые меняют состав проектов.
    /// </summary>
    /// <param name="snapshot">Снимок, с которым сверяться.</param>
    /// <param name="batch">Пути из событий.</param>
    /// <param name="disk">Что лежит на диске сейчас.</param>
    public static ImmutableArray<CanonicalPath> Changed(
        SolutionSnapshot snapshot,
        IEnumerable<CanonicalPath> batch,
        IDiskView disk)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(disk);

        var changed = ImmutableArray.CreateBuilder<CanonicalPath>();
        var listed = new Dictionary<ProjectIdentity, HashSet<CanonicalPath>>();

        foreach (var path in batch.Distinct())
        {
            if (Owner(snapshot, path) is not { } project || IsExcluded(project, path))
                continue;

            if (!listed.TryGetValue(project.Identity, out var items))
            {
                items = [.. project.Items.Select(item => item.FullPath).Where(item => !item.IsEmpty)];
                listed[project.Identity] = items;
            }

            if (Differs(project, path, items, disk))
                changed.Add(path);
        }

        return changed.ToImmutable();
    }

    /// <summary>Ближайший проект, в чьей папке лежит путь; null — ничей.</summary>
    /// <param name="snapshot">Снимок.</param>
    /// <param name="path">Путь.</param>
    internal static ProjectSnapshot? Owner(SolutionSnapshot snapshot, CanonicalPath path)
    {
        ProjectSnapshot? owner = null;

        foreach (var project in snapshot.Projects)
        {
            var directory = project.ProjectDirectory;

            if (directory.IsEmpty || !path.StartsWith(directory))
                continue;

            if (owner is null || directory.Value.Length > owner.ProjectDirectory.Value.Length)
                owner = project;
        }

        return owner;
    }

    /// <summary>
    /// Путь, которого проект не перечислит никогда: выход сборки, служебная папка, временный файл.
    /// </summary>
    /// <param name="project">Проект, которому путь принадлежит.</param>
    /// <param name="path">Путь.</param>
    /// <remarks>
    /// Правила — те, что MSBuild ставит по умолчанию: <c>bin</c> и <c>obj</c> в корне проекта,
    /// папки с точкой на любой глубине, файлы проектов и решений, <c>*.user</c>. Сверх этого —
    /// <c>node_modules</c>, ради объёма, и временные файлы редакторов: проект, в котором такой файл
    /// лежит всерьёз, узнает о нём со следующей перезагрузкой.
    /// </remarks>
    internal static bool IsExcluded(ProjectSnapshot project, CanonicalPath path)
    {
        var directory = project.ProjectDirectory;

        if (path == directory)
            return true;

        var segments = path.Value[directory.Value.Length..]
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
            return true;

        if (segments[0].Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segments[0].Equals("obj", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            if (index < segments.Length - 1 && segments[index].StartsWith('.'))
                return true;

            if (segments[index].Equals("node_modules", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var property in OutputProperties)
        {
            if (project.Properties.GetValueOrDefault(property) is { Length: > 0 } value
                && Resolve(directory, value) is { IsEmpty: false } output
                && output != directory
                && path.StartsWith(output))
            {
                return true;
            }
        }

        var name = segments[^1];

        return IsNeverListed(name) || IsTransient(name);
    }

    /// <summary>Временный файл редактора или атомарного сохранения.</summary>
    /// <param name="name">Имя файла.</param>
    internal static bool IsTransient(string name) =>
        name.EndsWith('~')
        || name.StartsWith("~$", StringComparison.Ordinal)
        || name.StartsWith(".#", StringComparison.Ordinal)
        || (name.Length > 1 && name[0] == '#' && name[^1] == '#')
        || name.Contains("___jb_", StringComparison.Ordinal)
        || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".swp", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".swx", StringComparison.OrdinalIgnoreCase);

    private static bool IsNeverListed(string name) =>
        name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".user", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".vssscc", StringComparison.OrdinalIgnoreCase)
        || (Path.GetExtension(name) is { Length: >= 5 } extension && extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase));

    /// <summary>Расходится ли диск с тем, что проект перечисляет.</summary>
    private static bool Differs(ProjectSnapshot project, CanonicalPath path, HashSet<CanonicalPath> items, IDiskView disk)
    {
        if (disk.IsFile(path))
            return !items.Contains(path);

        if (disk.IsDirectory(path))
            return disk.FilesUnder(path).Any(file => !IsExcluded(project, file) && !items.Contains(file));

        // Пропало. Перемена — только если там было что-то из проекта: сам файл или папка с его
        // файлами. Удалённая пустая папка или временный файл, которого проект не знал, — нет.
        return items.Contains(path) || items.Any(item => item.StartsWith(path));
    }

    private static CanonicalPath Resolve(CanonicalPath directory, string value)
    {
        try
        {
            return CanonicalPath.TryCreate(Path.GetFullPath(value, directory.Value), out var path)
                ? path
                : CanonicalPath.None;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return CanonicalPath.None;
        }
    }
}
