using System.Collections.Concurrent;
using ArxisStudio.Modules.Projects.Delivery;
using ArxisStudio.Modules.Projects.Engine;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Доставка перемен службы проектов: склейка, порядок, поток и чей сбой.
/// </summary>
public class ProjectsDeliveryTests
{
    private static readonly WorkspaceLoadRequest Request = new()
    {
        EntryPointPath = ProjectsStudio.Solution(),
        Workspace = WorkspaceIdentity.New(),
    };

    /// <summary>
    /// Публикации, пришедшие, пока поток занят, доставляются одним событием с общей разностью.
    /// </summary>
    [Fact]
    public async Task Publications_while_the_thread_is_busy_arrive_as_one_event_with_the_composed_difference()
    {
        using var thread = new ProjectsTestThread();
        using var busy = new ManualResetEventSlim();

        var seen = new List<ProjectsChangedEventArgs>();
        var publisher = new ChangePublisher(this, thread, _ => { }, failed: null);

        publisher.Subscribe((_, change) => seen.Add(change));

        var s1 = Solution(("App", ["Program.cs"]), ("Lib", ["Greeter.cs"]));
        var s2 = Solution(("App", ["Program.cs", "Extra.cs"]), ("Lib", ["Greeter.cs"]));
        var s3 = Solution(("App", ["Program.cs", "Extra.cs"]), ("Tool", []));

        publisher.Publish(Ready(1, s1), SnapshotDiff.Step(null, s1));

        await thread.IdleAsync();

        seen.Clear();

        thread.Post(busy.Wait);

        publisher.Publish(Ready(2, s2), SnapshotDiff.Step(s1, s2));
        publisher.Publish(Ready(3, s3), SnapshotDiff.Step(s2, s3));

        busy.Set();

        await thread.IdleAsync();

        var change = Assert.Single(seen);

        Assert.Same(s1, change.Previous.Snapshot);
        Assert.Same(s3, change.Current.Snapshot);
        Assert.Equal("Tool", Assert.Single(change.Added).Name);
        Assert.Equal("Lib", Assert.Single(change.Removed).Name);

        // App поменялся на шаге, который сам не доставлялся, — и всё равно назван, объектом нового снимка.
        Assert.Same(s3.Projects.Single(project => project.Name == "App"), Assert.Single(change.Modified));
        Assert.True(change.SolutionChanged, "состав решения поменялся, а событие этого не сказало");
    }

    /// <summary>События приходят в поток интерфейса, и номера их растут.</summary>
    [Fact]
    public async Task Events_arrive_on_the_interface_thread_and_their_numbers_grow()
    {
        using var thread = new ProjectsTestThread();

        var onThread = new List<bool>();
        var numbers = new List<long>();
        var publisher = new ChangePublisher(this, thread, _ => { }, failed: null);

        publisher.Subscribe((_, change) =>
        {
            onThread.Add(thread.CheckAccess());
            numbers.Add(change.Current.Sequence);
        });

        for (var sequence = 1; sequence <= 6; sequence++)
        {
            publisher.Publish(new ProjectsStatus { Sequence = sequence, IsLoading = sequence % 2 == 1 }, SnapshotStep.None);

            if (sequence % 2 == 0)
                await thread.IdleAsync();
        }

        await thread.IdleAsync();

        Assert.All(onThread, Assert.True);
        Assert.Equal(numbers.Order(), numbers);
        Assert.Equal(6, numbers[^1]);
    }

    /// <summary>Упавший подписчик не мешает соседу, а его сбой уходит дальше.</summary>
    [Fact]
    public async Task A_subscriber_that_throws_does_not_keep_the_event_from_its_neighbour()
    {
        using var thread = new ProjectsTestThread();

        var failures = new ConcurrentQueue<Exception>();
        var reached = 0;
        var publisher = new ChangePublisher(this, thread, _ => { }, failures.Enqueue);

        publisher.Subscribe((_, _) => throw new InvalidOperationException("сосед сломан"));
        publisher.Subscribe((_, _) => reached++);

        publisher.Publish(new ProjectsStatus { Sequence = 1 }, SnapshotStep.None);

        await thread.IdleAsync();

        Assert.Equal(1, reached);
        Assert.Equal("сосед сломан", Assert.Single(failures).Message);
    }

    /// <summary>
    /// Без ловца сбой бросается заново в потоке — и со своим стеком, по которому студия найдёт виновного.
    /// </summary>
    [Fact]
    public async Task Without_a_catcher_the_failure_is_thrown_again_on_the_thread_with_its_own_stack()
    {
        using var thread = new ProjectsTestThread();

        var publisher = new ChangePublisher(this, thread, _ => { }, failed: null);

        publisher.Subscribe(Broken);
        publisher.Publish(new ProjectsStatus { Sequence = 1 }, SnapshotStep.None);

        await thread.IdleAsync();

        var crash = Assert.Single(thread.Crashes);

        Assert.Contains(nameof(Broken), crash.StackTrace, StringComparison.Ordinal);
    }

    /// <summary>Обработчик, прокачавший поток изнутри события, вложенного события не получит.</summary>
    [Fact]
    public async Task A_handler_that_pumps_the_thread_gets_no_nested_event()
    {
        using var thread = new ProjectsTestThread();

        var publisher = new ChangePublisher(this, thread, _ => { }, failed: null);
        var numbers = new List<long>();
        var inside = false;
        var nested = false;

        publisher.Subscribe((_, change) =>
        {
            nested |= inside;
            numbers.Add(change.Current.Sequence);

            if (change.Current.Sequence != 1)
                return;

            inside = true;
            publisher.Publish(new ProjectsStatus { Sequence = 2 }, SnapshotStep.None);
            thread.PumpNested();
            inside = false;
        });

        publisher.Publish(new ProjectsStatus { Sequence = 1 }, SnapshotStep.None);

        await thread.IdleAsync();

        Assert.False(nested, "обработчик получил событие изнутри своего же");
        Assert.Equal(new long[] { 1, 2 }, numbers);
    }

    /// <summary>Своя реакция модуля идёт раньше подписчиков.</summary>
    [Fact]
    public async Task The_module_reaction_comes_before_the_subscribers()
    {
        using var thread = new ProjectsTestThread();

        var order = new List<string>();
        var publisher = new ChangePublisher(this, thread, _ => order.Add("модуль"), failed: null);

        publisher.Subscribe((_, _) => order.Add("подписчик"));
        publisher.Publish(new ProjectsStatus { Sequence = 1 }, SnapshotStep.None);

        await thread.IdleAsync();

        Assert.Equal(new[] { "модуль", "подписчик" }, order);
    }

    /// <summary>Отписанный обработчик не зовётся.</summary>
    [Fact]
    public async Task An_unsubscribed_handler_is_not_called()
    {
        using var thread = new ProjectsTestThread();

        var calls = 0;
        EventHandler<ProjectsChangedEventArgs> handler = (_, _) => calls++;
        var publisher = new ChangePublisher(this, thread, _ => { }, failed: null);

        publisher.Subscribe(handler);
        publisher.Unsubscribe(handler);
        publisher.Publish(new ProjectsStatus { Sequence = 1 }, SnapshotStep.None);

        await thread.IdleAsync();

        Assert.Equal(0, calls);
    }

    /// <summary>Событие называет ровно те поля, что разошлись, и не отдаёт пустых массивов по умолчанию.</summary>
    [Fact]
    public void An_event_names_exactly_the_fields_that_differ()
    {
        var before = new ProjectsStatus { Sequence = 1, Session = 1, State = ProjectsState.Opening, IsLoading = true };
        var after = before with { Sequence = 2, State = ProjectsState.Failed, IsLoading = false, Configuration = "Release" };

        var change = new ProjectsChangedEventArgs(before, after, default, default, default, solutionChanged: false);

        Assert.Equal(ProjectsChanges.State | ProjectsChanges.Loading | ProjectsChanges.Configuration, change.Changes);
        Assert.Empty(change.Added);
        Assert.Empty(change.Removed);
        Assert.Empty(change.Modified);
    }

    private static void Broken(object? sender, ProjectsChangedEventArgs change) =>
        throw new InvalidOperationException("обработчик плагина");

    private static ProjectsStatus Ready(long sequence, SolutionSnapshot snapshot) => new()
    {
        Sequence = sequence,
        Session = 1,
        State = ProjectsState.Ready,
        Snapshot = snapshot,
    };

    private static SolutionSnapshot Solution(params (string Name, string[] Items)[] projects) =>
        Solutions.Of(Request, projects).Snapshot!;
}
