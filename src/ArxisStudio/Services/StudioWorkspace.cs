using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;

namespace ArxisStudio.Services;

/// <summary>
/// Открытое в студии решение или проект.
/// </summary>
/// <remarks>
/// Обёртка над <see cref="ProjectWorkspace"/>: студии нужен один открытый
/// проект за раз и простые ответы — снапшот, диагностики, состояние загрузки.
/// Всё остальное (провайдеры, запросы, версии) остаётся внутри.
/// </remarks>
public sealed class StudioWorkspace : IAsyncDisposable,
    Modules.Designer.IDesignerWorkspace,
    Modules.Project.IProjectWorkspace
{
    private readonly ProjectWorkspace _workspace = new(new MSBuildProjectProvider());

    /// <inheritdoc/>
    public event EventHandler? SnapshotChanged;

    /// <summary>Путь к открытому решению или проекту; null, если ничего не открыто.</summary>
    public string? EntryPointPath { get; private set; }

    /// <summary>Последний снапшот модели; null, пока проект не открыт.</summary>
    public SolutionSnapshot? Snapshot { get; private set; }

    /// <summary>Диагностики последнего открытия: ошибки и предупреждения.</summary>
    public IReadOnlyList<ProjectDiagnostic> Diagnostics { get; private set; } = [];

    /// <summary>Открытие завершилось без ошибок.</summary>
    public bool IsLoaded => Snapshot is not null;

    /// <summary>Открывает решение или проект.</summary>
    /// <param name="path">Путь к <c>.sln</c>, <c>.slnx</c> или <c>.csproj</c>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Ошибка, если модель построить не удалось; иначе null.</returns>
    public async Task<string?> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        EntryPointPath = path;

        try
        {
            var result = await _workspace.LoadAsync(
                new WorkspaceLoadRequest
                {
                    Workspace = _workspace.Identity,
                    EntryPointPath = CanonicalPath.Create(path),
                    Options = new WorkspaceLoadOptions { IncludeItems = true },
                },
                cancellationToken);

            Snapshot = result.Snapshot;
            Diagnostics = result.Diagnostics;
            SnapshotChanged?.Invoke(this, EventArgs.Empty);

            // Снапшот с ошибками — всё ещё модель: часть проектов открылась, и
            // показать её полезнее, чем сообщить об отказе.
            return Snapshot is null
                ? FirstError(result.Diagnostics) ?? "Модель проекта не построена"
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Snapshot = null;
            Diagnostics = [];
            return e.Message;
        }
    }

    /// <summary>Находит проект, которому принадлежит файл.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    public ProjectSnapshot? FindProjectForFile(string filePath) =>
        Snapshot is { } snapshot && CanonicalPath.TryCreate(filePath, out var path) &&
        snapshot.TryGetProjectForFile(path, out var project)
            ? project
            : null;

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _workspace.DisposeAsync();

    private static string? FirstError(IEnumerable<ProjectDiagnostic> diagnostics) =>
        diagnostics.FirstOrDefault(d => d.IsError) is { } error
            ? $"{error.Code}: {error.Message}"
            : null;
}
