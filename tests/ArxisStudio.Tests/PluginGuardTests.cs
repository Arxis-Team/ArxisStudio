using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Шов вызовов плагина: студия зовёт чужой код только через него.
/// </summary>
/// <remarks>
/// План говорит об этом так: исключение логируется с атрибуцией к плагину,
/// повторные сбои помечают плагин неисправным и отключают его. Проверяется
/// здесь ровно это — и то, чего в плане нет, но без чего оно не работает:
/// отключённый плагин перестаёт получать вызовы вовсе.
/// </remarks>
public class PluginGuardTests
{
    /// <summary>Падение плагина остаётся внутри шва и приписывается ему — его же словами.</summary>
    /// <remarks>
    /// Команду из атрибута и конструктор вклада студия зовёт отражением, и снаружи остаётся
    /// «Exception has been thrown by the target of an invocation». Человеку нужно то, что бросил сам
    /// плагин.
    /// </remarks>
    [Fact]
    public void A_failing_call_is_caught_and_attributed()
    {
        var guard = new PluginGuard();
        var failures = new List<PluginFailure>();

        guard.Failed += (_, failure) => failures.Add(failure);

        var ok = guard.Run("arxis.demo", "команда demo.run", () => throw new InvalidOperationException("сломалось"));

        Assert.False(ok);
        Assert.Single(failures);
        Assert.Equal("arxis.demo", failures[0].PluginId);
        Assert.Equal("команда demo.run", failures[0].What);
        Assert.Equal("сломалось", failures[0].Message);

        guard.Run("arxis.demo", "команда demo.reflected", () =>
            throw new System.Reflection.TargetInvocationException(new InvalidOperationException("сломалось внутри")));

        Assert.Equal("сломалось внутри", failures[1].Message);
    }

    /// <summary>Ответ плагина доходит до студии, а счёт падений остаётся нулевым.</summary>
    [Fact]
    public void A_call_that_works_returns_what_the_plugin_built()
    {
        var guard = new PluginGuard();

        Assert.Equal("панель", guard.Get("arxis.demo", "панель demo", () => "панель"));
        Assert.False(guard.IsFaulty("arxis.demo"));
    }

    /// <summary>
    /// Пустой ответ — не падение.
    /// </summary>
    /// <remarks>
    /// Рисовальщик, который за эту строку не берётся, отвечает <c>null</c>, и
    /// считать это сбоем значило бы отключить плагин за то, что он честно
    /// сказал «не моё».
    /// </remarks>
    [Fact]
    public void Nothing_is_a_valid_answer()
    {
        var guard = new PluginGuard();
        var failures = 0;

        guard.Failed += (_, _) => failures++;

        Assert.True(guard.Get<string>("arxis.demo", "рисовальщик", () => null, out var result));
        Assert.Null(result);
        Assert.Equal(0, failures);
    }

    /// <summary>Третье падение подряд отключает плагин.</summary>
    [Fact]
    public void Three_failures_in_a_row_disable_the_plugin()
    {
        var guard = new PluginGuard();
        var disabled = new List<PluginFailure>();

        guard.Disabled += (_, failure) => disabled.Add(failure);

        for (var attempt = 0; attempt < PluginGuard.FailureLimit; attempt++)
            guard.Run("arxis.demo", "панель demo", () => throw new InvalidOperationException("опять"));

        Assert.Single(disabled);
        Assert.Equal(PluginGuard.FailureLimit, disabled[0].Count);
        Assert.True(guard.IsFaulty("arxis.demo"));
        Assert.Equal(["arxis.demo"], guard.Faulty);
    }

    /// <summary>
    /// Отключённый плагин больше не зовётся.
    /// </summary>
    /// <remarks>
    /// Это и есть отключение: пометить плагин и продолжать его звать значило бы
    /// показывать человеку ту же ошибку до конца сеанса.
    /// </remarks>
    [Fact]
    public void A_disabled_plugin_is_not_called_at_all()
    {
        var guard = new PluginGuard();
        var calls = 0;

        for (var attempt = 0; attempt < PluginGuard.FailureLimit; attempt++)
            guard.Run("arxis.demo", "панель demo", Break);

        var before = calls;

        Assert.False(guard.Run("arxis.demo", "панель demo", Break));
        Assert.Equal(before, calls);

        void Break()
        {
            calls++;
            throw new InvalidOperationException("опять");
        }
    }

    /// <summary>Сбои одного плагина не считаются другому.</summary>
    [Fact]
    public void Plugins_answer_for_themselves()
    {
        var guard = new PluginGuard();

        for (var attempt = 0; attempt < PluginGuard.FailureLimit; attempt++)
            guard.Run("arxis.broken", "панель", () => throw new InvalidOperationException("опять"));

        Assert.True(guard.IsFaulty("arxis.broken"));
        Assert.False(guard.IsFaulty("arxis.fine"));
        Assert.True(guard.Run("arxis.fine", "панель", () => { }));
    }

    /// <summary>Перезагрузка возвращает плагин в строй.</summary>
    [Fact]
    public void Reloading_forgets_what_the_old_copy_did()
    {
        var guard = new PluginGuard();

        for (var attempt = 0; attempt < PluginGuard.FailureLimit; attempt++)
            guard.Run("arxis.demo", "панель", () => throw new InvalidOperationException("опять"));

        guard.Forget("arxis.demo");

        Assert.False(guard.IsFaulty("arxis.demo"));
        Assert.True(guard.Run("arxis.demo", "панель", () => { }));
    }

    /// <summary>
    /// Прошедший вызов рвёт цепочку сбоев.
    /// </summary>
    /// <remarks>
    /// Счёт — про сбои подряд, так он назван везде: в описании шва, в пределе и в записи журнала об
    /// отключении. Пока он копился за весь сеанс, плагин с редкими сбоями отключался «после трёх
    /// подряд», которых подряд не было.
    /// </remarks>
    [Fact]
    public void A_call_that_works_breaks_the_row_of_failures()
    {
        var guard = new PluginGuard();

        for (var round = 0; round < 5; round++)
        {
            for (var attempt = 0; attempt < PluginGuard.FailureLimit - 1; attempt++)
                guard.Run("arxis.demo", "команда", () => throw new InvalidOperationException("иногда"));

            Assert.True(guard.Run("arxis.demo", "команда", () => { }), "между сбоями плагин обязан работать");
        }

        Assert.False(guard.IsFaulty("arxis.demo"), "сбои не шли подряд — отключать не за что");

        for (var attempt = 0; attempt < PluginGuard.FailureLimit; attempt++)
            guard.Run("arxis.demo", "команда", () => throw new InvalidOperationException("опять"));

        Assert.True(guard.IsFaulty("arxis.demo"), "три сбоя подряд обязаны отключать по-прежнему");
    }

    /// <summary>
    /// Отключают один раз, сколько бы докладов ни пришло следом.
    /// </summary>
    /// <remarks>
    /// Доклады о сбоях уже отключённого продолжают приходить: вторая панель того же плагина падает
    /// на своём замере раньше, чем её снимут. Каждый повторный сигнал заводил ещё одну выгрузку
    /// уже выгружаемого и запись «отключён после 4 сбоев подряд».
    /// </remarks>
    [Fact]
    public void A_plugin_is_disabled_once_however_many_reports_follow()
    {
        var guard = new PluginGuard();
        var failed = 0;
        var disabled = 0;

        guard.Failed += (_, _) => failed++;
        guard.Disabled += (_, _) => disabled++;

        for (var report = 0; report < PluginGuard.FailureLimit + 2; report++)
            guard.Report("arxis.demo", "раскладка панели", new InvalidOperationException("опять"));

        Assert.Equal(PluginGuard.FailureLimit + 2, failed);
        Assert.Equal(1, disabled);
    }

    /// <summary>
    /// Прощание доходит и до отключённого.
    /// </summary>
    /// <remarks>
    /// Отключают плагин затем, чтобы выгрузить, а выгрузке нужно, чтобы панель отпустила своё.
    /// Рабочая дорога шва отключённому отказывает — и как раз у него <c>Release</c> не звался.
    /// </remarks>
    [Fact]
    public void A_farewell_reaches_even_a_disabled_plugin()
    {
        var guard = new PluginGuard();

        for (var attempt = 0; attempt < PluginGuard.FailureLimit; attempt++)
            guard.Run("arxis.demo", "панель", () => throw new InvalidOperationException("опять"));

        var released = false;

        Assert.False(guard.Run("arxis.demo", "панель", () => released = true));
        Assert.False(released, "рабочая дорога отключённого не зовёт");

        Assert.True(guard.Farewell("arxis.demo", "прощание панели", () => released = true));
        Assert.True(released, "прощание обязано дойти до отключённого");
    }

    /// <summary>Падение на прощании пишется, но не считается: плагин уходит.</summary>
    [Fact]
    public void A_farewell_that_fails_is_written_and_not_counted()
    {
        var guard = new PluginGuard();
        var failures = new List<PluginFailure>();

        guard.Failed += (_, failure) => failures.Add(failure);

        for (var attempt = 0; attempt < PluginGuard.FailureLimit + 1; attempt++)
            Assert.False(guard.Farewell("arxis.demo", "прощание панели", () => throw new InvalidOperationException("и тут")));

        Assert.Equal(PluginGuard.FailureLimit + 1, failures.Count);
        Assert.All(failures, failure => Assert.Equal("прощание панели", failure.What));
        Assert.False(guard.IsFaulty("arxis.demo"), "сбой на прощании не отключает");
    }

    /// <summary>
    /// Доклады с чужих потоков не теряются и не ломают счёт.
    /// </summary>
    /// <remarks>
    /// Исключение забытой задачи поднимает поток финализатора, продолжение фоновой задачи плагина —
    /// поток пула. Словарь без замка в такой гонке терял сбои или падал сам.
    /// </remarks>
    [Fact]
    public void Reports_from_other_threads_are_all_counted()
    {
        const int Plugins = 512;

        var guard = new PluginGuard();
        var disabled = 0;

        guard.Disabled += (_, _) => Interlocked.Increment(ref disabled);

        Parallel.For(0, Plugins * PluginGuard.FailureLimit, new ParallelOptions { MaxDegreeOfParallelism = 8 }, step =>
        {
            guard.Report($"arxis.p{step % Plugins}", "забытая задача", new InvalidOperationException("фон"));
            _ = guard.IsFaulty($"arxis.p{(step + 1) % Plugins}");
        });

        Assert.Equal(Plugins, disabled);
        Assert.Equal(Plugins, guard.Faulty.Count);
    }

    /// <summary>
    /// Асинхронный вызов ловится и до первого ожидания, и после.
    /// </summary>
    /// <remarks>
    /// Открытие файла у редактора асинхронное, и упасть оно может в любой половине. Пойманная одна
    /// означала бы, что вторая идёт мимо счёта.
    /// </remarks>
    [Fact]
    public async Task An_asynchronous_call_is_caught_on_either_side_of_its_wait()
    {
        var guard = new PluginGuard();
        var failures = new List<PluginFailure>();

        guard.Failed += (_, failure) => failures.Add(failure);

        Assert.False(await guard.RunAsync("arxis.demo", "открытие", () => throw new InvalidOperationException("сразу")));

        Assert.False(await guard.RunAsync("arxis.demo", "открытие", async () =>
        {
            await Task.Yield();

            throw new InvalidOperationException("после ожидания");
        }));

        Assert.Equal(["сразу", "после ожидания"], failures.Select(failure => failure.Message));
        Assert.True(await guard.RunAsync("arxis.demo", "открытие", () => Task.CompletedTask));

        // Удачный вызов оборвал цепочку: два сбоя выше в счёт следующих не идут.
        for (var attempt = 0; attempt < PluginGuard.FailureLimit - 1; attempt++)
            await guard.RunAsync("arxis.demo", "открытие", () => throw new InvalidOperationException("опять"));

        Assert.False(guard.IsFaulty("arxis.demo"));
    }

    /// <summary>Асинхронное прощание доходит до отключённого, а его сбой не считается.</summary>
    [Fact]
    public async Task An_asynchronous_farewell_reaches_a_disabled_plugin()
    {
        var guard = new PluginGuard();

        for (var attempt = 0; attempt < PluginGuard.FailureLimit; attempt++)
            guard.Run("arxis.demo", "панель", () => throw new InvalidOperationException("опять"));

        var released = false;

        Assert.False(await guard.RunAsync("arxis.demo", "закрытие", () => { released = true; return Task.CompletedTask; }));
        Assert.False(released, "рабочая дорога отключённого не зовёт");

        Assert.True(await guard.FarewellAsync("arxis.demo", "закрытие", () => { released = true; return Task.CompletedTask; }));
        Assert.True(released);

        Assert.False(await guard.FarewellAsync("arxis.demo", "закрытие", () => throw new InvalidOperationException("и тут")));
    }

    /// <summary>
    /// Отказ процесса шов не перехватывает.
    /// </summary>
    /// <remarks>
    /// Нехватка памяти — не сбой плагина, и продолжать после неё студия всё
    /// равно не сможет: притвориться, что обошлось, хуже честного падения.
    /// </remarks>
    [Fact]
    public void A_process_level_failure_goes_through()
    {
        var guard = new PluginGuard();

        Assert.Throws<OutOfMemoryException>(
            () => guard.Run("arxis.demo", "панель", () => throw new OutOfMemoryException()));
    }

    /// <summary>
    /// Упавшая команда плагина не выходит за пределы вызова.
    /// </summary>
    /// <remarks>
    /// Обработчик заявляет плагин, а зовёт его студия — из меню или из другой
    /// команды. Хозяин запоминается при заявке: по стеку упавшего обработчика
    /// плагина уже не назвать.
    /// </remarks>
    [Fact]
    public void A_command_that_throws_is_charged_to_its_plugin()
    {
        var guard = new PluginGuard();
        var failures = new List<PluginFailure>();
        var commands = new StudioCommands(guard);

        guard.Failed += (_, failure) => failures.Add(failure);

        new PluginCommands(commands, "arxis.demo")
            .Register("demo.run", () => throw new InvalidOperationException("сломалось"));

        Assert.False(commands.Invoke("demo.run"));
        Assert.Single(failures);
        Assert.Equal("arxis.demo", failures[0].PluginId);
        Assert.Contains("demo.run", failures[0].What);
    }

    /// <summary>
    /// Команда самой студии идёт мимо шва.
    /// </summary>
    /// <remarks>
    /// Своей ошибке студия хозяина не назначит, и глушить её значило бы прятать
    /// свой же дефект под видом чужого.
    /// </remarks>
    [Fact]
    public void The_studio_own_command_is_not_guarded()
    {
        var commands = new StudioCommands(new PluginGuard());

        commands.Register("studio.run", () => throw new InvalidOperationException("свой дефект"));

        Assert.Throws<InvalidOperationException>(() => commands.Invoke("studio.run"));
    }
}
