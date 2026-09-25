using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Вопрос о перезапуске: когда его задают, что значит отказ и чем кончается согласие.
/// </summary>
/// <remarks>
/// Вопрос и сам перезапуск подменены: модальный диалог в безголовом прогоне ждал бы ответа вечно,
/// а настоящий перезапуск закрыл бы процесс тестов. Повод настоящий — запись в службе расширений.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioRestartTests
{
    private readonly StudioPlugins _plugins = Plugins();
    private readonly Window _owner = new();
    private readonly List<Window> _asked = [];
    private int _performed;

    /// <summary>Спрашивают только о новом: та же причина второй раз вопроса не заводит.</summary>
    [AvaloniaFact]
    public async Task The_question_is_asked_only_about_something_new()
    {
        var restart = Restart(agree: false);

        await restart.OfferAsync(_owner);
        Assert.Empty(_asked);

        _plugins.Await("probe.one", "держит Avalonia");
        await restart.OfferAsync(_owner);
        await restart.OfferAsync(_owner);

        Assert.Equal([_owner], _asked);

        _plugins.Await("probe.one", "держит Avalonia");
        await restart.OfferAsync(_owner);

        Assert.Single(_asked);

        _plugins.Await("probe.two", "контракт изменился на диске");
        await restart.OfferAsync(_owner);

        Assert.Equal(2, _asked.Count);
    }

    /// <summary>«Не сейчас» ничего не делает, но нужда в перезапуске остаётся.</summary>
    [AvaloniaFact]
    public async Task Not_now_restarts_nothing_and_the_need_stays()
    {
        var restart = Restart(agree: false);
        var changed = 0;

        restart.Changed += (_, _) => changed++;

        _plugins.Await("probe.one", "держит Avalonia");
        await restart.OfferAsync(_owner);

        Assert.Equal(0, _performed);
        Assert.True(restart.IsRequired);
        Assert.Equal(1, changed);
    }

    /// <summary>Согласие дописывает несохранённое, потом перезапускает.</summary>
    [AvaloniaFact]
    public async Task Agreeing_prepares_first_and_restarts_after()
    {
        var restart = Restart(agree: true);
        var order = new List<string>();

        restart.Perform = () =>
        {
            order.Add("перезапуск");
            return Task.FromResult(true);
        };

        _plugins.Await("probe.one", "держит Avalonia");

        await restart.OfferAsync(_owner, () =>
        {
            order.Add("сохранение");
            return Task.FromResult(true);
        });

        Assert.Equal(["сохранение", "перезапуск"], order);
    }

    /// <summary>Не записалось несохранённое — перезапуска нет.</summary>
    [AvaloniaFact]
    public async Task A_failed_preparation_calls_the_restart_off()
    {
        var restart = Restart(agree: true);

        Assert.False(await restart.RestartAsync(() => Task.FromResult(false)));
        Assert.Equal(0, _performed);
    }

    /// <summary>Идущий перезапуск второй раз не начинается.</summary>
    /// <remarks>
    /// Оба вызова отпускаются одним ответом: ожидание второго, начатого поверх первого, иначе
    /// повисло бы на том же ответе, и сломанная защита вешала бы прогон, а не роняла тест.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_restart_under_way_is_not_started_twice()
    {
        var restart = Restart(agree: true);
        var hold = new TaskCompletionSource<bool>();

        restart.Perform = () =>
        {
            _performed++;
            return hold.Task;
        };

        var first = restart.RestartAsync();
        var second = restart.RestartAsync();

        hold.SetResult(true);

        Assert.True(await first);
        Assert.False(await second, "второй перезапуск начался поверх первого");
        Assert.Equal(1, _performed);
    }

    /// <summary>
    /// Запасной шаг идёт, только если подготовка отработала, а перезапуска не будет.
    /// </summary>
    /// <remarks>
    /// Окно настроек записывает галочки плагинов, не применяя их, и применяет вживую здесь, если
    /// перезапуск не состоялся. Второму входу поверх идущего перезапуска запасной шаг не положен:
    /// применять вживую то, что первый вот-вот унесёт в новую копию, — работа поперёк него.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_fallback_runs_only_when_prepared_and_not_restarted()
    {
        var restart = Restart(agree: true);
        var fell = 0;

        restart.Perform = () => Task.FromResult(false);

        Assert.False(await restart.RestartAsync(Prepared(true), Fallback));
        Assert.Equal(1, fell);

        Assert.False(await restart.RestartAsync(Prepared(false), Fallback));
        Assert.Equal(2, fell);

        restart.Perform = () => Task.FromResult(true);

        Assert.True(await restart.RestartAsync(Prepared(true), Fallback));
        Assert.Equal(2, fell);

        var hold = new TaskCompletionSource<bool>();

        restart.Perform = () => hold.Task;

        var first = restart.RestartAsync(Prepared(true), Fallback);
        var second = restart.RestartAsync(Prepared(true), Fallback);

        hold.SetResult(false);

        Assert.False(await first);
        Assert.False(await second);
        Assert.Equal(3, fell);

        Task Fallback()
        {
            fell++;
            return Task.CompletedTask;
        }

        static Func<Task<bool>> Prepared(bool done) => () => Task.FromResult(done);
    }

    /// <summary>Перезапускать некому — и спрашивать не о чем.</summary>
    [AvaloniaFact]
    public async Task Nobody_is_asked_when_nothing_can_restart()
    {
        var restart = new StudioRestart(_plugins) { Ask = Ask(true) };

        _plugins.Await("probe.one", "держит Avalonia");
        await restart.OfferAsync(_owner);

        Assert.Empty(_asked);
        Assert.False(await restart.RestartAsync());
    }

    private StudioRestart Restart(bool agree) => new(_plugins)
    {
        Ask = Ask(agree),
        Perform = () =>
        {
            _performed++;
            return Task.FromResult(true);
        },
    };

    private Func<Window, Task<bool>> Ask(bool agree) => owner =>
    {
        _asked.Add(owner);
        return Task.FromResult(agree);
    };

    /// <summary>Служба расширений без единого расширения — поводы к перезапуску тест пишет сам.</summary>
    private static StudioPlugins Plugins()
    {
        var guard = new PluginGuard();
        var contributions = new PluginContributionRegistry();
        var dock = new StudioDock(new DockView());

        return new StudioPlugins(new StudioLog(), guard, new StudioTaskRegistry(), contributions)
        {
            Commands = new StudioCommands(guard),
            Dock = dock,
            ToolBar = new StudioToolBar(new ToolBarStrip(), new ToolBarStrip(), new ToolBarStrip()),
            Documents = new StudioDocuments(dock, contributions.EditorFor, new Silence()),
            Services = new Dictionary<Type, object>(),
            Catalog = () => [],
            Assemblies = [],
            Settings = new PluginSettingsStore(userFile: Path.Combine(Path.GetTempPath(), $"arxis-restart-{Guid.NewGuid():N}.json")),
        };
    }

    private sealed class Silence : Sdk.IStudioStatus
    {
        public void Show(string message)
        {
        }
    }
}
