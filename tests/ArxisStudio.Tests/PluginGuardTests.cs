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
    /// <summary>Падение плагина остаётся внутри шва и приписывается ему.</summary>
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
