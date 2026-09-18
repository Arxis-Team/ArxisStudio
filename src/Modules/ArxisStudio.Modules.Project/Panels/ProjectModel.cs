using System.ComponentModel;
using System.Globalization;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Tree;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>Что показывает окно проекта.</summary>
internal enum ProjectState
{
    /// <summary>Службы проектов нет — показывать нечего.</summary>
    NoService,

    /// <summary>Решение не открыто.</summary>
    Closed,

    /// <summary>Решение открывается впервые, снимка ещё нет.</summary>
    Opening,

    /// <summary>Решение не открылось.</summary>
    Failed,

    /// <summary>Дерево решения.</summary>
    Ready,
}

/// <summary>
/// Окно проекта без окна: состояние, дерево и то, что с ними делают.
/// </summary>
/// <remarks>
/// Решение приходит от службы проектов событием в потоке интерфейса. Подписка — первой, чтение
/// состояния — вторым: иначе событие, пришедшее между ними, потерялось бы; а событие со своим номером
/// не новее прочитанного отбрасывается — оно о том, что уже учтено.
/// <para>
/// Дерево строится вне потока интерфейса: проверка диска для решения на тысячи файлов — это тысячи
/// обращений к файловой системе. Каждая постройка берёт билет, и применяется только последняя:
/// снимок, пришедший, пока диск отвечал о прежнем, делает прежний ответ ненужным.
/// </para>
/// </remarks>
internal sealed class ProjectModel : INotifyPropertyChanged, IDisposable
{
    private readonly IStudioContext _context;
    private readonly IStudioProjects? _projects;
    private CancellationTokenSource? _building;
    private SolutionSnapshot? _snapshot;
    private ProjectsLoad? _load;
    private ProjectsLoad? _dismissed;
    private CanonicalPath _entry = CanonicalPath.None;
    private long _sequence = -1;
    private long _ticket;
    private bool _disposed;

    /// <summary>Заводит окно и подписывает его на службу проектов.</summary>
    /// <param name="context">Контекст модуля.</param>
    /// <param name="words">Подписи дерева на языке студии: первая постройка идёт прямо здесь.</param>
    public ProjectModel(IStudioContext context, Words words)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(words);

        _context = context;
        Words = words;
        _projects = context.Projects();

        if (_projects is null)
        {
            State = ProjectState.NoService;
            return;
        }

        _projects.Changed += OnChanged;
        Apply(_projects.Status, rebuild: true);
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Строки дерева.</summary>
    public RowList Tree { get; } = new();

    /// <summary>Что показывает окно.</summary>
    public ProjectState State { get; private set; } = ProjectState.Closed;

    /// <summary>Службы проектов нет.</summary>
    public bool IsNoService => State == ProjectState.NoService;

    /// <summary>Решение не открыто.</summary>
    public bool IsClosed => State == ProjectState.Closed;

    /// <summary>Решение открывается.</summary>
    public bool IsOpening => State == ProjectState.Opening;

    /// <summary>Решение не открылось.</summary>
    public bool IsFailed => State == ProjectState.Failed;

    /// <summary>Показано дерево.</summary>
    public bool IsReady => State == ProjectState.Ready;

    /// <summary>Что открывается: «Открываю TestApp.sln…».</summary>
    public string? Opening { get; private set; }

    /// <summary>Почему решение не открылось.</summary>
    public string? Failure { get; private set; }

    /// <summary>Решение перезагружается, а дерево показывает прежний снимок.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>Перезагрузка не удалась, и дерево показывает последнее загруженное.</summary>
    public bool IsStale { get; private set; }

    /// <summary>Поиск ничего не нашёл.</summary>
    public bool NothingFound => Tree.IsFiltered && Tree.Found == 0;

    /// <summary>Последняя постройка дерева — тесты ждут её, а не времени.</summary>
    internal Task Settled { get; private set; } = Task.CompletedTask;

    /// <summary>Подписи дерева на языке студии.</summary>
    internal Words Words { get; private set; } = Words.English;

    /// <summary>Строит дерево заново с новыми словами — язык студии сменился.</summary>
    /// <param name="words">Подписи на новом языке.</param>
    public void Relabel(Words words)
    {
        Words = words;

        if (_snapshot is not null)
            Build(_snapshot, forget: false);
    }

    /// <summary>Ищет по решению.</summary>
    /// <param name="query">Запрос; пусто — показать всё.</param>
    public void Search(string? query)
    {
        Tree.Filter(query);
        Raise(nameof(NothingFound));
    }

    /// <summary>Открывает файл в редакторе студии.</summary>
    /// <param name="node">Узел файла.</param>
    public void Open(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Path.IsEmpty || _context.GetService<IStudioDocuments>() is not { } documents)
            return;

        _ = documents.OpenAsync(node.Path.Value);
    }

    /// <summary>Открывает решение, выбранное человеком.</summary>
    /// <param name="path">Путь решения или проекта.</param>
    public Task OpenSolution(string path)
    {
        if (_projects is null || !CanonicalPath.TryCreate(path, out var entry))
            return Task.CompletedTask;

        return _projects.OpenAsync(entry);
    }

    /// <summary>Повторяет открытие, которое не удалось.</summary>
    public Task Retry() => _projects?.ReloadAsync() ?? Task.CompletedTask;

    /// <summary>Прячет предупреждение о неудачной перезагрузке.</summary>
    /// <remarks>
    /// Прячет о той загрузке, о которой говорило: следующая неудачная скажет о себе заново, а
    /// всё, что приходит до неё, — начало перезагрузки, её спиннер — прочитанного не возвращает.
    /// </remarks>
    public void Dismiss()
    {
        _dismissed = _load;
        IsStale = false;
        Raise(nameof(IsStale));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_projects is not null)
            _projects.Changed -= OnChanged;

        _building?.Cancel();
        _building?.Dispose();
        _building = null;
    }

    private void OnChanged(object? sender, ProjectsChangedEventArgs e)
    {
        if (_disposed || e.Current.Sequence <= _sequence)
            return;

        Apply(e.Current, rebuild: (e.Changes & (ProjectsChanges.Snapshot | ProjectsChanges.Session)) != 0);
    }

    private void Apply(ProjectsStatus status, bool rebuild)
    {
        _sequence = status.Sequence;
        _load = status.LastLoad;

        var snapshot = status.Snapshot;
        var failed = status.LastLoad?.Result is { HasErrors: true, HasSnapshot: false } result ? result : null;

        IsLoading = status.IsLoading && snapshot is not null;
        IsStale = status.State == ProjectsState.Failed && snapshot is not null && !ReferenceEquals(_load, _dismissed);
        Opening = status.EntryPoint.IsEmpty ? null : Format("project.state.opening", status.EntryPoint.FileName);
        Failure = failed?.Diagnostics.FirstOrDefault(diagnostic => diagnostic.IsError)?.Message
                  ?? status.LastLoad?.Result.Diagnostics.FirstOrDefault()?.Message;

        State = snapshot is not null ? ProjectState.Ready
            : status.State switch
            {
                ProjectsState.Opening => ProjectState.Opening,
                ProjectsState.Failed => ProjectState.Failed,
                _ => ProjectState.Closed,
            };

        if (snapshot is null)
        {
            _snapshot = null;
            _ticket++;
            _building?.Cancel();
            Tree.Show(null, forget: false);
        }
        else if (rebuild || !ReferenceEquals(snapshot, _snapshot))
        {
            // Раскрытое забывается, только когда открыто другое решение: перезагрузка того же —
            // новый файл, другая конфигурация — должна оставить дерево таким, каким его раскрыли.
            var forget = !_entry.IsEmpty && _entry != status.EntryPoint;

            _entry = status.EntryPoint;
            Build(snapshot, forget);
        }

        Raise(null);
    }

    private void Build(SolutionSnapshot snapshot, bool forget)
    {
        _snapshot = snapshot;

        var ticket = ++_ticket;
        var words = Words;

        _building?.Cancel();
        _building?.Dispose();
        _building = new CancellationTokenSource();

        var cancellation = _building.Token;
        var scheduler = SynchronizationContext.Current is null
            ? TaskScheduler.Current
            : TaskScheduler.FromCurrentSynchronizationContext();

        Settled = Task.Run(() => SolutionTree.Build(snapshot, DiskProbe.Present(snapshot, cancellation), words), cancellation)
            .ContinueWith(
                built =>
                {
                    if (_disposed || ticket != _ticket)
                        return;

                    // Постройка упала — это ошибка окна, а не решения, и дерево остаётся прежним.
                    // Молча оно выглядело бы просто устаревшим; журнал называет причину.
                    if (built.Exception?.GetBaseException() is { } failure)
                    {
                        _context.Log.Write(
                            StudioLogLevel.Error,
                            ProjectModule.LogSource,
                            $"Дерево решения {snapshot.Name} не построилось: {failure.Message}");
                        return;
                    }

                    if (!built.IsCompletedSuccessfully)
                        return;

                    Tree.Show(built.Result, forget);
                    Raise(nameof(NothingFound));
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                scheduler);
    }

    private string Format(string key, string value) =>
        string.Format(CultureInfo.CurrentCulture, _context.Strings[key], value);

    private void Raise(string? property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
