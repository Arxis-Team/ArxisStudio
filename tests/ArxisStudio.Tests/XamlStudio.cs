using System.Collections.Concurrent;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Projects;
using ArxisStudio.Modules.Projects.Delivery;
using ArxisStudio.Modules.Xaml;
using ArxisStudio.Modules.Xaml.Session;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Xaml;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Студия со службами проектов и XAML, собранная так, как её собирает студия.
/// </summary>
/// <remarks>
/// <para>
/// Оба модуля поднимает хост, контекст выдаёт фабрика студии, службы берутся экспортом — той же
/// дорогой, какой их возьмёт дизайнер или плагин. Поток у обеих — поток интерфейса Avalonia, как в
/// студии: хост поколения зовёт и слышит там же, и второй поток рядом с ним проверял бы не студию.
/// </para>
/// <para>
/// Решение лежит на диске по-настоящему — проект <c>App</c> в своей папке, разметка в нём, — потому
/// что служба читает документы и пишет их, а слежение видит запись. Модель отдаёт провайдер теста:
/// снимок из того, что лежит в папке проекта, с глобальными свойствами запроса — профиль дизайна
/// читает со своими.
/// </para>
/// </remarks>
internal sealed class XamlStudio : IAsyncDisposable
{
    private readonly string _settings;

    /// <summary>Поднимает обе службы.</summary>
    /// <param name="idleRelease">Простой до конца сессии; по умолчанию — до закрытия решения.</param>
    /// <param name="services">Службы сверх своих — швы модулей, которые тест поднимет рядом.</param>
    public XamlStudio(TimeSpan? idleRelease = null, IReadOnlyDictionary<Type, object>? services = null)
    {
        Root = TempFolder.Create("xaml");
        _settings = Path.Combine(Root, "plugin-settings.json");

        Directory.CreateDirectory(ProjectFolder);

        Provider.Answer = Answer;

        var projects = new ProjectsHostOptions
        {
            Workspace = () => new ProjectWorkspace(Provider),
            Thread = AvaloniaProjectsThread.Instance,
            Watch = _ => null,
            SubscriberFailed = Failures.Enqueue,
            ContentCoalescing = new FileChangeCoalescingOptions
            {
                QuietPeriod = TimeSpan.FromMilliseconds(50),
                MaximumDelay = TimeSpan.FromMilliseconds(500),
            },
        };

        var xaml = new XamlServiceOptions
        {
            SubscriberFailed = Failures.Enqueue,
            IdleRelease = idleRelease ?? Timeout.InfiniteTimeSpan,
        };

        var all = new Dictionary<Type, object>
        {
            [typeof(ProjectsHostOptions)] = projects,
            [typeof(XamlServiceOptions)] = xaml,
            [typeof(IStudioStatus)] = Status,
        };

        foreach (var (type, service) in services ?? new Dictionary<Type, object>())
            all[type] = service;

        Contexts = new StudioContextFactory(
            Log,
            Commands,
            projectPath: null,
            all,
            settings: new PluginSettingsStore(projectPath: null, userFile: _settings),
            guard: Guard,
            plugins: Roster,
            exports: Exports,
            restart: (id, reason) => Restarts.Enqueue((id, reason)));

        Host = new PluginHost(Contexts);

        Host.Unloading += (_, id) =>
        {
            Commands.RemoveOwnedBy(id);
            Exports.RemoveOwnedBy(id);
        };

        Roster.Attach(Host, () => []);

        var projectsModule = Host.LoadBuiltIn(typeof(ProjectsModule).Assembly);
        Assert.True(projectsModule.IsLoaded, projectsModule.Error);

        XamlModule = Host.LoadBuiltIn(typeof(XamlModule).Assembly);
        Assert.True(XamlModule.IsLoaded, XamlModule.Error);

        Projects = Exports.Get(typeof(IStudioProjects)) as IStudioProjects
            ?? throw new InvalidOperationException("служба проектов не опубликована");

        Documents = Exports.Get(typeof(IStudioXamlDocuments)) as IStudioXamlDocuments
            ?? throw new InvalidOperationException("служба документов XAML не опубликована");

        Design = Exports.Get(typeof(IStudioXamlDesign)) as IStudioXamlDesign
            ?? throw new InvalidOperationException("служба поколения XAML не опубликована");

        Types = Exports.Get(typeof(IStudioXamlTypes)) as IStudioXamlTypes
            ?? throw new InvalidOperationException("служба типов XAML не опубликована");
    }

    /// <summary>Папка теста.</summary>
    public string Root { get; }

    /// <summary>Папка проекта App.</summary>
    public string ProjectFolder => Path.Combine(Root, "App");

    /// <summary>Файл проекта App — он же точка входа; на диске его нет, провайдер теста его не читает.</summary>
    public CanonicalPath ProjectFile => CanonicalPath.Create(Path.Combine(ProjectFolder, "App.csproj"));

    /// <summary>Выход проекта, когда тест его положил; null — у проекта выхода нет.</summary>
    public CanonicalPath? Output { get; set; }

    /// <summary>Имя сборки проекта — то, под которым живут его ресурсы.</summary>
    public string AssemblyName { get; set; } = "App";

    /// <summary>Версия Avalonia, которую восстановление нашло проекту; null — не сказано.</summary>
    public string? AvaloniaVersion { get; set; }

    /// <summary>Провайдер модели.</summary>
    public ScriptedProvider Provider { get; } = new();

    /// <summary>Журнал.</summary>
    public StudioLog Log { get; } = new();

    /// <summary>Что написала служба XAML.</summary>
    public IEnumerable<StudioLogRecord> Written => Log.Records.Where(record => record.Source == Modules.Xaml.XamlModule.LogSource);

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

    /// <summary>Поднятый модуль XAML.</summary>
    public LoadedPlugin XamlModule { get; }

    /// <summary>Служба проектов — так, как её видит плагин.</summary>
    public IStudioProjects Projects { get; }

    /// <summary>Документы — тем же путём.</summary>
    public IStudioXamlDocuments Documents { get; }

    /// <summary>Поколение — тем же путём.</summary>
    public IStudioXamlDesign Design { get; }

    /// <summary>Типы, которые ставят в форму, — тем же путём.</summary>
    public IStudioXamlTypes Types { get; }

    /// <summary>Сбои чужого кода, которые в студии ушли бы ей необработанными.</summary>
    public ConcurrentQueue<Exception> Failures { get; } = new();

    /// <summary>Просьбы о перезапуске: кто и почему.</summary>
    public ConcurrentQueue<(string Plugin, string Reason)> Restarts { get; } = new();

    /// <summary>Служба изнутри.</summary>
    public XamlService Service =>
        ((Modules.Xaml.XamlModule)Assert.Single(XamlModule.Entries)).Service ?? throw new InvalidOperationException("служба не поднята");

    /// <summary>Сессия решения изнутри.</summary>
    public XamlDesignSession Session => Service.Session ?? throw new InvalidOperationException("сессии нет");

    /// <summary>Путь файла в папке проекта.</summary>
    /// <param name="name">Имя от папки проекта.</param>
    public CanonicalPath PathOf(string name) => CanonicalPath.Create(Path.Combine(ProjectFolder, name));

    /// <summary>Кладёт файл в папку проекта, как его положил бы человек или другой редактор.</summary>
    /// <param name="name">Имя от папки проекта.</param>
    /// <param name="text">Содержимое.</param>
    /// <returns>Путь.</returns>
    public CanonicalPath Write(string name, string text)
    {
        var path = PathOf(name);

        Directory.CreateDirectory(Path.GetDirectoryName(path.Value)!);
        File.WriteAllText(path.Value, text);

        return path;
    }

    /// <summary>Открывает решение и ждёт снимка.</summary>
    public async Task OpenAsync()
    {
        var result = await Projects.OpenAsync(ProjectFile);

        Assert.True(result.Status != WorkspaceLoadStatus.Failed, string.Join("; ", result.Diagnostics));
    }

    /// <summary>Ждёт, пока условие станет правдой, прокачивая поток интерфейса, — а не время.</summary>
    /// <param name="condition">Условие.</param>
    /// <param name="what">Что ждали — для сообщения.</param>
    public static async Task UntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"не дождались: {what}");

            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Ждёт события и отдаёт его, прокачивая поток интерфейса.</summary>
    /// <param name="subscribe">Подписка на событие, отвечающая отпиской.</param>
    /// <param name="what">Что ждали.</param>
    public static async Task<T> NextAsync<T>(Func<Action<T>, Action> subscribe, string what)
    {
        var heard = new List<T>();
        var unsubscribe = subscribe(heard.Add);

        try
        {
            await UntilAsync(() => heard.Count > 0, what);

            return heard[0];
        }
        finally
        {
            unsubscribe();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await Service.StopAsync();

        Host.Dispose();

        TempFolder.Erase(Root);
    }

    /// <summary>Снимок того, что лежит в папке проекта: разметка — AvaloniaXaml, код — Compile.</summary>
    private WorkspaceLoadResult Answer(WorkspaceLoadRequest request)
    {
        var project = new ProjectSnapshotBuilder
        {
            Identity = ProjectIdentity.Create(request.Workspace, ProjectFile),
            Name = "App",
            ProjectFilePath = ProjectFile,
        };

        project.Properties["AssemblyName"] = AssemblyName;

        if (Output is { } output)
            project.Outputs.Add(new OutputArtifact { Kind = OutputArtifactKind.Assembly, Path = output });

        if (AvaloniaVersion is { } version)
            project.ResolvedPackages.Add(new ResolvedPackage { PackageId = "Avalonia", Version = version });

        foreach (var file in Directory.EnumerateFiles(ProjectFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(ProjectFolder, file);

            if (relative.StartsWith("bin", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("obj", StringComparison.OrdinalIgnoreCase))
                continue;

            var type = Path.GetExtension(file) switch
            {
                ".axaml" => "AvaloniaXaml",
                ".cs" => "Compile",
                _ => null,
            };

            if (type is not null)
                project.Items.Add(new ProjectItem { ItemType = type, Include = relative, FullPath = CanonicalPath.Create(file) });
        }

        var solution = new SolutionSnapshotBuilder
        {
            Workspace = request.Workspace,
            Name = "App",
            Request = request,
        };

        solution.Projects.Add(project.ToSnapshot());

        return WorkspaceLoadResult.Success(solution.ToSnapshot());
    }
}
