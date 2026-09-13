using ArxisStudio.Services;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Сочетания клавиш студии.
/// </summary>
/// <remarks>
/// До этой работы во всём коде студии не было ни одного жеста: ни
/// <c>KeyBinding</c>, ни <c>KeyGesture</c>, ни <c>HotKeyManager</c>. Закрыть
/// вкладку, перейти в соседнюю панель, вызвать команду можно было только мышью.
/// <para>
/// Реестр ничего не исполняет сам: он переводит нажатие в имя команды и зовёт
/// реестр команд. Так клавиша и пункт меню делают ровно одно и то же —
/// разойтись им нечем.
/// </para>
/// </remarks>
public class StudioShortcutsTests
{
    /// <summary>Сочетание зовёт ту команду, которой его отдали.</summary>
    [AvaloniaFact]
    public void A_gesture_calls_the_command_it_was_given()
    {
        var called = new List<string>();
        var keys = new StudioShortcuts(Calls(called));
        var window = Shown(keys);

        Assert.True(keys.Bind("Ctrl+W", "studio.close"));

        window.KeyPress(Key.W, RawInputModifiers.Control, PhysicalKey.W, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["studio.close"], called);

        window.Close();
    }

    /// <summary>
    /// Занятое сочетание второму не достаётся, и отказ не молчит.
    /// </summary>
    /// <remarks>
    /// Отнять сочетание у того, кто пришёл первым, значило бы менять поведение
    /// студии от порядка загрузки плагинов. Проигравший при этом обязан узнать
    /// имя победителя: иначе «моё сочетание не работает» не имеет ответа.
    /// </remarks>
    [AvaloniaFact]
    public void A_taken_gesture_does_not_go_to_the_second_asker()
    {
        var called = new List<string>();
        var keys = new StudioShortcuts(Calls(called));
        var window = Shown(keys);

        Assert.True(keys.Bind("Ctrl+W", "studio.close"));
        Assert.False(keys.Bind("Ctrl+W", "hello.greet"), "сочетание отняли у того, кто пришёл первым");

        var refused = Assert.Single(keys.Refused);

        Assert.Equal("hello.greet", refused.CommandId);
        Assert.Equal("studio.close", refused.Winner);

        window.KeyPress(Key.W, RawInputModifiers.Control, PhysicalKey.W, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["studio.close"], called);

        window.Close();
    }

    /// <summary>
    /// Сочетание команды, которой нет, клавишу не съедает.
    /// </summary>
    /// <remarks>
    /// Плагин объявляет жест манифестом, а поднимается позже — или не
    /// поднимается вовсе. Съеденная им клавиша пропала бы для всех, и причину
    /// человек не нашёл бы никогда.
    /// </remarks>
    [AvaloniaFact]
    public void A_gesture_for_a_command_that_is_not_there_does_not_eat_the_key()
    {
        var keys = new StudioShortcuts(_ => false);
        var window = Shown(keys);
        var handled = (bool?)null;

        // Обработчик встаёт после реестрового — тот подписался в Attach, — и
        // потому видит его решение, а не своё. Спрашивать надо именно флаг:
        // счётчик нажатий с handledEventsToo считал бы одинаково в обоих
        // случаях и не проверял бы ничего. Проверено поломкой.
        window.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) => handled = e.Handled,
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        Assert.True(keys.Bind("Ctrl+W", "нет.такой"));

        window.KeyPress(Key.W, RawInputModifiers.Control, PhysicalKey.W, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.False(handled, "сочетание несуществующей команды съело клавишу");

        window.Close();
    }

    /// <summary>
    /// Клавишу, обработанную кем-то другим, реестр не отнимает.
    /// </summary>
    /// <remarks>
    /// Обработчик всплывающий нарочно. В терминале <c>Ctrl+W</c> стирает слово,
    /// и студия не вправе закрывать вкладку вместо этого: клавиша принадлежит
    /// тому, кто её обработал, а реестру достаётся невостребованное.
    /// </remarks>
    [AvaloniaFact]
    public void A_key_someone_else_handled_is_not_taken()
    {
        var called = new List<string>();
        var keys = new StudioShortcuts(Calls(called));
        var greedy = new Border { Focusable = true, Height = 20 };

        greedy.KeyDown += (_, e) => e.Handled = true;

        var window = Shown(keys, greedy);

        Assert.True(keys.Bind("Ctrl+W", "studio.close"));
        Assert.True(greedy.Focus());

        window.KeyPress(Key.W, RawInputModifiers.Control, PhysicalKey.W, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(called);

        window.Close();
    }

    /// <summary>
    /// Неразобранное сочетание — отказ, но не конфликт.
    /// </summary>
    /// <remarks>
    /// Строку пишет человек, и опечатка в ней не повод не поднять плагин. В
    /// конфликтах ей тоже не место: там помнят тех, у кого сочетание отняли, а
    /// «Ctrl+Шифт» не отнимал никто.
    /// </remarks>
    [AvaloniaFact]
    public void An_unreadable_gesture_is_refused_without_a_conflict()
    {
        var keys = new StudioShortcuts(_ => true);

        Assert.False(keys.Bind("Ctrl+Шифт", "studio.close"));
        Assert.Empty(keys.All);
        Assert.Empty(keys.Refused);
    }

    /// <summary>Сочетание команды пишут рядом с её названием — в меню и в палитре.</summary>
    [AvaloniaFact]
    public void The_gesture_of_a_command_can_be_told()
    {
        var keys = new StudioShortcuts(_ => true);

        keys.Bind("Ctrl+W", "studio.close");

        Assert.Equal("Ctrl+W", keys.Gesture("studio.close"));
        Assert.Null(keys.Gesture("нет.такой"));
    }

    /// <summary>Запоминает, кого позвали.</summary>
    private static Func<string, bool> Calls(List<string> called) => id =>
    {
        called.Add(id);

        return true;
    };

    /// <summary>Окно, которое слушает клавиши этого реестра.</summary>
    private static Window Shown(StudioShortcuts keys, Control? content = null)
    {
        var window = new Window { Width = 400, Height = 300, Content = content ?? new Border() };

        keys.Attach(window);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return window;
    }
}
