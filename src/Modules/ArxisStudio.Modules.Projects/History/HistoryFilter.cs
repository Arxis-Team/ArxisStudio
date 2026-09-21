using System.Collections.Immutable;
using ArxisStudio.Modules.Projects.Watching;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.History;

/// <summary>
/// Что локальная история записывает: где следить и какие пути в счёт.
/// </summary>
/// <remarks>
/// <para>
/// Следит она за папками проектов — на любой глубине — и за папкой решения без вложенных. В папке
/// решения лежит то, что правит все проекты сразу: <c>Directory.Build.props</c>,
/// <c>.editorconfig</c>, <c>global.json</c>, само решение. Вглубь неё не идём: там соседние
/// репозитории, документация и всё, что к решению отношения не имеет.
/// </para>
/// <para>
/// Выход сборки и служебные папки отбрасываются тем же правилом, что и у состава проекта
/// (<see cref="MembershipFilter.IsOutsideSources"/>), а файлы проектов и решений, которые состав
/// проекта пропускает, история пишет: правка файла проекта — такая же правка, как всякая другая.
/// Временные файлы редакторов не пишутся: атомарное сохранение создаёт и убирает их за миг, и
/// история, их записавшая, была бы из них одних.
/// </para>
/// </remarks>
internal static class HistoryFilter
{
    /// <summary>Где следить: папки проектов вглубь и папку решения без вложенных.</summary>
    /// <param name="snapshot">Снимок.</param>
    public static ImmutableArray<(CanonicalPath Root, bool Deep)> Roots(SolutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var roots = MembershipFilter.Roots(snapshot).Select(root => (root, true)).ToList();

        if (Folder(snapshot.EntryPoint.Path) is { } solution && !roots.Exists(root => solution.StartsWith(root.root)))
            roots.Add((solution, false));

        return [.. roots];
    }

    /// <summary>Пишет ли история этот путь — файл или папку.</summary>
    /// <param name="snapshot">Снимок.</param>
    /// <param name="path">Путь.</param>
    public static bool IsTracked(SolutionSnapshot snapshot, CanonicalPath path)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var name = path.FileName;

        if (name.Length == 0 || MembershipFilter.IsTransient(name) || IsNoise(name))
            return false;

        if (MembershipFilter.Owner(snapshot, path) is { } project)
            return !MembershipFilter.IsOutsideSources(project, path);

        return Folder(path) is { } folder && Folder(snapshot.EntryPoint.Path) == folder;
    }

    /// <summary>Папка, в которой лежит путь; null — у корня диска её нет.</summary>
    /// <param name="path">Путь.</param>
    public static CanonicalPath? Folder(CanonicalPath path) =>
        Path.GetDirectoryName(path.Value) is { Length: > 0 } folder && CanonicalPath.TryCreate(folder, out var parent)
            ? parent
            : null;

    /// <summary>Файлы, которые пишет система, а не человек, и которые проект не перечисляет.</summary>
    private static bool IsNoise(string name) =>
        name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".user", StringComparison.OrdinalIgnoreCase);
}
