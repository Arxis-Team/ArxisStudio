using System.Collections.Immutable;
using ArxisStudio.Modules.Projects.Delivery;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.Engine;

/// <summary>
/// Сравнение снимков по содержимому.
/// </summary>
/// <remarks>
/// Ссылки здесь ничего не говорят: каждая загрузка строит все проекты заново, и проект,
/// перечитанный без перемен, — другой объект с тем же содержимым. Равенство записей ядра тоже не
/// помощник: у записей с неизменяемыми массивами (<see cref="AssemblyReferenceInfo.Aliases"/>,
/// <see cref="ResolvedPackage.Dependencies"/>, <see cref="SolutionFolder.Projects"/>)
/// сгенерированное равенство сравнивает массивы по ссылке, и две одинаковые загрузки разошлись бы
/// на каждом таком поле. Такие записи сравниваются здесь поле за полем.
/// </remarks>
internal static class SnapshotDiff
{
    private static readonly Sameness<ProjectReferenceInfo> ProjectReferences = new(static (x, y) =>
        x.ProjectFilePath == y.ProjectFilePath
        && x.Project == y.Project
        && x.ReferenceOutputAssembly == y.ReferenceOutputAssembly
        && Same(x.Aliases, y.Aliases, StringComparer.Ordinal)
        && Equals(x.Metadata, y.Metadata));

    private static readonly Sameness<AssemblyReferenceInfo> AssemblyReferences = new(static (x, y) =>
        string.Equals(x.Name, y.Name, StringComparison.Ordinal)
        && x.HintPath == y.HintPath
        && x.ResolvedPath == y.ResolvedPath
        && x.Private == y.Private
        && Same(x.Aliases, y.Aliases, StringComparer.Ordinal)
        && Equals(x.Metadata, y.Metadata));

    private static readonly Sameness<ResolvedPackage> ResolvedPackages = new(static (x, y) =>
        string.Equals(x.PackageId, y.PackageId, StringComparison.Ordinal)
        && string.Equals(x.Version, y.Version, StringComparison.Ordinal)
        && x.IsDirect == y.IsDirect
        && Same(x.CompileAssemblies, y.CompileAssemblies)
        && Same(x.RuntimeAssemblies, y.RuntimeAssemblies)
        && Same(x.Dependencies, y.Dependencies, StringComparer.Ordinal));

    private static readonly Sameness<SolutionFolder> Folders = new(static (x, y) =>
        string.Equals(x.Name, y.Name, StringComparison.Ordinal)
        && string.Equals(x.Path, y.Path, StringComparison.Ordinal)
        && Same(x.Projects, y.Projects));

    /// <summary>
    /// Шаг от прежнего снимка сессии к новому.
    /// </summary>
    /// <param name="before">Прежний снимок; null — снимка ещё не было.</param>
    /// <param name="after">Новый снимок.</param>
    /// <returns>
    /// Задетые проекты — добавленные, снятые и изменившиеся — и то, тронуто ли само решение.
    /// Снимок другой сессии задевает всё: общих идентичностей у сессий нет.
    /// </returns>
    public static SnapshotStep Step(SolutionSnapshot? before, SolutionSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(after);

        if (before is null || before.Workspace != after.Workspace)
            return new SnapshotStep([.. after.Projects.Select(project => project.Identity)], SolutionChanged: true);

        var was = before.Projects.ToDictionary(project => project.Identity);
        var touched = ImmutableArray.CreateBuilder<ProjectIdentity>();

        foreach (var project in after.Projects)
        {
            if (!was.Remove(project.Identity, out var previous) || !SameProject(previous, project))
                touched.Add(project.Identity);
        }

        touched.AddRange(was.Keys);

        return new SnapshotStep(touched.ToImmutable(), !SameSolution(before, after));
    }

    /// <summary>Одинаково ли описание самого решения — без содержимого проектов.</summary>
    /// <param name="a">Один снимок.</param>
    /// <param name="b">Другой.</param>
    public static bool SameSolution(SolutionSnapshot a, SolutionSnapshot b) =>
        string.Equals(a.Name, b.Name, StringComparison.Ordinal)
        && a.EntryPoint == b.EntryPoint
        && a.Solution == b.Solution
        && string.Equals(a.ProviderName, b.ProviderName, StringComparison.Ordinal)
        && Same(a.Configurations, b.Configurations, StringComparer.Ordinal)
        && Same(a.Platforms, b.Platforms, StringComparer.Ordinal)
        && Same(a.Diagnostics, b.Diagnostics)
        && Same(a.Folders, b.Folders, Folders)
        && a.Projects.Select(project => project.Identity).SequenceEqual(b.Projects.Select(project => project.Identity));

    /// <summary>Одинаково ли содержимое двух снимков одного проекта.</summary>
    /// <param name="a">Один снимок.</param>
    /// <param name="b">Другой.</param>
    public static bool SameProject(ProjectSnapshot a, ProjectSnapshot b) =>
        a.Identity == b.Identity
        && a.ProjectFilePath == b.ProjectFilePath
        && string.Equals(a.Name, b.Name, StringComparison.Ordinal)
        && string.Equals(a.Language, b.Language, StringComparison.Ordinal)
        && string.Equals(a.Kind, b.Kind, StringComparison.Ordinal)
        && string.Equals(a.ProviderName, b.ProviderName, StringComparison.Ordinal)
        && string.Equals(a.ActiveConfiguration, b.ActiveConfiguration, StringComparison.Ordinal)
        && string.Equals(a.ActivePlatform, b.ActivePlatform, StringComparison.Ordinal)
        && string.Equals(a.ActiveTargetFramework, b.ActiveTargetFramework, StringComparison.Ordinal)
        && Same(a.TargetFrameworks, b.TargetFrameworks, StringComparer.Ordinal)
        && Same(a.Configurations, b.Configurations, StringComparer.Ordinal)
        && Same(a.Platforms, b.Platforms, StringComparer.Ordinal)
        && Equals(a.Properties, b.Properties)
        && Same(a.EvaluationInputs, b.EvaluationInputs)
        && Same(a.Items, b.Items)
        && Same(a.Diagnostics, b.Diagnostics)
        && Same(a.PackageReferences, b.PackageReferences)
        && Same(a.FrameworkReferences, b.FrameworkReferences)
        && Same(a.AnalyzerReferences, b.AnalyzerReferences)
        && Same(a.Outputs, b.Outputs)
        && Same(a.ProjectReferences, b.ProjectReferences, ProjectReferences)
        && Same(a.AssemblyReferences, b.AssemblyReferences, AssemblyReferences)
        && Same(a.ResolvedPackages, b.ResolvedPackages, ResolvedPackages);

    /// <summary>
    /// Поэлементное сравнение массивов.
    /// </summary>
    /// <remarks>
    /// Пустой массив по умолчанию считается пустым, а не падает: ядро обещает таких не отдавать,
    /// но запись, собранная инициализатором без этого поля, обещания не читала.
    /// </remarks>
    private static bool Same<T>(ImmutableArray<T> x, ImmutableArray<T> y, IEqualityComparer<T>? comparer = null)
    {
        var left = x.IsDefault ? [] : x;
        var right = y.IsDefault ? [] : y;

        return left.AsSpan().SequenceEqual(right.AsSpan(), comparer);
    }

    /// <summary>Равенство по правилу, без хеша: последовательностям он не нужен.</summary>
    private sealed class Sameness<T>(Func<T, T, bool> same) : IEqualityComparer<T>
        where T : class
    {
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y) || (x is not null && y is not null && same(x, y));

        public int GetHashCode(T obj) => 0;
    }
}
