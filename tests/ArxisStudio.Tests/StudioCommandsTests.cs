using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Реестр команд: пробуждение спящего хозяина и снятие по владельцу.
/// </summary>
/// <remarks>
/// До сих пор реестр проверялся косвенно — через подъём плагинов. Здесь его
/// собственные правила: они маленькие, но на них стоят и меню, и вызовы
/// команд из чужого кода.
/// </remarks>
public class StudioCommandsTests
{
    /// <summary>
    /// Невзятая команда будит — один раз.
    /// </summary>
    /// <remarks>
    /// Хозяин команды может ждать своего <c>onCommand:</c>: без будильника
    /// вызов из кода плагина возвращал бы false, а из меню — работал. Дорога
    /// обязана быть одна.
    /// </remarks>
    [Fact]
    public void Invoking_an_unhandled_command_calls_the_awakener_once()
    {
        var commands = new StudioCommands();
        var calls = new List<string>();

        commands.Awaken = calls.Add;

        Assert.False(commands.Invoke("ghost.run"));
        Assert.Equal(["ghost.run"], calls);
    }

    /// <summary>Обработчик, заявленный будильником, срабатывает в том же вызове.</summary>
    [Fact]
    public void A_handler_registered_by_the_awakener_runs_in_the_same_invoke()
    {
        var commands = new StudioCommands();
        var ran = false;

        commands.Awaken = id => commands.Register(id, () => ran = true);

        Assert.True(commands.Invoke("late.run"));
        Assert.True(ran);
    }

    /// <summary>Будильник, никого не разбудивший, оставляет честный отказ.</summary>
    [Fact]
    public void An_awakener_that_registers_nothing_still_returns_false()
    {
        var commands = new StudioCommands { Awaken = _ => { } };

        Assert.False(commands.Invoke("ghost.run"));
    }

    /// <summary>Без будильника поведение прежнее.</summary>
    [Fact]
    public void Invoke_without_an_awakener_behaves_as_before()
    {
        var commands = new StudioCommands();
        var ran = false;

        commands.Register("real.run", () => ran = true);

        Assert.True(commands.Invoke("real.run"));
        Assert.True(ran);
        Assert.False(commands.Invoke("ghost.run"));
    }

    /// <summary>
    /// Снятие по владельцу снимает его целиком и не трогает чужих.
    /// </summary>
    /// <remarks>
    /// По владельцу, а не по манифесту: манифест при перезагрузке уже свежий,
    /// и команда, убранная новой версией, оставалась бы висеть с обработчиком
    /// из выгруженного контекста.
    /// </remarks>
    [Fact]
    public void Commands_are_removed_by_owner_not_by_manifest()
    {
        var commands = new StudioCommands();

        commands.Register("mine.one", () => { }, "arxis.mine");
        commands.Register("mine.gone", () => { }, "arxis.mine");
        commands.Register("other.run", () => { }, "arxis.other");

        commands.RemoveOwnedBy("arxis.mine");

        Assert.DoesNotContain("mine.one", commands.Registered);
        Assert.DoesNotContain("mine.gone", commands.Registered);
        Assert.Contains("other.run", commands.Registered);
    }

    /// <summary>Команды самой студии (без владельца) снятием не задеваются.</summary>
    [Fact]
    public void Studio_owned_commands_survive_owner_removal()
    {
        var commands = new StudioCommands();

        commands.Register("studio.run", () => { });
        commands.RemoveOwnedBy("arxis.mine");

        Assert.Contains("studio.run", commands.Registered);
    }

    /// <summary>
    /// Команду, заявленную соседом, второму не отдают — и говорят об этом.
    /// </summary>
    /// <remarks>
    /// Пока заявка перезаписывала молча, второй плагин получал чужую команду, а его выгрузка
    /// снимала её по владельцу целиком: до перезапуска она не находила обработчика вовсе. Правило
    /// то же, что у экспортов: занятое другому не отдают.
    /// </remarks>
    [Fact]
    public void A_command_taken_by_a_neighbour_is_not_given_to_another()
    {
        var commands = new StudioCommands();
        var conflicts = new List<string>();
        var ran = new List<string>();

        commands.Conflict += (_, message) => conflicts.Add(message);

        Assert.True(commands.Register("shared.run", () => ran.Add("первый"), "arxis.first"));
        Assert.False(commands.Register("shared.run", () => ran.Add("второй"), "arxis.second"));

        Assert.True(commands.Invoke("shared.run"));
        Assert.Equal(["первый"], ran);

        var conflict = Assert.Single(conflicts);

        Assert.Contains("arxis.second", conflict, StringComparison.Ordinal);
        Assert.Contains("arxis.first", conflict, StringComparison.Ordinal);

        // Уход проигравшего команду не уносит: записи на него нет.
        commands.RemoveOwnedBy("arxis.second");

        Assert.True(commands.Invoke("shared.run"));
    }

    /// <summary>Свою команду студия плагину не отдаёт — и той обёрткой, какой заявляет плагин.</summary>
    [Fact]
    public void The_studio_keeps_its_own_commands()
    {
        var commands = new StudioCommands();
        var ran = new List<string>();

        commands.Register("studio.palette", () => ran.Add("студия"));

        new PluginCommands(commands, "arxis.greedy").Register("studio.palette", () => ran.Add("плагин"));
        commands.RemoveOwnedBy("arxis.greedy");

        Assert.True(commands.Invoke("studio.palette"));
        Assert.Equal(["студия"], ran);
    }

    /// <summary>
    /// Студия старше принесённого: её заявка вытесняет чужую, сколько бы та ни простояла.
    /// </summary>
    /// <remarks>
    /// Правило «кто раньше заявил» зависело бы от порядка подъёма, а не от того, чья это команда:
    /// плагин, поднявшийся раньше окна, отнимал бы у студии её же «Закрыть».
    /// </remarks>
    [Fact]
    public void The_studio_takes_back_a_command_a_plugin_took_first()
    {
        var commands = new StudioCommands();
        var conflicts = new List<string>();
        var ran = new List<string>();

        commands.Conflict += (_, message) => conflicts.Add(message);

        Assert.True(commands.Register("studio.close", () => ran.Add("плагин"), "arxis.early"));
        Assert.True(commands.Register("studio.close", () => ran.Add("студия"), owner: null));

        Assert.True(commands.Invoke("studio.close"));
        Assert.Equal(["студия"], ran);
        Assert.Contains("arxis.early", Assert.Single(conflicts), StringComparison.Ordinal);

        // Уход плагина команду студии не трогает: хозяин у неё уже другой.
        commands.RemoveOwnedBy("arxis.early");

        Assert.True(commands.Invoke("studio.close"));
    }

    /// <summary>Свою команду хозяин вправе заявить заново — это обновление, а не спор.</summary>
    [Fact]
    public void An_owner_may_register_its_own_command_again()
    {
        var commands = new StudioCommands();
        var conflicts = 0;
        var ran = new List<string>();

        commands.Conflict += (_, _) => conflicts++;

        Assert.True(commands.Register("mine.run", () => ran.Add("было"), "arxis.mine"));
        Assert.True(commands.Register("mine.run", () => ran.Add("стало"), "arxis.mine"));

        commands.Invoke("mine.run");

        Assert.Equal(["стало"], ran);
        Assert.Equal(0, conflicts);
    }
}
