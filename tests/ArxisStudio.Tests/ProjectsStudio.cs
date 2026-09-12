using System.Collections.Concurrent;
using System.Collections.Immutable;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Projects;
using ArxisStudio.Modules.Projects.Watching;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Студия с модулем «Проекты», собранная так, как её собирает студия.
/// </summary>
/// <remarks>
/// Модуль поднимает хост, контекст выдаёт фабрика студии, служба берётся экспортом — той же
/// дорогой, какой её возьмёт плагин. Подменены только поток интерфейса, движок и слежение: их тест
/// держит в руках. Настройки пишутся во временный файл, а не в пользовательский.
/// </remarks>
internal sealed class ProjectsStudio : IDisposable
{
    private readonly string _settings =
        Path.Combine(Path.GetTempPath(), $"arxis-projects-settings-{Guid.NewGuid():N}.json");

    /// <summary>Поднимает модуль.</summary>
    /// <param name="workspace">Движок сессии; null — провайдер теста.</param>
    /// <param name="watch">Слежение за диском; null — не следить.</param>
    /// <param name="rethrow">
    /// Бросать ли исключение подписчика заново в потоке, как в студии; иначе оно копится в
    /// <see cref="Failures"/>.
    /// </param>
    public ProjectsStudio(
        Func<ProjectWorkspace>? workspace = null,
        Func<Action<ImmutableArray<CanonicalPath>>, IProjectsWatch?>? watch = null,
        bool rethrow = false)
    {
        var options = new ProjectsHostOptions
        {
            Workspace = workspace ?? (() => new ProjectWorkspace(Provider)),
            Thread = Thread,
            Watch = watch ?? (_ => null),
            SubscriberFailed = rethrow ? null : Failures.Enqueue,
        };

        var services = new Dictionary<Type, object>
        {
            [typeof(ProjectsHostOptions)] = options,
            [typeof(IStudioStatus)] = Status,
        };

        Contexts = new StudioContextFactory(
            Log,
            Commands,
            projectPath: null,
            services,
            settings: new PluginSettingsStore(projectPath: null, userFile: _settings),
            guard: Guard,
            plugins: Roster,
            exports: Exports);

        Host = new PluginHost(Contexts);

        Host.Unloading += (_, id) =>
        {
            Commands.RemoveOwnedBy(id);
            Exports.RemoveOwnedBy(id);
        };

        Roster.Attach(Host, () => []);
        Guard.Failed += (_, failure) => Strikes.Enqueue(failure);

        Module = Host.LoadBuiltIn(typeof(ProjectsModule).Assembly);

        Assert.True(Module.IsLoaded, Module.Error);

        Projects = Exports.Get(typeof(IStudioProjects)) as IStudioProjects
            ?? throw new InvalidOperationException("модуль поднялся, а службу не опубликовал");

        Build = Exports.Get(typeof(IStudioBuild)) as IStudioBuild
            ?? throw new InvalidOperationException("модуль поднялся, а службу сборки не опубликовал");

        Packages = Exports.Get(typeof(IStudioPackages)) as IStudioPackages
            ?? throw new InvalidOperationException("модуль поднялся, а службу пакетов не опубликовал");
    }

    /// <summary>Поток интерфейса.</summary>
    public ProjectsTestThread Thread { get; } = new();

    /// <summary>Провайдер теста — движок по умолчанию.</summary>
    public ScriptedProvider Provider { get; } = new();

    /// <summary>Журнал.</summary>
    public StudioLog Log { get; } = new();

    /// <summary>Что служба проектов написала в журнал: им она и отчитывается о найденном.</summary>
    public IEnumerable<StudioLogRecord> Written =>
        Log.Records.Where(record => record.Source == ProjectsModule.LogSource);

    /// <summary>Команды.</summary>
    public StudioCommands Commands { get; } = new();

    /// <summary>Экспорты.</summary>
    public StudioExportRegistry Exports { get; } = new();

    /// <summary>Строка состояния.</summary>
    public StatusProbe Status { get; } = new();

    /// <summary>Шов сбоев.</summary>
    public PluginGuard Guard { get; } = new();

    /// <summary>Служба соседей.</summary>
    public StudioPluginRoster Roster { get; } = new();

    /// <summary>Фабрика контекстов.</summary>
    public StudioContextFactory Contexts { get; }

    /// <summary>Хост.</summary>
    public PluginHost Host { get; }

    /// <summary>Поднятый модуль.</summary>
    public LoadedPlugin Module { get; }

    /// <summary>Служба — так, как её видит плагин.</summary>
    public IStudioProjects Projects { get; }

    /// <summary>Служба сборки — тем же путём.</summary>
    public IStudioBuild Build { get; }

    /// <summary>Служба пакетов — тем же путём.</summary>
    public IStudioPackages Packages { get; }

    /// <summary>Исключения подписчиков, когда их не бросают заново.</summary>
    public ConcurrentQueue<Exception> Failures { get; } = new();

    /// <summary>Сбои, засчитанные модулям и плагинам.</summary>
    public ConcurrentQueue<PluginFailure> Strikes { get; } = new();

    /// <summary>Настройки модуля — той же службой, что у самого модуля.</summary>
    public IStudioSettings Settings => Contexts.Issued["arxis.projects"];

    /// <summary>Путь к решению; на диске его нет, провайдер теста диск не читает.</summary>
    public static CanonicalPath Solution(string name = "Hello") =>
        CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "arxis-projects-fixture", name, name + ".slnx"));

    /// <summary>Записывает доставленные события.</summary>
    public List<ProjectsChangedEventArgs> Record()
    {
        var seen = new List<ProjectsChangedEventArgs>();

        Projects.Changed += (_, change) => seen.Add(change);

        return seen;
    }

    /// <summary>Ждёт состояния, отвечающего условию: доставленного или уже опубликованного.</summary>
    public Task<ProjectsStatus> WhenAsync(Func<ProjectsStatus, bool> condition, TimeSpan? timeout = null)
    {
        var reached = new TaskCompletionSource<ProjectsStatus>(TaskCreationOptions.RunContinuationsAsynchronously);

        Projects.Changed += (_, change) =>
        {
            if (condition(change.Current))
                reached.TrySetResult(change.Current);
        };

        if (Projects.Status is var status && condition(status))
            reached.TrySetResult(status);

        return reached.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(30));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Host.Dispose();
        Thread.Dispose();

        try
        {
            File.Delete(_settings);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Строка состояния, которая помнит сказанное.</summary>
internal sealed class StatusProbe : IStudioStatus
{
    /// <summary>Что сказали, по порядку.</summary>
    public ConcurrentQueue<string> Said { get; } = new();

    /// <inheritdoc/>
    public void Show(string message) => Said.Enqueue(message);
}

/// <summary>Слежение, которым тест управляет: что велели следить и когда сказать «устарело».</summary>
/// <param name="stale">Кому служба велела говорить.</param>
internal sealed class FakeWatch(Action<ImmutableArray<CanonicalPath>> stale) : IProjectsWatch
{
    /// <summary>Снимки, за которыми велели следить.</summary>
    public List<SolutionSnapshot> Followed { get; } = [];

    /// <summary>Слежение погашено.</summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    public void Follow(SolutionSnapshot snapshot) => Followed.Add(snapshot);

    /// <summary>Говорит службе, что на диске перемена.</summary>
    public void Stale(ImmutableArray<CanonicalPath> causes) => stale(causes);

    /// <inheritdoc/>
    public void Dispose() => IsDisposed = true;
}

/// <summary>Диск из двух множеств.</summary>
internal sealed class FakeDisk : IDiskView
{
    /// <summary>Файлы.</summary>
    public HashSet<CanonicalPath> Files { get; } = [];

    /// <summary>Папки.</summary>
    public HashSet<CanonicalPath> Directories { get; } = [];

    /// <inheritdoc/>
    public bool IsFile(CanonicalPath path) => Files.Contains(path);

    /// <inheritdoc/>
    public bool IsDirectory(CanonicalPath path) => Directories.Contains(path);

    /// <inheritdoc/>
    public IEnumerable<CanonicalPath> FilesUnder(CanonicalPath directory) =>
        Files.Where(file => file != directory && file.StartsWith(directory));
}
