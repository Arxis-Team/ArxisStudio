using System.Collections.Concurrent;
using System.Collections.Immutable;
using ArxisStudio.Modules.Projects.Watching;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Слежение за диском на настоящих файлах: обе дороги доходят до «устарело».
/// </summary>
/// <remarks>
/// Склейка укорочена, а ожидание — сигналом с потолком, а не паузой: тест кончается, как только
/// слежение сказало своё.
/// </remarks>
public class ProjectsWatchTests : IDisposable
{
    private static readonly FileChangeCoalescingOptions Quick = new()
    {
        QuietPeriod = TimeSpan.FromMilliseconds(50),
        MaximumDelay = TimeSpan.FromMilliseconds(500),
    };

    private readonly string _root = TempFolder.Create("projects-watch");
    private readonly ConcurrentQueue<ImmutableArray<CanonicalPath>> _reports = new();
    private WorkspaceLoadRequest? _request;

    public void Dispose()
    {
        TempFolder.Erase(_root);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Правка файла решения доходит до «устарело» и называет его.
    /// </summary>
    /// <remarks>
    /// Решение — не вход оценки ни одного проекта, и следить за ним служба обязана отдельно:
    /// пропавший из решения проект — ровно та перемена, о которой стоит услышать.
    /// </remarks>
    [Fact]
    public async Task An_edit_of_the_solution_file_asks_for_a_reload_and_names_it()
    {
        var snapshot = OnDisk();
        var stale = new TaskCompletionSource<ImmutableArray<CanonicalPath>>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var watch = new ProjectsWatch(causes => stale.TrySetResult(causes), Quick);

        watch.Follow(snapshot);

        File.AppendAllText(snapshot.EntryPoint.Path.Value, "<!-- правка -->");

        var causes = await stale.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Contains(snapshot.EntryPoint.Path, causes);
    }

    /// <summary>
    /// Файл, появившийся под проектом, доходит до «устарело», хотя ни один вход оценки не менялся.
    /// </summary>
    [Fact]
    public async Task A_file_created_under_a_project_asks_for_a_reload_and_names_it()
    {
        var snapshot = OnDisk();
        var stale = new TaskCompletionSource<ImmutableArray<CanonicalPath>>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var watch = new ProjectsWatch(causes => stale.TrySetResult(causes), Quick);

        watch.Follow(snapshot);

        var extra = Path.Combine(_root, "Lib", "Extra.cs");

        File.WriteAllText(extra, "class Extra { }");

        var causes = await stale.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Contains(CanonicalPath.Create(extra), causes);
    }

    /// <summary>
    /// Переименование, сделанное службой, перезагрузки не просит, когда модель его уже перечитала.
    /// </summary>
    /// <remarks>
    /// Пачка правки приходит посреди перечитывания и сверилась бы с прежним снимком, где файл ещё под
    /// старым именем, — так каждое переименование перечитывало модель дважды. Придержанная, она
    /// сверяется со снимком, который перечитывание опубликовало.
    /// </remarks>
    [Fact]
    public void A_rename_the_service_made_asks_for_nothing_once_the_model_is_reread()
    {
        using var watch = Watching(OnDisk());

        using (var hold = watch.Hold())
        {
            File.Move(Lib("Greeter.cs"), Lib("Hello.cs"));
            Echo(watch, Lib("Greeter.cs"), Lib("Hello.cs"));

            watch.Follow(Reread("Hello.cs"));
            hold.Confirm();
        }

        Assert.Empty(_reports);
    }

    /// <summary>
    /// Файл проекта, переписанный службой, перезагрузки не просит, когда модель его уже перечитала.
    /// </summary>
    /// <remarks>
    /// Вход оценки снимок сверяет по пути, а не по содержимому: переписанный правкой файл проекта
    /// просил бы загрузку и после перечитывания, которое его прочло. Отпечаток, снятый сразу после
    /// правки, говорит, что файл тот же, — и тогда, когда настоящее эхо приходит позже.
    /// </remarks>
    [Fact]
    public void A_project_file_the_service_rewrote_asks_for_nothing_once_the_model_is_reread()
    {
        var snapshot = OnDisk();
        var project = Lib("Lib.csproj");

        using var watch = Watching(snapshot);

        using (var hold = watch.Hold())
        {
            File.WriteAllText(project, "<Project><!-- ссылки переписаны --></Project>");
            hold.Expect([CanonicalPath.Create(project)]);
            Echo(watch, project);

            watch.Follow(snapshot);
            hold.Confirm();
        }

        Echo(watch, project);

        Assert.Empty(_reports);
    }

    /// <summary>
    /// Файл, появившийся снаружи посреди правки, перезагрузку просит, если перечитывание его не
    /// увидело, — и называет причиной его одного.
    /// </summary>
    [Fact]
    public void An_outside_file_added_during_the_edit_still_asks_for_a_reload()
    {
        var extra = Lib("Extra.cs");

        using var watch = Watching(OnDisk());

        using (var hold = watch.Hold())
        {
            File.Move(Lib("Greeter.cs"), Lib("Hello.cs"));
            File.WriteAllText(extra, "class Extra { }");
            Echo(watch, Lib("Greeter.cs"), Lib("Hello.cs"), extra);

            // Перечитывание прочло папку раньше, чем в ней появился Extra.cs.
            watch.Follow(Reread("Hello.cs"));
            hold.Confirm();
        }

        Assert.NotEmpty(_reports);
        Assert.All(_reports, causes => Assert.Equal([CanonicalPath.Create(extra)], causes));
    }

    /// <summary>
    /// Файл проекта, переписанный снаружи после перечитывания, перезагрузку просит: отпечаток разошёлся.
    /// </summary>
    [Fact]
    public void A_project_file_rewritten_outside_after_the_reread_still_asks_for_a_reload()
    {
        var snapshot = OnDisk();
        var project = Lib("Lib.csproj");

        using var watch = Watching(snapshot);

        using (var hold = watch.Hold())
        {
            File.WriteAllText(project, "<Project><!-- ссылки переписаны --></Project>");
            hold.Expect([CanonicalPath.Create(project)]);
            Echo(watch, project);

            watch.Follow(snapshot);
            hold.Confirm();
        }

        File.WriteAllText(project, "<Project><!-- снаружи --></Project>");
        Echo(watch, project);

        Assert.NotEmpty(_reports);
        Assert.All(_reports, causes => Assert.Equal([CanonicalPath.Create(project)], causes));
    }

    /// <summary>
    /// Правка, после которой модель перечитать не вышло, остаётся слежению: её перемены просят загрузку.
    /// </summary>
    /// <remarks>
    /// Отпечаток говорит, что вход прочла модель, только если она его прочла: неудачное
    /// перечитывание нового снимка не опубликовало, и запомненное не подтверждается.
    /// </remarks>
    [Fact]
    public void An_edit_the_model_was_not_reread_after_still_asks_for_a_reload()
    {
        var project = Lib("Lib.csproj");

        using var watch = Watching(OnDisk());

        using (var hold = watch.Hold())
        {
            File.WriteAllText(project, "<Project><!-- ссылки переписаны --></Project>");
            hold.Expect([CanonicalPath.Create(project)]);
            Echo(watch, project);
        }

        Assert.NotEmpty(_reports);
        Assert.All(_reports, causes => Assert.Equal([CanonicalPath.Create(project)], causes));
    }

    /// <summary>Слежение по снимку, которое складывает свои приговоры набору.</summary>
    private ProjectsWatch Watching(SolutionSnapshot snapshot)
    {
        var watch = new ProjectsWatch(_reports.Enqueue, Quick);

        watch.Follow(snapshot);

        return watch;
    }

    /// <summary>Пачка правки — прямо слежению и сразу, не дожидаясь настоящих событий.</summary>
    private static void Echo(ProjectsWatch watch, params string[] paths)
    {
        foreach (var path in paths)
            watch.Report(path);

        watch.Flush();
    }

    /// <summary>Снимок, который опубликовало бы перечитывание: проект Lib с этими файлами.</summary>
    private SolutionSnapshot Reread(params string[] items) => Solutions.Of(_request!, [("Lib", items)]).Snapshot!;

    private string Lib(string name) => Path.Combine(_root, "Lib", name);

    /// <summary>Решение и проект Lib на диске.</summary>
    private SolutionSnapshot OnDisk()
    {
        var solution = Path.Combine(_root, "Hello.slnx");

        File.WriteAllText(solution, "<Solution />");
        Directory.CreateDirectory(Path.Combine(_root, "Lib"));
        File.WriteAllText(Path.Combine(_root, "Lib", "Lib.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_root, "Lib", "Greeter.cs"), "class Greeter { }");

        _request = new WorkspaceLoadRequest
        {
            EntryPointPath = CanonicalPath.Create(solution),
            Workspace = WorkspaceIdentity.New(),
        };

        return Solutions.Of(_request, [("Lib", new[] { "Greeter.cs" })]).Snapshot!;
    }
}
