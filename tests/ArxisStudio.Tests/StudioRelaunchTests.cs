using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Перезапуск на уровне процессов: ключи, команда новой копии, ответ прежней и ожидание её ухода.
/// </summary>
/// <remarks>
/// Настоящих процессов тест не поднимает и не снимает: прежний процесс подменяется швом
/// <c>StudioRelaunch.Find</c>, а новая копия — файлом, который стирает другой поток. Шов
/// процессный, поэтому тесты, трогающие его, стоят в общей коллекции.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioRelaunchTests : IDisposable
{
    private readonly string _folder = TempFolder.Create("relaunch");
    private readonly Func<int, StudioRelaunch.IPredecessor?> _find = StudioRelaunch.Find;

    public void Dispose()
    {
        StudioRelaunch.Find = _find;

        TempFolder.Erase(_folder, strict: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Ключи перезапуска разбираются, а проект в командной строке их не видит.</summary>
    [Fact]
    public void The_restart_keys_are_read_and_the_project_argument_skips_them()
    {
        var session = Path.Combine(_folder, "restart-17.json");
        string[] arguments = [$"--restore={session}", "--after=17"];

        Assert.Equal<(int?, string?)>((17, session), StudioRelaunch.Parse(arguments));
        Assert.Equal(ProjectArgument.None, StudioArguments.Project(arguments));
    }

    /// <summary>
    /// Преемница прежней копии — только забравшая сессию; не забравшая её никого не ждёт и не снимает.
    /// </summary>
    /// <remarks>
    /// Копия, не ответившая прежней, считала себя её преемницей: ждала папку, которую прежняя и не
    /// думала отпускать, а не дождавшись — сняла бы её, и с ней студию, в которой человек остался
    /// работать.
    /// </remarks>
    [Fact]
    public void Only_a_copy_that_took_the_session_succeeds_the_predecessor()
    {
        var taken = StudioSession.FileFor(_folder, 21);

        StudioSession.Write(new StudioSession { Studio = true }, taken);

        var (session, complaint, predecessor) = StudioRelaunch.Read([$"--restore={taken}", "--after=21"]);

        Assert.NotNull(session);
        Assert.Null(complaint);
        Assert.Equal(21, predecessor);

        var broken = Path.Combine(_folder, "restart-22.json");

        File.WriteAllText(broken, "{ не сессия");

        (session, complaint, predecessor) = StudioRelaunch.Read([$"--restore={broken}", "--after=22"]);

        Assert.Null(session);
        Assert.NotNull(complaint);
        Assert.True(
            predecessor is null,
            "копия, не забравшая сессию, сочла себя преемницей — ждала бы прежнюю и сняла бы её вместе со студией человека");
    }

    /// <summary>Без ключей перезапуска копия поднята обычно.</summary>
    [Fact]
    public void Without_the_keys_nothing_is_taken() =>
        Assert.Equal<(StudioSession?, string?, int?)>((null, null, null), StudioRelaunch.Read(["Hello.slnx"]));

    /// <summary>Номер, который не номер, не превращается в чужой процесс.</summary>
    [Theory]
    [InlineData("--after=")]
    [InlineData("--after=-5")]
    [InlineData("--after=0")]
    [InlineData("--after=12abc")]
    public void A_broken_process_number_is_not_read(string argument) =>
        Assert.Null(StudioRelaunch.Parse([argument]).After);

    /// <summary>Своим исполняемым файлом новая копия поднимается с одними ключами.</summary>
    [Fact]
    public void An_apphost_is_restarted_with_the_keys_alone()
    {
        var command = StudioRelaunch.Command(
            @"C:\Данные с пробелом\restart-9.json", 9, @"C:\Studio\ArxisStudio.exe", @"C:\Studio\ArxisStudio.dll");

        Assert.Equal(@"C:\Studio\ArxisStudio.exe", command.FileName);
        Assert.False(command.UseShellExecute, "перезапуск через оболочку не наследует окружение студии");
        Assert.Equal([@"--restore=C:\Данные с пробелом\restart-9.json", "--after=9"], command.ArgumentList);
    }

    /// <summary>Под хозяином <c>dotnet</c> первым аргументом идёт сама студия.</summary>
    [Fact]
    public void Under_the_dotnet_host_the_studio_goes_first()
    {
        var command = StudioRelaunch.Command(
            @"C:\Data\restart-9.json", 9, @"C:\Program Files\dotnet\dotnet.exe", @"C:\Studio\ArxisStudio.dll");

        Assert.Equal([@"C:\Studio\ArxisStudio.dll", @"--restore=C:\Data\restart-9.json", "--after=9"], command.ArgumentList);
    }

    /// <summary>Забранная сессия — ответ: файла больше нет.</summary>
    [Fact]
    public async Task A_taken_session_is_the_answer()
    {
        var file = Path.Combine(_folder, "restart-1.json");

        File.WriteAllText(file, "{}");

        var taking = Task.Run(async () =>
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            File.Delete(file);
        }, TestContext.Current.CancellationToken);

        Assert.True(await StudioRelaunch.AwaitTakenAsync(file, () => false, TimeSpan.FromSeconds(5)));

        await taking;
    }

    /// <summary>Умершая новая копия ответом не считается, и ждать её до конца срока незачем.</summary>
    [Fact]
    public async Task A_successor_that_died_is_no_answer()
    {
        var file = Path.Combine(_folder, "restart-2.json");

        File.WriteAllText(file, "{}");

        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(await StudioRelaunch.AwaitTakenAsync(file, () => true, TimeSpan.FromSeconds(30)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "умершую копию ждали до конца срока");
    }

    /// <summary>Не ответила за срок — ответа нет.</summary>
    [Fact]
    public async Task No_answer_in_time_is_no_answer()
    {
        var file = Path.Combine(_folder, "restart-3.json");

        File.WriteAllText(file, "{}");

        Assert.False(await StudioRelaunch.AwaitTakenAsync(file, () => false, TimeSpan.FromMilliseconds(200)));
        Assert.True(File.Exists(file));
    }

    /// <summary>Прежней копии уже нет — ждать и снимать некого.</summary>
    [Fact]
    public void A_predecessor_already_gone_is_not_waited_for()
    {
        StudioRelaunch.Find = _ => null;

        Assert.True(StudioRelaunch.AwaitExit(1, TimeSpan.FromSeconds(1)));
    }

    /// <summary>Прежняя вышла сама, пока её ждали, — снимать её не за что.</summary>
    [Fact]
    public void A_predecessor_that_leaves_in_time_is_not_killed()
    {
        var predecessor = new Predecessor(exits: true);

        StudioRelaunch.Find = _ => predecessor;

        Assert.True(StudioRelaunch.AwaitExit(1, TimeSpan.FromSeconds(1)));
        Assert.False(predecessor.Killed, "прежнюю копию сняли, хотя она ушла сама");
    }

    /// <summary>Зависшая прежняя снимается: сессия уже у новой копии.</summary>
    [Fact]
    public void A_hung_predecessor_is_killed()
    {
        var predecessor = new Predecessor(exits: false);

        StudioRelaunch.Find = _ => predecessor;

        Assert.True(StudioRelaunch.AwaitExit(1, TimeSpan.FromMilliseconds(10)));
        Assert.True(predecessor.Killed, "зависшую прежнюю копию не сняли");
        Assert.True(predecessor.Disposed);
    }

    /// <summary>Прежний процесс в руках теста: уходит сам или ждёт, пока его снимут.</summary>
    private sealed class Predecessor(bool exits) : StudioRelaunch.IPredecessor
    {
        public bool Killed { get; private set; }

        public bool Disposed { get; private set; }

        public bool WaitForExit(TimeSpan timeout) => exits || Killed;

        public void Kill() => Killed = true;

        public void Dispose() => Disposed = true;
    }
}
