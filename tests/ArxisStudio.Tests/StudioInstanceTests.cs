using System.Diagnostics;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Один экземпляр студии на папку данных: занятость, передача аргументов, упавший хозяин.
/// </summary>
/// <remarks>
/// Имена занятости в тестах свои, случайные: запущенная рядом студия держит настоящее
/// имя, и тест, занявший его, отдал бы ей свои просьбы.
/// <para>
/// Вторая студия в тесте живёт в своём потоке, и это не удобство, а условие. Мьютекс
/// повторно входим для потока-хозяина: второй захват из того же потока вышел бы, и
/// «вторая» студия сочла бы себя первой. Между процессами так не бывает, а внутри одного
/// потока бывает всегда — первая редакция тестов на этом зависла, и нашлась заодно
/// пустая петля в слушателе канала.
/// </para>
/// </remarks>
public class StudioInstanceTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    /// <summary>Первая студия над папкой — первая, следующая — нет.</summary>
    [Fact]
    public void The_first_to_claim_the_folder_is_first_and_the_next_is_not()
    {
        var name = Unique();

        using var first = StudioInstance.Claim(name);

        var (secondIsFirst, _) = Second(name, arguments: null);

        Assert.True(first.IsFirst);
        Assert.False(secondIsFirst);
    }

    /// <summary>Аргументы второй студии доходят до первой.</summary>
    [Fact]
    public void Arguments_sent_by_a_second_studio_reach_the_first()
    {
        var name = Unique();
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Hello.slnx"));

        using var first = StudioInstance.Claim(name);
        using var arrived = new ManualResetEventSlim();
        string[]? received = null;

        first.Listen(arguments =>
        {
            received = arguments;
            arrived.Set();
        });

        var (_, sent) = Second(name, [path]);

        Assert.True(sent, "первая студия не приняла просьбу");
        Assert.True(arrived.Wait(Wait, TestContext.Current.CancellationToken), "просьба не дошла до слушателя");
        Assert.Equal([path], Assert.IsType<string[]>(received));
    }

    /// <summary>
    /// Относительный путь делается полным там, откуда позвали вторую студию.
    /// </summary>
    /// <remarks>
    /// У первой студии своя рабочая папка, и тот же «Hello.slnx» указал бы у неё на чужой
    /// файл. Ключи при этом остаются ключами: путь из них не делают.
    /// </remarks>
    [Fact]
    public void A_relative_path_is_made_full_where_the_second_studio_was_called()
    {
        var name = Unique();

        using var first = StudioInstance.Claim(name);
        using var arrived = new ManualResetEventSlim();
        string[]? received = null;

        first.Listen(arguments =>
        {
            received = arguments;
            arrived.Set();
        });

        var (_, sent) = Second(name, ["--verbose", "Hello.slnx"]);

        Assert.True(sent);
        Assert.True(arrived.Wait(Wait, TestContext.Current.CancellationToken));
        Assert.Equal(["--verbose", Path.GetFullPath("Hello.slnx")], Assert.IsType<string[]>(received));
    }

    /// <summary>
    /// Просьба, пришедшая раньше слушателя, дожидается его.
    /// </summary>
    /// <remarks>
    /// Вторая студия может постучаться, пока первая ещё под заставкой. Потерянная просьба —
    /// двойной щелчок по решению, после которого не открылось ничего.
    /// </remarks>
    [Fact]
    public void A_request_that_came_before_anyone_listened_waits_for_the_listener()
    {
        var name = Unique();

        using var first = StudioInstance.Claim(name);
        using var arrived = new ManualResetEventSlim();
        string[]? received = null;

        var (_, sent) = Second(name, ["--early"]);

        Assert.True(sent);

        first.Listen(arguments =>
        {
            received = arguments;
            arrived.Set();
        });

        Assert.True(arrived.Wait(Wait, TestContext.Current.CancellationToken), "ранняя просьба потерялась");
        Assert.Equal(["--early"], Assert.IsType<string[]>(received));
    }

    /// <summary>Отпущенную папку занимает следующий.</summary>
    [Fact]
    public void A_released_folder_can_be_claimed_again()
    {
        var name = Unique();

        StudioInstance.Claim(name).Dispose();

        var (nextIsFirst, _) = Second(name, arguments: null);

        Assert.True(nextIsFirst);
    }

    /// <summary>
    /// Упавшая студия папку не держит.
    /// </summary>
    /// <remarks>
    /// Ради этого занятость и держит мьютекс, а не файл-замок: хозяин, ушедший без
    /// отпускания, оставляет мьютекс брошенным, и следующий честно становится первым.
    /// Файл-замок пережил бы падение и не пустил бы студию вовсе. Падение здесь — поток,
    /// взявший мьютекс и закончившийся без отпускания: для системы это та же смерть хозяина.
    /// </remarks>
    [Fact]
    public void A_studio_that_died_holding_the_folder_does_not_keep_it()
    {
        var name = Unique();

        var dead = new Thread(() =>
        {
            var mutex = new Mutex(false, @"Local\" + name);

            mutex.WaitOne();
            GC.KeepAlive(mutex);
        });

        dead.Start();
        dead.Join();

        var (nextIsFirst, _) = Second(name, arguments: null);

        Assert.True(nextIsFirst, "папку держит хозяин, которого уже нет");
    }

    /// <summary>
    /// Не отвечающая первая не держит вторую дольше отпущенного.
    /// </summary>
    /// <remarks>
    /// Человек, щёлкнувший по значку, обязан получить окно. Первая, которая держит папку и
    /// молчит, — повисшая, — не должна оставить его ни с чем: вторая ждёт своё время и
    /// поднимается сама.
    /// </remarks>
    [Fact]
    public void A_first_that_does_not_answer_does_not_keep_the_second_waiting()
    {
        var name = Unique();
        using var holding = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();

        var silent = new Thread(() =>
        {
            using var mutex = new Mutex(false, @"Local\" + name);

            mutex.WaitOne();
            holding.Set();
            done.Wait();
            mutex.ReleaseMutex();
        });

        silent.Start();
        holding.Wait(TestContext.Current.CancellationToken);

        try
        {
            var clock = Stopwatch.StartNew();
            var (secondIsFirst, sent) = Second(name, ["--anyone"], TimeSpan.FromMilliseconds(300));

            Assert.False(secondIsFirst);
            Assert.False(sent);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"вторая ждала {clock.Elapsed}");
        }
        finally
        {
            done.Set();
            silent.Join();
        }
    }

    /// <summary>
    /// Перезапущенная копия ждёт папку и получает её, как только прежняя отпустит, — а третий запуск
    /// в щель между ними не входит.
    /// </summary>
    /// <remarks>
    /// Новая копия не дожидается выхода прежней, чтобы потом спросить папку без ожидания: между
    /// «прежняя ушла» и «новая спросила» третий запуск стал бы первым, и две студии поделили бы
    /// одну папку. Мьютекс ждётся — и переходит из рук в руки без щели.
    /// </remarks>
    [Fact]
    public void A_waiting_claim_gets_the_folder_once_let_go_and_a_third_does_not_slip_in()
    {
        var name = Unique();
        var first = StudioInstance.Claim(name);
        var restartedIsFirst = false;

        using var got = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();

        var restarted = new Thread(() =>
        {
            using var claim = StudioInstance.Claim(name, TimeSpan.FromSeconds(10));

            restartedIsFirst = claim.IsFirst;
            got.Set();
            done.Wait(TimeSpan.FromSeconds(10));
        });

        restarted.Start();

        try
        {
            Assert.True(first.IsFirst);

            // Пока прежняя держит папку, ждущая не возвращается: не жди она — ответила бы сразу, и
            // «не первой».
            Assert.False(
                got.Wait(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken),
                "перезапущенная копия не ждала папку, которую держит прежняя");

            // Тем же потоком, что брал: так отпускает прежняя копия на выходе из цикла приложения.
            first.Dispose();

            Assert.True(got.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "ждущая копия папку не получила");
            Assert.True(restartedIsFirst);

            var (thirdIsFirst, _) = Second(name, arguments: null);

            Assert.False(thirdIsFirst, "третий запуск занял папку, которую держит перезапущенная копия");
        }
        finally
        {
            done.Set();
            restarted.Join();
        }
    }

    /// <summary>
    /// Остановленная первая держит папку, но просьб больше не принимает.
    /// </summary>
    /// <remarks>
    /// Так ведёт себя студия, решившаяся на перезапуск: принятая теперь просьба ушла бы вместе с
    /// процессом, а отпущенная папка — третьему запуску, раньше новой копии.
    /// </remarks>
    [Fact]
    public void A_stopped_first_keeps_the_folder_and_answers_nobody()
    {
        var name = Unique();

        using var first = StudioInstance.Claim(name);

        first.Listen(_ => { });
        first.Stop();

        var (secondIsFirst, sent) = Second(name, ["--anyone"], TimeSpan.FromMilliseconds(300));

        Assert.False(secondIsFirst, "остановленная первая отпустила папку");
        Assert.False(sent, "остановленная первая приняла просьбу");
    }

    /// <summary>Разные папки данных — разные студии; регистр пути на Windows не различается.</summary>
    [Fact]
    public void Different_data_folders_are_different_studios()
    {
        var home = Path.Combine(Path.GetTempPath(), "arxis-home");

        Assert.NotEqual(StudioInstance.NameFor(home), StudioInstance.NameFor(home + "-other"));
        Assert.Equal(StudioInstance.NameFor(home), StudioInstance.NameFor(home + Path.DirectorySeparatorChar));

        if (OperatingSystem.IsWindows())
            Assert.Equal(StudioInstance.NameFor(home.ToUpperInvariant()), StudioInstance.NameFor(home.ToLowerInvariant()));
    }

    /// <summary>
    /// Вторая студия: занимает папку в своём потоке и, если есть что, отдаёт аргументы.
    /// </summary>
    /// <remarks>
    /// Всё, что делает вторая, — захват, передача и отпускание, — идёт в одном потоке, как
    /// в отдельном процессе: мьютекс отпускается тем, кто брал.
    /// </remarks>
    private static (bool IsFirst, bool Sent) Second(string name, string[]? arguments, TimeSpan? patience = null)
    {
        (bool, bool) result = default;

        var thread = new Thread(() =>
        {
            using var second = StudioInstance.Claim(name);

            result = (second.IsFirst, arguments is not null && !second.IsFirst && second.Send(arguments, patience));
        });

        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "вторая студия не закончила разговор");

        return result;
    }

    private static string Unique() => "ArxisStudio-test-" + Guid.NewGuid().ToString("N")[..12];
}
