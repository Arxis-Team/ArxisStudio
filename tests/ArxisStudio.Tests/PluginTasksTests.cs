using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Фоновые задачи плагина.
/// </summary>
/// <remarks>
/// Весь код плагина студия зовёт из потока интерфейса, и долгое дело, сделанное
/// там же, замораживает окно целиком. Задача — это способ уйти с этого потока и
/// остаться видимым: у неё есть имя, ход и отмена.
/// </remarks>
public class PluginTasksTests
{
    /// <summary>Работа идёт не в том потоке, из которого её позвали.</summary>
    /// <remarks>
    /// Это и есть всё содержание службы: если работа останется в потоке
    /// вызывающего, студия замрёт ровно так же, как без неё.
    /// <para>
    /// Зовут отсюда со своего потока, а не с потока теста. Тест идёт на потоке
    /// пула, и, пока он ждёт, поток возвращается в пул: при нехватке потоков
    /// пул отдавал под работу его же — номера совпадали, тест падал, а студия
    /// при этом не замирала. Свой поток пулу не принадлежит и достаться работе
    /// не может ни при какой загрузке, поэтому совпадение номеров означает
    /// ровно одно: службу позвали, а она сделала работу на месте.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_work_leaves_the_thread_it_was_started_from()
    {
        var caller = new TaskCompletionSource<int>();
        var started = new TaskCompletionSource<Task<int>>();

        var thread = new Thread(() =>
        {
            caller.SetResult(Environment.CurrentManagedThreadId);

            started.SetResult(Tasks().RunAsync("Проба", (_, _) =>
                Task.FromResult(Environment.CurrentManagedThreadId)));
        })
        {
            IsBackground = true,
        };

        thread.Start();

        var inside = await await started.Task;

        Assert.NotEqual(await caller.Task, inside);
    }

    /// <summary>Пока задача идёт, студия о ней знает; кончилась — забывает.</summary>
    [Fact]
    public async Task A_running_task_is_visible_and_a_finished_one_is_not()
    {
        var registry = new StudioTaskRegistry();
        var started = new TaskCompletionSource();
        var allowed = new TaskCompletionSource();

        var work = Tasks(registry).RunAsync("Обход проекта", async (progress, _) =>
        {
            progress.Report(0.5, "половина");
            started.SetResult();

            await allowed.Task;
        });

        await started.Task;

        var running = Assert.Single(registry.Running);

        Assert.Equal("Обход проекта", running.Title);
        Assert.Equal("половина", running.Message);
        Assert.Equal(0.5, running.Fraction);

        allowed.SetResult();
        await work;

        Assert.Empty(registry.Running);
    }

    /// <summary>
    /// Отмена приходит в работу токеном и ошибкой не считается.
    /// </summary>
    /// <remarks>
    /// Отменил человек, а не плагин ошибся: записать это в журнал ошибкой
    /// значило бы копить в нём следы нормальных действий, а посчитать сбоем —
    /// отключить плагин за то, что его попросили остановиться.
    /// </remarks>
    [Fact]
    public async Task Cancelling_reaches_the_work_and_is_not_a_failure()
    {
        var registry = new StudioTaskRegistry();
        var guard = new PluginGuard();
        var failures = 0;
        var started = new TaskCompletionSource();

        guard.Failed += (_, _) => failures++;

        var work = Tasks(registry, guard).RunAsync("Долгая работа", async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        });

        await started.Task;
        Assert.Single(registry.Running).Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);

        Assert.Empty(registry.Running);
        Assert.Equal(0, failures);
    }

    /// <summary>Сбой задачи считается плагину — тем же швом, что и сбой панели.</summary>
    [Fact]
    public async Task A_failing_task_is_charged_to_its_plugin()
    {
        var guard = new PluginGuard();
        var failures = new List<PluginFailure>();

        guard.Failed += (_, failure) => failures.Add(failure);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Tasks(guard: guard).RunAsync("Импорт", (_, _) => throw new InvalidOperationException("сломалось")));

        var failure = Assert.Single(failures);

        Assert.Equal("arxis.demo", failure.PluginId);
        Assert.Contains("Импорт", failure.What);
    }

    /// <summary>
    /// Перед выгрузкой задачи плагина останавливаются.
    /// </summary>
    /// <remarks>
    /// Работающая задача держит типы плагина, а через них его контекст
    /// загрузки: не остановив её, студия выгрузит плагин только на словах.
    /// </remarks>
    [Fact]
    public async Task Tasks_of_a_plugin_stop_before_it_unloads()
    {
        var registry = new StudioTaskRegistry();
        var started = new TaskCompletionSource();

        var work = Tasks(registry).RunAsync("Долгая работа", async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        });

        await started.Task;

        Assert.True(await registry.StopAsync("arxis.demo", TimeSpan.FromSeconds(5)), "задача не остановилась");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
    }

    /// <summary>
    /// Работа, не смотрящая в токен, не держит студию вечно.
    /// </summary>
    /// <remarks>
    /// Отмена в .NET кооперативная, и упрямую работу остановить нечем. Ждать её
    /// без предела значило бы заморозить студию насмерть при перезагрузке
    /// плагина; поэтому у ожидания есть срок, а у человека — предупреждение.
    /// </remarks>
    [Fact]
    public async Task A_stubborn_task_is_waited_for_only_so_long()
    {
        var registry = new StudioTaskRegistry();
        var started = new TaskCompletionSource();
        var allowed = new TaskCompletionSource();

        var work = Tasks(registry).RunAsync("Упрямая работа", async (_, _) =>
        {
            started.SetResult();
            await allowed.Task;
        });

        await started.Task;

        Assert.False(await registry.StopAsync("arxis.demo", TimeSpan.FromMilliseconds(200)));

        allowed.SetResult();
        await work;
    }

    private static PluginTasks Tasks(StudioTaskRegistry? registry = null, PluginGuard? guard = null) =>
        new("arxis.demo", registry ?? new StudioTaskRegistry(), guard ?? new PluginGuard(), new StudioLog());
}
