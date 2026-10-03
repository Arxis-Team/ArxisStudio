using System.Runtime.Loader;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Modules.Xaml.Documents;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace ArxisStudio.Modules.Xaml.Session;

/// <summary>
/// Одно решение глазами службы XAML: профиль дизайна, хост поколения и открытые документы.
/// </summary>
/// <remarks>
/// <para>
/// Хост поколения (<see cref="ProjectDesignHost"/>) не владеет ни моделью, ни диском: снимки и сборки
/// ему даёт профиль дизайна службы проектов (<see cref="StudioDesignSource"/>), пишет он службой файлов
/// (<see cref="StudioDesignWriter"/>), а перемены на диске узнаёт от её слежения за содержимым. Второго
/// владельца MSBuild в процессе нет, и сохранение дизайнера — запись студии с её историей.
/// </para>
/// <para>
/// <b>Порядок конца</b> — тот, что отпускает поколение: показы отдают корни, аренды узнают о закрытии,
/// хост закрывает документы и доказывает, что поколение ушло, и только потом отпускается профиль.
/// </para>
/// <para>Всё — в потоке интерфейса, кроме того, что хост делает у себя.</para>
/// </remarks>
internal sealed class XamlDesignSession : IAsyncDisposable
{
    /// <summary>Свойство, которое читает инспектор данных: компилируются ли привязки по умолчанию.</summary>
    public const string CompiledBindingsProperty = "AvaloniaUseCompiledBindingsByDefault";

    private readonly XamlService _owner;
    private readonly IStudioProjectProfile _profile;
    private readonly IStudioFiles _files;
    private readonly StudioDesignSource _source;
    private readonly IDisposable _fan;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<CanonicalPath, DocumentEntry> _documents = [];

    // Открытия в пути: пока документ открывается, простой не наступает.
    private int _opening;
    private bool _watching;
    private bool _disposed;
    private string? _restart;

    /// <summary>Поднимает профиль дизайна и хост; поколения ещё нет — его поднимает <see cref="StartAsync"/>.</summary>
    /// <param name="owner">Служба.</param>
    /// <param name="projects">Служба проектов.</param>
    /// <param name="files">Служба файлов.</param>
    /// <param name="status">Состояние службы проектов, для которого сессия.</param>
    public XamlDesignSession(XamlService owner, IStudioProjects projects, IStudioFiles files, ProjectsStatus status)
    {
        _owner = owner;
        _files = files;
        Bound = status.Session;
        Configuration = status.Configuration;

        _profile = projects.OpenProfile(new ProjectProfileRequest(ProjectProfileKind.Design)
        {
            AdditionalProperties = [.. ProjectDesignHostOptions.ReadProperties, CompiledBindingsProperty],
        });

        _source = new StudioDesignSource(_profile, Bound);

        Host = new ProjectDesignHost(_source, new ProjectDesignHostOptions
        {
            BuildProperties = _profile.GlobalProperties,
            Configuration = status.Configuration,
            BuildDelay = XamlSettings.BuildDelay(owner.Context.Settings),
            TimeProvider = owner.Options.TimeProvider,
            ExternalEditDescription = owner.Words.ChangedOutside,
            ReleaseHostState = ReleaseFocus,
            Writer = new StudioDesignWriter(files, ExpectedOf, owner.Words),
        });

        Host.StateChanged += OnStateChanged;
        Host.BuildCompleted += OnBuildCompleted;
        Host.SwapCompleted += OnSwapCompleted;
        Host.RestartRequired += OnRestartRequired;
        Host.ExternalConflict += OnExternalConflict;
        Host.DocumentMoved += OnDocumentMoved;
        Host.DocumentDeleted += OnDocumentDeleted;
        Host.ChangesApplied += OnChangesApplied;
        Host.ApplicationChanged += OnApplicationChanged;
        Host.PopulationFailed += OnPopulationFailed;
        Host.OperationFailed += OnOperationFailed;
        Host.Gate.Changed += OnGateChanged;

        _fan = Host.Register(new ParticipantFan(this));
    }

    /// <summary>Номер сессии службы проектов, чьё это решение.</summary>
    public long Bound { get; }

    /// <summary>Конфигурация, в которой оно оценено.</summary>
    public string? Configuration { get; }

    /// <summary>Хост поколения.</summary>
    public ProjectDesignHost Host { get; }

    /// <summary>
    /// Снимок профиля дизайна — тот, по которому работает хост: у его проектов свои идентичности, не
    /// основного сеанса.
    /// </summary>
    public SolutionSnapshot? DesignSnapshot => _source.Snapshot;

    /// <summary>Служба.</summary>
    public XamlService Owner => _owner;

    /// <summary>Поколение поднялось.</summary>
    public bool IsStarted { get; private set; }

    /// <summary>Сессия кончается или кончилась: новых документов она не откроет.</summary>
    public bool IsRetiring => _lifetime.IsCancellationRequested || _disposed;

    /// <summary>Отмена всего, что сессия начала: её жизнь.</summary>
    public CancellationToken Lifetime => _lifetime.Token;

    /// <summary>Аренды всех документов и открытия в пути.</summary>
    public int Leases => _opening + _documents.Values.Sum(entry => entry.Leases);

    /// <summary>Открытые документы.</summary>
    internal IReadOnlyCollection<DocumentEntry> Documents => _documents.Values;

    /// <summary>Подходит ли сессия к состоянию службы проектов: то же решение в той же конфигурации.</summary>
    /// <param name="status">Состояние.</param>
    public bool Fits(ProjectsStatus status) =>
        status.State != ProjectsState.Closed
        && status.Session == Bound
        && string.Equals(status.Configuration, Configuration, StringComparison.Ordinal);

    /// <summary>Поднимает поколение: первый снимок профиля, сборка устаревшего, загрузка.</summary>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

        _owner.Bind(Host.Gate);

        // Слушать диск — до подъёма: пока хост собирает и грузит, сохранённое копится в его очереди, а
        // не теряется.
        _files.ContentChanged += OnContentChanged;
        _watching = true;

        await _source.ReadyAsync(linked.Token);
        await Host.StartAsync(linked.Token);

        // Пока ничего не показано, после замены не прикрепляется ничего: прикрепит показ.
        Host.SetVisibleDocuments([]);

        IsStarted = true;

        foreach (var diagnostic in Host.GenerationDiagnostics)
            _owner.Log(StudioLogLevel.Info, $"{diagnostic.Code}: {diagnostic.Message}");
    }

    /// <summary>Открывает документ — или берёт ещё одну аренду открытого.</summary>
    /// <param name="path">Файл.</param>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    public async Task<IXamlDocumentHandle> OpenAsync(CanonicalPath path, CancellationToken cancellationToken)
    {
        ThrowIfRetiring();

        if (_documents.TryGetValue(path, out var open))
            return open.Lease();

        _opening++;

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

            var lending = new RootLending();
            var live = await Host.OpenDocumentAsync(path, new ProjectDesignDocumentOptions { RootAccess = lending }, linked.Token);

            ThrowIfRetiring();

            // Два открытия одного файла разом: хост отдал обоим один документ, и запись о нём одна.
            if (!_documents.TryGetValue(path, out var entry))
            {
                entry = new DocumentEntry(this, path, live, lending);
                _documents.Add(path, entry);
            }

            return entry.Lease();
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException
            && IsRetiring && !cancellationToken.IsCancellationRequested)
        {
            // Сессию кончили, пока документ открывался: решение закрыли или сменили. Открывавший ничего не
            // отменял, и отмена была бы ему неправдой.
            throw new InvalidOperationException(_owner.Words.NothingOpen, e);
        }
        finally
        {
            _opening--;

            if (Leases == 0)
                _owner.Idle(this);
        }
    }

    /// <summary>Текст открытого документа; null — не открыт.</summary>
    /// <param name="path">Файл.</param>
    public XamlDocument? SyntaxOf(CanonicalPath path) =>
        _documents.TryGetValue(path, out var entry) ? entry.Live.Document : null;

    /// <summary>Последняя аренда документа отпущена: документ закрывается, несохранённое уходит с ним.</summary>
    /// <param name="entry">Документ.</param>
    public async Task CloseAsync(DocumentEntry entry)
    {
        if (_documents.TryGetValue(entry.Path, out var mapped) && ReferenceEquals(mapped, entry))
            _documents.Remove(entry.Path);

        entry.Detach();
        UpdateVisible();

        if (!_disposed)
        {
            try
            {
                await Host.CloseDocumentAsync(entry.Live);
            }
            catch (ObjectDisposedException)
            {
                // Хост кончился раньше: его документы закрыл он сам.
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _owner.Log(StudioLogLevel.Warning, $"{entry.Path.FileName} закрылся со сбоем: {e.Message}");
            }
        }

        if (Leases == 0)
            _owner.Idle(this);
    }

    /// <summary>Говорит хосту, какие документы показаны: их он прикрепит к новому поколению.</summary>
    public void UpdateVisible()
    {
        if (_disposed)
            return;

        Host.SetVisibleDocuments([.. _documents.Values.Where(entry => entry.View is not null).Select(entry => entry.Live)]);
    }

    /// <summary>Где сессия — словами контракта.</summary>
    public (XamlDesignState State, string? Reason) Describe()
    {
        if (!IsStarted)
            return (XamlDesignState.Starting, null);

        return Host.State switch
        {
            ProjectDesignState.Starting => (XamlDesignState.Starting, null),
            ProjectDesignState.Live => (XamlDesignState.Live, null),
            ProjectDesignState.Building => (XamlDesignState.Building, null),
            ProjectDesignState.SwapPending => (XamlDesignState.SwapPending,
                Host.Gate.Reasons is { Length: > 0 } reasons ? _owner.Words.SwapHeld(string.Join(", ", reasons)) : Host.StaleReason),
            ProjectDesignState.Swapping => (XamlDesignState.Swapping, Host.StaleReason),
            ProjectDesignState.RestartRequired => (XamlDesignState.RestartRequired, _restart),
            ProjectDesignState.Unsupported => (XamlDesignState.Unsupported, Host.UnsupportedReason),
            _ => (XamlDesignState.Idle, null),
        };
    }

    /// <summary>Итог сборки словами контракта.</summary>
    /// <param name="result">Итог хоста.</param>
    public static XamlBuildOutcome Outcome(ProjectDesignBuildResult result) =>
        new(result.Status == ProjectOperationStatus.Succeeded, result.Reason, result.Diagnostics, result.TypesChanged, result.Duration);

    /// <summary>Отменяет всё, что сессия начала: подъём, открытия, ожидания.</summary>
    public void Cancel()
    {
        if (!_disposed)
            _lifetime.Cancel();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _lifetime.Cancel();

        if (_watching)
            _files.ContentChanged -= OnContentChanged;

        Host.StateChanged -= OnStateChanged;
        Host.BuildCompleted -= OnBuildCompleted;
        Host.SwapCompleted -= OnSwapCompleted;
        Host.RestartRequired -= OnRestartRequired;
        Host.ExternalConflict -= OnExternalConflict;
        Host.DocumentMoved -= OnDocumentMoved;
        Host.DocumentDeleted -= OnDocumentDeleted;
        Host.ChangesApplied -= OnChangesApplied;
        Host.ApplicationChanged -= OnApplicationChanged;
        Host.PopulationFailed -= OnPopulationFailed;
        Host.OperationFailed -= OnOperationFailed;
        Host.Gate.Changed -= OnGateChanged;

        // Сперва держатели: показы отдают корни, аренды узнают о закрытии. Поколение уйдёт, только
        // когда построенного из него не держит никто.
        foreach (var entry in _documents.Values.ToArray())
            entry.Close();

        _documents.Clear();
        _fan.Dispose();

        await Host.DisposeAsync();

        _source.Dispose();
        await _profile.DisposeAsync();
        _lifetime.Dispose();
    }

    /// <summary>Что документ файла считает его содержимым — сверка записи.</summary>
    private ReadOnlyMemory<byte>? ExpectedOf(CanonicalPath file) =>
        _documents.TryGetValue(file, out var entry) ? StudioDesignWriter.Encode(entry.Live.SavedText) : null;

    private void ThrowIfRetiring()
    {
        if (IsRetiring)
            throw new InvalidOperationException(_owner.Words.NothingOpen);
    }

    /// <summary>Документ хоста — запись сессии о нём.</summary>
    private DocumentEntry? EntryOf(XamlLiveDocument document) =>
        _documents.Values.FirstOrDefault(entry => ReferenceEquals(entry.Live, document));

    private void OnContentChanged(object? sender, FileContentChangedEventArgs e) =>
        Host.NotifyChanged(e.Changes.Select(change => change.Change));

    private void OnStateChanged(object? sender, ProjectDesignStateChangedEventArgs e) => _owner.Report();

    private void OnGateChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(_owner.Report);

    /// <remarks>
    /// Строка журнала собрана из полей итога, а не из его <c>ToString</c> и причины: их пишет адаптер
    /// по-английски. Ход по проектам журнал службы проектов уже рассказал.
    /// </remarks>
    private void OnBuildCompleted(object? sender, ProjectDesignBuildCompletedEventArgs e)
    {
        var result = e.Result;
        var succeeded = result.Status == ProjectOperationStatus.Succeeded;

        _owner.Log(
            succeeded ? StudioLogLevel.Info : StudioLogLevel.Warning,
            $"Сборка дизайна — {(succeeded ? "готово" : "не удалось")}, проектов {result.Projects.Length}"
            + (result.Restored ? ", с восстановлением" : string.Empty)
            + (result.TypesChanged ? ", типы сменились" : string.Empty)
            + $", {result.Duration.TotalMilliseconds:F0} мс");

        foreach (var diagnostic in result.Diagnostics.Where(diagnostic => diagnostic.Severity == ProjectDiagnosticSeverity.Error))
            _owner.Log(StudioLogLevel.Error, $"{diagnostic.Code}: {diagnostic.Message}");

        _owner.Built(Outcome(result));
    }

    /// <remarks>Как и у сборки — из полей отчёта, по шагам замены.</remarks>
    private void OnSwapCompleted(object? sender, ProjectDesignSwapCompletedEventArgs e)
    {
        var report = e.Report;

        _owner.Log(
            report.Reclaimed ? StudioLogLevel.Info : StudioLogLevel.Warning,
            $"Замена поколения — {(report.Reclaimed ? "прежнее ушло" : "прежнее держится")}: "
            + $"отпускание {report.Release.TotalMilliseconds:F0} мс, разбор {report.Teardown.TotalMilliseconds:F0} мс, "
            + $"проверка ухода {report.Reclaim.TotalMilliseconds:F0} мс, новое {report.Rebuild.TotalMilliseconds:F0} мс");
    }

    private void OnRestartRequired(object? sender, ProjectDesignRestartEventArgs e)
    {
        _restart = _owner.Words.Restart(e.Reason);

        _owner.Log(StudioLogLevel.Warning, $"{_restart} — {e.Message}");
        _owner.Context.GetService<IStudioRestart>()?.Require(_restart);
        _owner.Report();
    }

    private void OnExternalConflict(object? sender, ProjectDesignConflictEventArgs e) =>
        EntryOf(e.Document)?.Conflict(e.DiskText);

    private void OnDocumentMoved(object? sender, ProjectDesignDocumentEventArgs e)
    {
        if (EntryOf(e.Document) is not { } entry)
            return;

        if (_documents.TryGetValue(entry.Path, out var mapped) && ReferenceEquals(mapped, entry))
            _documents.Remove(entry.Path);

        _documents[e.File] = entry;
        entry.Moved(e.File);
    }

    private void OnDocumentDeleted(object? sender, ProjectDesignDocumentEventArgs e) =>
        EntryOf(e.Document)?.Deleted();

    private void OnChangesApplied(object? sender, ProjectDesignChangesEventArgs e)
    {
        foreach (var entry in _documents.Values.ToArray())
            entry.Settle();
    }

    private void OnApplicationChanged(object? sender, ProjectDesignApplicationEventArgs e)
    {
        foreach (var entry in _documents.Values.ToArray())
            entry.View?.ApplicationSaved(e.File);
    }

    private void OnPopulationFailed(object? sender, XamlLivePopulationFailedEventArgs e) =>
        _owner.Log(StudioLogLevel.Warning,
            $"{e.ControlType.Name}: живой документ не построил экземпляр, показан собранный — "
            + string.Join("; ", e.Diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => diagnostic.Message)));

    private void OnOperationFailed(object? sender, ProjectDesignFailureEventArgs e) =>
        _owner.Log(StudioLogLevel.Error, $"{e.Operation}: {e.Exception.GetType().Name}: {e.Exception.Message}");

    /// <summary>
    /// Последним перед выгрузкой: клавиатура, оставшаяся на контроле поколения, держала бы его.
    /// </summary>
    /// <remarks>
    /// Снимается только фокус, стоящий на объекте выгружаемого контекста: каретка человека в панелях
    /// студии от замены типов проекта не зависит и не должна прыгать.
    /// </remarks>
    private static ValueTask ReleaseFocus(CancellationToken cancellationToken)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return ValueTask.CompletedTask;

        foreach (var window in desktop.Windows)
        {
            if (window.FocusManager is { } focus
                && focus.GetFocusedElement() is { } focused
                && AssemblyLoadContext.GetLoadContext(focused.GetType().Assembly) is { IsCollectible: true })
            {
                focus.Focus(null);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Участник хоста от всей службы: участники контракта отпускают своё раньше показов и берут новое
    /// после них.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Отпуская, участник ещё видит корень на своём холсте и успевает заморозить кадр с формой — человек
    /// видит формы, а не пустоту, пока типы меняются; показы отдают корни следом. Беря новое, участник
    /// находит новый корень уже стоящим.
    /// </para>
    /// <para>
    /// Хост зовёт его, держа свою очередь, поэтому ни показ, ни участник не ждут здесь хоста: приложение
    /// формы показ берёт уже после замены.
    /// </para>
    /// </remarks>
    private sealed class ParticipantFan(XamlDesignSession session) : IProjectDesignParticipant
    {
        public async ValueTask ReleaseAsync(CancellationToken cancellationToken)
        {
            foreach (var participant in session._owner.Participants)
            {
                try
                {
                    await participant.ReleaseAsync(cancellationToken);
                }
                catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
                {
                    session._owner.Fail(e);
                }
            }

            foreach (var view in Views())
                view.LetGo();
        }

        public async ValueTask RestoreAsync(CancellationToken cancellationToken)
        {
            foreach (var view in Views())
                view.TakeUp();

            foreach (var participant in session._owner.Participants)
            {
                try
                {
                    await participant.RestoreAsync(cancellationToken);
                }
                catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
                {
                    session._owner.Fail(e);
                }
            }
        }

        private DesignView[] Views() => [.. session._documents.Values.Select(entry => entry.View).OfType<DesignView>()];
    }
}
