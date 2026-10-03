using System.Collections.Immutable;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;

namespace ArxisStudio.Modules.Projects.Watching;

/// <summary>
/// Слежение за содержимым файлов решения: что случилось с каждым путём — пачками, как их склеил
/// коалесцер.
/// </summary>
/// <remarks>
/// Отдельно от слежения за составом (<see cref="ProjectsWatch"/>): тому важно, поменялась ли модель, а
/// этому — что стало с каждым файлом, и сохранённая форма для него не новая и не переехавшая. Наблюдатель
/// ядра (<see cref="ProjectSourceWatcher"/>) говорит каждую перемену с её видом, коалесцер склеивает
/// сохранение через временный файл в одно <see cref="FileChangeKind.Changed"/> (ADR 0025 ProjectSystem).
/// Живёт, пока у службы файлов есть подписчик на перемены содержимого.
/// </remarks>
internal sealed class ContentWatch : IDisposable
{
    private readonly FileChangeCoalescer _coalescer;
    private readonly ProjectSourceWatcher _watcher;

    /// <summary>Заводит слежение.</summary>
    /// <param name="batch">Кому отдать пачку — в потоке таймера коалесцера.</param>
    /// <param name="coalescing">Склейка событий.</param>
    public ContentWatch(Action<ImmutableArray<FileChange>> batch, FileChangeCoalescingOptions coalescing)
    {
        _coalescer = FileChangeCoalescer.ForChanges(batch, coalescing);
        _watcher = new ProjectSourceWatcher(_coalescer.Add);
    }

    /// <summary>Следит за папками проектов снимка; прежние, которых в нём нет, отпускает.</summary>
    /// <param name="snapshot">Снимок, опубликованный последним.</param>
    public void Follow(SolutionSnapshot snapshot) => _watcher.Watch(snapshot);

    /// <inheritdoc/>
    public void Dispose()
    {
        _watcher.Dispose();
        _coalescer.Dispose();
    }
}
