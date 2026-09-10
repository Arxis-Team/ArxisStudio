using System.Collections.Concurrent;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Tests;

/// <summary>
/// Провайдер модели, которым тест управляет: что отдать и где остановиться.
/// </summary>
/// <remarks>
/// Настоящий MSBuild проверяется интеграционными тестами. Поведение службы — очередь, склейка,
/// отмена, сессии — проверяется здесь, где загрузка кончается ровно тогда, когда тест её отпустил,
/// а не когда движку захотелось.
/// </remarks>
internal sealed class ScriptedProvider : IProjectSystemProvider
{
    private readonly ConcurrentQueue<WorkspaceLoadRequest> _requests = new();
    private int _loads;

    /// <inheritdoc/>
    public string Name => "Scripted";

    /// <summary>Сколько загрузок дошло до провайдера.</summary>
    public int Loads => Volatile.Read(ref _loads);

    /// <summary>С чем приходили загрузки, по порядку.</summary>
    public IReadOnlyCollection<WorkspaceLoadRequest> Requests => _requests;

    /// <summary>Проекты решения по умолчанию: имя и файлы.</summary>
    public (string Name, string[] Items)[] Projects { get; set; } =
        [("App", ["Program.cs"]), ("Lib", ["Greeter.cs"])];

    /// <summary>Что отдать вместо решения по умолчанию.</summary>
    public Func<WorkspaceLoadRequest, WorkspaceLoadResult>? Answer { get; set; }

    /// <summary>Где остановиться; null — не останавливаться.</summary>
    public LoadGate? Gate { get; set; }

    /// <inheritdoc/>
    public bool CanLoad(WorkspaceEntryPoint entryPoint) => true;

    /// <inheritdoc/>
    public async ValueTask<WorkspaceLoadResult> LoadAsync(WorkspaceLoadRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _loads);
        _requests.Enqueue(request);

        if (Gate is { } gate)
            await gate.PassAsync(cancellationToken);

        return Answer is { } answer ? answer(request) : Solutions.Of(request, Projects);
    }
}

/// <summary>
/// Остановка внутри загрузки: тест видит, что загрузка вошла, и решает, когда ей идти дальше.
/// </summary>
internal sealed class LoadGate
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Загрузка дошла до остановки.</summary>
    public Task Entered => _entered.Task;

    /// <summary>Отпускает остановленную и все следующие.</summary>
    public void Release() => _released.TrySetResult();

    /// <summary>Проходит остановку: ждёт отпуска или отмены.</summary>
    public async Task PassAsync(CancellationToken cancellationToken)
    {
        _entered.TrySetResult();

        await _released.Task.WaitAsync(cancellationToken);
    }
}

/// <summary>Снимки решений, собранные руками.</summary>
internal static class Solutions
{
    /// <summary>Решение из названных проектов, лежащих папками рядом с точкой входа.</summary>
    public static WorkspaceLoadResult Of(
        WorkspaceLoadRequest request,
        IEnumerable<(string Name, string[] Items)> projects,
        params ProjectDiagnostic[] diagnostics)
    {
        var solution = new SolutionSnapshotBuilder
        {
            Workspace = request.Workspace,
            Solution = SolutionIdentity.Create(request.Workspace, request.EntryPointPath),
            Name = Path.GetFileNameWithoutExtension(request.EntryPointPath.Value),
            ProviderName = "Scripted",
            Request = request,
        };

        foreach (var (name, items) in projects)
            solution.Projects.Add(Project(request, name, items));

        foreach (var diagnostic in diagnostics)
            solution.Diagnostics.Add(diagnostic);

        return WorkspaceLoadResult.Success(solution.ToSnapshot());
    }

    /// <summary>Проект <c>имя/имя.csproj</c> рядом с точкой входа.</summary>
    public static ProjectSnapshot Project(WorkspaceLoadRequest request, string name, params string[] items)
    {
        var file = request.EntryPointPath.Directory.Combine(Path.Combine(name, name + ".csproj"));

        var project = new ProjectSnapshotBuilder
        {
            Identity = ProjectIdentity.Create(request.Workspace, file),
            ProjectFilePath = file,
            Name = name,
            ProviderName = "Scripted",
            ActiveConfiguration = request.Configuration,
        };

        project.EvaluationInputs.Add(file);

        foreach (var item in items)
        {
            project.Items.Add(new ProjectItem
            {
                ItemType = ProjectItemTypes.Compile,
                Include = item,
                FullPath = file.Directory.Combine(item),
                Origin = ProjectItemOrigin.Declared,
            });
        }

        return project.ToSnapshot();
    }

    /// <summary>Провал загрузки с одной ошибкой на файле.</summary>
    public static WorkspaceLoadResult Failure(string code, CanonicalPath file) =>
        WorkspaceLoadResult.Failure(ProjectDiagnostic.ForFile(
            code, $"{code}: не открылось", ProjectDiagnosticSeverity.Error, file));
}
