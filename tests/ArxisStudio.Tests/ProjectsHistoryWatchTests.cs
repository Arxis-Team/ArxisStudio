using ArxisStudio.LocalHistory;
using ArxisStudio.Modules.Projects;
using ArxisStudio.Modules.Projects.History;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Локальная история службы проектов на настоящих файлах: опорный снимок, внешние правки, отбор.
/// </summary>
/// <remarks>
/// <para>
/// Правки сообщаются наблюдателю прямо и отдаются сразу, а не ждут тишины: тест кончается, как
/// только история своё записала. Настоящие наблюдатели папок при этом тоже работают, и их пачки
/// приходят когда придут, — поэтому проверяется то, что от их прихода не зависит: история пишет
/// только расхождение, и повтор пачки ничего не добавляет.
/// </para>
/// <para>
/// Историю каждый тест ведёт в своей временной папке. Остальные тесты процесса историю не ведут
/// вовсе (<see cref="TestLocalHistory"/>).
/// </para>
/// </remarks>
public sealed class ProjectsHistoryWatchTests : IDisposable
{
    private static readonly FileChangeCoalescingOptions Quick = new()
    {
        QuietPeriod = TimeSpan.FromMilliseconds(50),
        MaximumDelay = TimeSpan.FromMilliseconds(500),
    };

    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"arxis-history-watch-{Guid.NewGuid():N}")).FullName;

    private string HistoryRoot => Path.Combine(_root, "history");

    private string Lib => Path.Combine(_root, "solution", "Lib");

    private string Greeter => Path.Combine(Lib, "Greeter.cs");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Внешняя правка записывается один раз и с тем, что было до неё.
    /// </summary>
    /// <remarks>
    /// «До» есть потому, что опорный снимок запомнил файл при первом взгляде. Повтор той же пачки —
    /// наблюдатель папок сообщает о записи по нескольку раз — второго действия не даёт.
    /// </remarks>
    [Fact]
    public async Task An_external_edit_is_written_once_with_what_was_there_before()
    {
        using var studio = new ProjectsStudio();
        using var recorder = Recorder(studio);
        using var watcher = Follow(recorder);

        await Settle(recorder);

        var store = Store(recorder);
        var before = store.Known(Greeter)?.Content;

        Assert.NotNull(before);
        Assert.Empty(store.Actions);

        File.WriteAllText(Greeter, "class Greeter { void Hi() { } }");
        await Report(recorder, watcher, Greeter);

        var action = Assert.Single(store.Actions);
        var change = Assert.Single(action.Changes);

        Assert.Equal(HistoryOrigin.External, action.Origin);
        Assert.Equal(recorder.Words.External, action.Label);
        Assert.Equal(HistoryChangeKind.Modified, change.Kind);
        Assert.Equal(before, change.Before);
        Assert.Equal(store.Known(Greeter)?.Content, change.After);
        Assert.Equal("class Greeter { void Hi() { } }"u8.ToArray(), store.Read(change.After!.Value));

        await Report(recorder, watcher, Greeter);

        Assert.Single(store.Actions);
    }

    /// <summary>
    /// Опорный снимок запоминает незнакомое молча, а разошедшееся, пока студия была закрыта, пишет.
    /// </summary>
    /// <remarks>
    /// Переименованное мимо студии пишется переездом, а файл той же длины и того же времени записи
    /// не читается вовсе — как у git и IntelliJ, это цена того, что большое решение открывается без
    /// хэширования каждого файла.
    /// </remarks>
    [Fact]
    public async Task The_baseline_learns_quietly_and_writes_what_changed_while_closed()
    {
        var notes = Path.Combine(Lib, "Notes.md");
        var same = Path.Combine(Lib, "Same.cs");

        using var studio = new ProjectsStudio();

        OnDisk();
        File.WriteAllText(notes, "# заметки");
        File.WriteAllText(same, "class Same { }");

        using (var first = Recorder(studio))
        using (Follow(first))
        {
            await Settle(first);

            Assert.Empty(Store(first).Actions);

            // Узнанное опорным снимком уходит на диск в конце обхода, а не с первой пачкой после
            // него: пачек может не быть долго, а упавшая студия иначе снимала бы решение заново.
            Assert.True(File.Exists(Path.Combine(HistoryRoot, "state.json")), "опорный снимок остался только в памяти");

            first.Dispose();
            await first.Completion.WaitAsync(TimeSpan.FromSeconds(30), Token);
        }

        var written = File.GetLastWriteTimeUtc(same);

        File.WriteAllText(Greeter, "class Greeter { int Changed; }");
        File.Move(notes, Path.Combine(Lib, "Readme.md"));
        File.WriteAllText(same, "class Diff { }");
        File.SetLastWriteTimeUtc(same, written);

        using var second = Recorder(studio);
        using var watcher = Follow(second);

        await Settle(second);

        var actions = Store(second).Actions;
        var changes = actions.SelectMany(action => action.Changes).ToList();

        Assert.All(actions, action => Assert.Equal(second.Words.Offline, action.Label));
        Assert.Contains(changes, change => change is { Kind: HistoryChangeKind.Modified } && change.Path == Greeter);
        Assert.Contains(changes, change => change.Kind == HistoryChangeKind.Moved
            && change.From == notes
            && change.Path == Path.Combine(Lib, "Readme.md"));
        Assert.DoesNotContain(changes, change => change.Path == same);
    }

    /// <summary>Выход сборки, служебные папки и временные файлы история не пишет и не помнит.</summary>
    [Fact]
    public async Task Build_output_service_folders_and_temporary_files_stay_out()
    {
        OnDisk();

        string[] outside =
        [
            Path.Combine(Lib, "bin", "Debug", "Lib.dll"),
            Path.Combine(Lib, "obj", "Lib.AssemblyInfo.cs"),
            Path.Combine(Lib, ".vs", "state.json"),
            Path.Combine(Lib, "node_modules", "left-pad", "index.js"),
            Path.Combine(Lib, "Greeter.cs~"),
        ];

        foreach (var file in outside)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "шум");
        }

        using var studio = new ProjectsStudio();
        using var recorder = Recorder(studio);
        using var watcher = Follow(recorder);

        await Settle(recorder);

        var store = Store(recorder);

        Assert.All(outside, file => Assert.Null(store.Known(file)));

        foreach (var file in outside)
            File.WriteAllText(file, "ещё шум");

        await Report(recorder, watcher, outside);

        Assert.Empty(store.Actions);
    }

    /// <summary>Переименование мимо студии пишется переездом, а не пропажей и появлением.</summary>
    [Fact]
    public async Task An_external_rename_is_written_as_a_move()
    {
        using var studio = new ProjectsStudio();
        using var recorder = Recorder(studio);
        using var watcher = Follow(recorder);

        await Settle(recorder);

        var renamed = Path.Combine(Lib, "Welcome.cs");

        File.Move(Greeter, renamed);
        await Report(recorder, watcher, Greeter, renamed);

        var change = Assert.Single(Assert.Single(Store(recorder).Actions).Changes);

        Assert.Equal(HistoryChangeKind.Moved, change.Kind);
        Assert.Equal(Greeter, change.From);
        Assert.Equal(renamed, change.Path);
        Assert.Null(Store(recorder).Known(Greeter));
        Assert.NotNull(Store(recorder).Known(renamed));
    }

    /// <summary>
    /// Папка, удалённая мимо студии, уносит с собой всё, что история под ней знала, — с содержимым.
    /// </summary>
    [Fact]
    public async Task A_folder_deleted_outside_takes_its_known_files_along()
    {
        OnDisk();

        var models = Path.Combine(Lib, "Models");

        Directory.CreateDirectory(models);
        File.WriteAllText(Path.Combine(models, "A.cs"), "class A { }");
        File.WriteAllText(Path.Combine(models, "B.cs"), "class B { }");

        using var studio = new ProjectsStudio();
        using var recorder = Recorder(studio);
        using var watcher = Follow(recorder);

        await Settle(recorder);

        var store = Store(recorder);
        var known = new[] { "A.cs", "B.cs" }.Select(name => store.Known(Path.Combine(models, name))?.Content).ToList();

        Directory.Delete(models, recursive: true);

        // Наблюдатель папок сообщает и о папке, и о каждом её файле — одной пачкой; файл, пришедший
        // обоими путями, записывается один раз.
        await Report(recorder, watcher, models, Path.Combine(models, "A.cs"), Path.Combine(models, "B.cs"));

        var changes = Assert.Single(store.Actions).Changes;

        Assert.Equal(2, changes.Length);
        Assert.All(changes, change => Assert.Equal(HistoryChangeKind.Deleted, change.Kind));
        Assert.Equal(
            known.Select(id => id?.Value).Order(StringComparer.Ordinal),
            changes.Select(change => change.Before?.Value).Order(StringComparer.Ordinal));
        Assert.All(changes, change => Assert.NotNull(store.Read(change.Before!.Value)));
    }

    /// <summary>
    /// Состояние, которое студия записала сама, наблюдатель повтором не пишет.
    /// </summary>
    /// <remarks>
    /// Так будет у каждого действия службы файлов: она запоминает, каким стал файл, и наблюдатель,
    /// увидевший ту же запись на диске, находит в истории её же.
    /// </remarks>
    [Fact]
    public async Task What_the_studio_wrote_itself_is_not_written_again()
    {
        using var studio = new ProjectsStudio();
        using var recorder = Recorder(studio);
        using var watcher = Follow(recorder);

        await Settle(recorder);

        var store = Store(recorder);

        File.WriteAllText(Greeter, "class Greeter { string Own; }");
        store.Learn(Greeter, store.Capture(Greeter)!);

        await Report(recorder, watcher, Greeter);

        Assert.Empty(store.Actions);
    }

    /// <summary>Вторая студия над той же папкой историю не пишет и говорит об этом.</summary>
    [Fact]
    public async Task A_second_studio_leaves_the_history_to_the_first()
    {
        using var holder = LocalHistoryStore.Open(HistoryRoot);
        using var studio = new ProjectsStudio();
        using var recorder = Recorder(studio);

        await Settle(recorder);

        Assert.Null(recorder.Store);
        Assert.Contains(studio.Written, record => record.Message.Contains("другая студия", StringComparison.Ordinal));
    }

    /// <summary>Переменная среды выключает историю, а явная папка сильнее неё.</summary>
    [Fact]
    public void The_environment_turns_history_off_and_an_explicit_folder_wins()
    {
        Assert.Equal("0", Environment.GetEnvironmentVariable(HistoryRecorder.EnvironmentVariable));
        Assert.Null(HistoryRecorder.Root(null));
        Assert.Equal(HistoryRoot, HistoryRecorder.Root(HistoryRoot));
    }

    /// <summary>
    /// Служба ведёт историю открытого решения и гасит её, когда человек её выключил.
    /// </summary>
    [Fact]
    public async Task The_service_keeps_the_history_of_the_open_solution_until_turned_off()
    {
        OnDisk();

        using var studio = new ProjectsStudio(historyRoot: HistoryRoot);

        studio.Provider.Projects = [("Lib", ["Greeter.cs"])];

        await studio.Projects.OpenAsync(CanonicalPath.Create(Path.Combine(_root, "solution", "Hello.slnx")), Token);

        var host = ((ProjectsModule)Assert.Single(studio.Module.Entries)).Host!;
        var history = host.History;

        await Settle(history);

        var watcher = Assert.IsType<HistoryWatcher>(host.Session?.History);

        Assert.NotNull(Store(history).Known(Greeter));

        File.WriteAllText(Greeter, "class Greeter { bool Kept; }");
        await Report(history, watcher, Greeter);

        Assert.Equal(HistoryChangeKind.Modified, Assert.Single(Assert.Single(Store(history).Actions).Changes).Kind);

        studio.Settings.Set(ProjectsSettings.HistoryKey, false);
        await Settle(history);

        Assert.Null(host.Session?.History);
        Assert.Null(history.Store);
    }

    /// <summary>Решение на диске: проект <c>Lib</c> с одним файлом.</summary>
    private SolutionSnapshot OnDisk()
    {
        var solution = Path.Combine(_root, "solution", "Hello.slnx");

        Directory.CreateDirectory(Lib);

        if (!File.Exists(solution))
            File.WriteAllText(solution, "<Solution />");

        if (!File.Exists(Path.Combine(Lib, "Lib.csproj")))
            File.WriteAllText(Path.Combine(Lib, "Lib.csproj"), "<Project />");

        if (!File.Exists(Greeter))
            File.WriteAllText(Greeter, "class Greeter { }");

        var request = new WorkspaceLoadRequest
        {
            EntryPointPath = CanonicalPath.Create(solution),
            Workspace = WorkspaceIdentity.New(),
        };

        return Solutions.Of(request, [("Lib", new[] { "Greeter.cs" })]).Snapshot!;
    }

    /// <summary>История на контексте поднятого модуля — с его настройками, словарями и журналом.</summary>
    private HistoryRecorder Recorder(ProjectsStudio studio) =>
        new(studio.Module.Studio ?? throw new InvalidOperationException("у модуля нет контекста"), HistoryRoot, Quick);

    private HistoryWatcher Follow(HistoryRecorder recorder)
    {
        var watcher = recorder.Watch() ?? throw new InvalidOperationException("история не ведётся");

        watcher.Follow(OnDisk());

        return watcher;
    }

    private static LocalHistoryStore Store(HistoryRecorder recorder) =>
        recorder.Store ?? throw new InvalidOperationException("история не открылась");

    /// <summary>Сообщает о путях, отдаёт пачку сразу и ждёт, пока история её запишет.</summary>
    private static async Task Report(HistoryRecorder recorder, HistoryWatcher watcher, params string[] paths)
    {
        foreach (var path in paths)
            watcher.Report(path);

        watcher.Flush();
        await Settle(recorder);
    }

    /// <summary>
    /// Ждёт, пока очередь записи опустеет: несколько кругов, потому что дело опорного снимка ставит
    /// в очередь свои куски.
    /// </summary>
    private static async Task Settle(HistoryRecorder recorder)
    {
        for (var round = 0; round < 4; round++)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Assert.True(recorder.Enqueue(() =>
            {
                done.TrySetResult();
                return Task.CompletedTask;
            }), "очередь истории закрыта");

            await done.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        }
    }
}
