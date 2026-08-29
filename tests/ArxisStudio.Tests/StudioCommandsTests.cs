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
}
