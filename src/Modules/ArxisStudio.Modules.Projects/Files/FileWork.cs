using System.Collections.Immutable;
using ArxisStudio.Modules.Projects.Watching;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.Files;

/// <summary>Что просят сделать с файлами.</summary>
internal enum FileWorkKind
{
    /// <summary>Переместить или переименовать.</summary>
    Move,

    /// <summary>Скопировать.</summary>
    Copy,

    /// <summary>Удалить насовсем.</summary>
    Delete,
}

/// <summary>Просьба к службе файлов.</summary>
/// <param name="Kind">Что сделать.</param>
/// <param name="Pairs">Что куда — у переноса и копии.</param>
/// <param name="Paths">Что удалить.</param>
/// <param name="Label">Метка действия для человека.</param>
internal sealed record FileWork(FileWorkKind Kind, ImmutableArray<FileMove> Pairs, ImmutableArray<CanonicalPath> Paths, string Label)
{
    /// <summary>Откуда: у переноса и копии — источники пар, у удаления — сами пути.</summary>
    public IEnumerable<CanonicalPath> Sources => Kind == FileWorkKind.Delete ? Paths : Pairs.Select(pair => pair.From);

    /// <summary>
    /// Та же просьба без лишнего: путь, лежащий в папке, которую уже просят, уедет, скопируется или
    /// удалится вместе с ней, и второй раз его искать было бы негде.
    /// </summary>
    public FileWork Normalize()
    {
        var folders = Sources.Where(source => Directory.Exists(source.Value)).ToList();

        bool Covered(CanonicalPath path) => folders.Any(folder => folder != path && path.StartsWith(folder));

        return this with
        {
            Pairs = [.. Pairs.Where(pair => !Covered(pair.From)).Distinct()],
            Paths = [.. Paths.Where(path => !Covered(path)).Distinct()],
        };
    }
}

/// <summary>Итог правки: ответ просившему и перемена для тех, кто держит файлы открытыми.</summary>
/// <param name="Result">Ответ.</param>
/// <param name="Change">
/// Что переехало и что удалено; null — такого не было. Правка содержимого переменой не считается:
/// следить редактору не за чем.
/// </param>
internal sealed record FileWorkResult(ProjectOperationResult Result, FilesChangedEventArgs? Change)
{
    /// <summary>
    /// Правка могла поменять модель: путь появился, пропал или переехал, или переписан файл,
    /// который читает MSBuild. У переноса, копии и удаления — всегда, когда диск поменялся.
    /// </summary>
    public bool Rereads { get; init; } = Change is not null;
}

/// <summary>
/// Проверки до первого байта: всё, что можно узнать, не трогая диск, узнаётся раньше, чем что-то
/// сдвинется.
/// </summary>
/// <remarks>
/// Правка, отказавшая посередине, откатывается, но не всякая откатывается целиком: удалённое не
/// вернуть без истории. Поэтому всё, что проверяемо заранее, — существование, место, занятость
/// назначения, папка в самой себе — проверяется заранее, а до диска доходят только его собственные
/// отказы: файл занят, прав нет.
/// </remarks>
internal static class FileChecks
{
    /// <summary>Проверяет просьбу по снимку и диску.</summary>
    /// <param name="work">Просьба.</param>
    /// <param name="snapshot">Снимок открытого решения.</param>
    /// <param name="words">Слова отказов.</param>
    /// <returns>Отказ; null — можно.</returns>
    /// <remarks>
    /// Источник копии не сторожится: копия его не трогает, и файл из проводника или из выхода сборки
    /// вставить в проект законно. Сторожится назначение — у всех трёх правок.
    /// </remarks>
    public static ProjectDiagnostic? Check(FileWork work, SolutionSnapshot snapshot, FileWords words)
    {
        foreach (var source in work.Sources)
        {
            if (!File.Exists(source.Value) && !Directory.Exists(source.Value))
                return Refused(ProjectsDiagnosticCodes.Missing, words.Missing(source.Value), source);

            if (work.Kind != FileWorkKind.Copy && Guard(snapshot, source, words, holdsProjects: true) is { } refused)
                return refused;
        }

        if (work.Kind == FileWorkKind.Delete)
            return null;

        var targets = new HashSet<CanonicalPath>();

        foreach (var pair in work.Pairs)
        {
            var (from, to) = pair;
            var caseOnly = work.Kind == FileWorkKind.Move && from == to && !string.Equals(from.Value, to.Value, StringComparison.Ordinal);

            if (Path.GetDirectoryName(to.Value) is not { Length: > 0 } parent || !Directory.Exists(parent))
                return Refused(ProjectsDiagnosticCodes.Missing, words.Missing(Path.GetDirectoryName(to.Value) ?? to.Value), to);

            if (Guard(snapshot, to, words, holdsProjects: false) is { } refused)
                return refused;

            // Заменяется только файл файлом: папку на месте назначения служба не сливает и не стирает.
            var replaced = pair.Replace && from != to && File.Exists(to.Value) && File.Exists(from.Value);

            if (!caseOnly && (from == to || (File.Exists(to.Value) && !replaced) || Directory.Exists(to.Value) || !targets.Add(to)))
                return Refused(ProjectsDiagnosticCodes.TargetExists, words.Exists(to.Value), to);

            if (Directory.Exists(from.Value) && to != from && to.StartsWith(from))
                return Refused(ProjectsDiagnosticCodes.IntoItself, words.IntoItself(from.Value), from);
        }

        return null;
    }

    /// <summary>Проверяет создание по снимку и диску.</summary>
    /// <param name="items">Что создать.</param>
    /// <param name="snapshot">Снимок открытого решения.</param>
    /// <param name="words">Слова отказов.</param>
    /// <returns>Отказ; null — можно.</returns>
    /// <remarks>
    /// Созданное не затирает ничего: ни файла, ни каталога на своём месте, ни соседа по пачке. Каталог
    /// на пути, занятый файлом — на диске или в той же пачке, — тоже «занято»: создать под файлом
    /// нечего.
    /// </remarks>
    public static ProjectDiagnostic? Check(IReadOnlyList<FileCreation> items, SolutionSnapshot snapshot, FileWords words)
    {
        var targets = new HashSet<CanonicalPath>();
        var files = items.Where(item => !item.IsDirectory).Select(item => item.Path).ToHashSet();

        foreach (var item in items)
        {
            var path = item.Path;

            if (Guard(snapshot, path, words, holdsProjects: false) is { } refused)
                return refused;

            if (File.Exists(path.Value) || Directory.Exists(path.Value) || !targets.Add(path))
                return Refused(ProjectsDiagnosticCodes.TargetExists, words.Exists(path.Value), path);

            var home = MembershipFilter.Owner(snapshot, path)!.ProjectDirectory;

            for (var parent = path.Directory; parent.StartsWith(home) && parent != home; parent = parent.Directory)
            {
                if (File.Exists(parent.Value) || files.Contains(parent))
                    return Refused(ProjectsDiagnosticCodes.TargetExists, words.Exists(parent.Value), parent);
            }
        }

        return null;
    }

    /// <summary>
    /// Путь внутри правки: в папке проекта, не выход сборки, не сам проект и не решение.
    /// </summary>
    /// <param name="snapshot">Снимок.</param>
    /// <param name="path">Путь.</param>
    /// <param name="words">Слова отказов.</param>
    /// <param name="holdsProjects">
    /// Путь — источник: папка, в которой лежит чужой файл проекта, унесла бы проект мимо решения.
    /// </param>
    private static ProjectDiagnostic? Guard(SolutionSnapshot snapshot, CanonicalPath path, FileWords words, bool holdsProjects)
    {
        if (MembershipFilter.Owner(snapshot, path) is not { } owner)
            return Refused(ProjectsDiagnosticCodes.OutsideProjects, words.Outside(path.Value), path);

        var protectedPath = MembershipFilter.IsOutsideSources(owner, path)
            || path == snapshot.EntryPoint.Path
            || snapshot.Projects.Any(project => project.ProjectFilePath == path)
            || (holdsProjects && snapshot.Projects.Any(project => project.ProjectFilePath.StartsWith(path)));

        return protectedPath
            ? Refused(ProjectsDiagnosticCodes.OutsideProjects, words.Protected(path.Value), path)
            : null;
    }

    private static ProjectDiagnostic Refused(string code, string message, CanonicalPath path) =>
        new(code, message, ProjectDiagnosticSeverity.Error) { FilePath = path };
}
