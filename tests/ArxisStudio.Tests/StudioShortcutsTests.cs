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

    /// <summary>
    /// Сочетания выключенного расширения уходят вместе с ним.
    /// </summary>
    /// <remarks>
    /// Сочетание живёт ровно столько, сколько живёт команда за ним. Оставшееся
    /// отнимало бы клавишу у всех и не делало бы ничего, а перезагруженный
    /// плагин просил бы своё же сочетание второй раз и получал отказ с
    /// сообщением, что оно занято им самим.
    /// </remarks>
    [AvaloniaFact]
    public void The_gestures_of_a_plugin_that_left_go_with_it()
    {
        var keys = new StudioShortcuts(_ => true);

        Assert.True(keys.Bind("Ctrl+W", "studio.close"));
        Assert.True(keys.Bind("Ctrl+Alt+G", "hello.greet", "arxis.hello"));
        Assert.False(keys.Bind("Ctrl+W", "hello.close", "arxis.hello"));

        Assert.Equal(2, keys.All.Count);
        Assert.Single(keys.Refused);

        keys.RemoveOwnedBy("arxis.hello");

        // Своё осталось, принесённое ушло — вместе с отказом, который был
        // ответом на вопрос, которого больше нет.
        Assert.Equal("studio.close", Assert.Single(keys.All).CommandId);
        Assert.Empty(keys.Refused);

        // И сочетание снова свободно: тот же плагин, поднявшись заново, его получит.
        Assert.True(keys.Bind("Ctrl+Alt+G", "hello.greet", "arxis.hello"));
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

    /// <summary>
    /// Сочетание, отданное человеком, достаётся ему, а студия получает отказ.
    /// </summary>
    /// <remarks>
    /// Человек старше всех: его файл раздаётся раньше студийных сочетаний и манифестов. Отказ
    /// при этом тот же, что у всех, — с именем победителя, — чтобы на вопрос «куда делось моё
    /// Ctrl+W» был ответ.
    /// </remarks>
    [AvaloniaFact]
    public void A_gesture_the_person_gave_wins_over_the_default()
    {
        var called = new List<string>();
        var keys = new StudioShortcuts(Calls(called));
        var window = Shown(keys);

        keys.Personalize(Keymap(("hello.greet", ["Ctrl+W"])));

        Assert.False(keys.Bind("Ctrl+W", "studio.close"), "студия отняла сочетание у человека");

        var refused = Assert.Single(keys.Refused);

        Assert.Equal("studio.close", refused.CommandId);
        Assert.Equal("hello.greet", refused.Winner);
        Assert.True(Assert.Single(keys.All).Personal, "сочетание человека не помечено его");

        window.KeyPress(Key.W, RawInputModifiers.Control, PhysicalKey.W, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["hello.greet"], called);

        window.Close();
    }

    /// <summary>Команда, которой человек дал другое сочетание, прежнего не держит.</summary>
    [AvaloniaFact]
    public void A_command_the_person_remapped_keeps_only_the_new_gesture()
    {
        var called = new List<string>();
        var keys = new StudioShortcuts(Calls(called));
        var window = Shown(keys);

        keys.Personalize(Keymap(("studio.close", ["Ctrl+F4"])));

        Assert.True(keys.Bind("Ctrl+W", "studio.close"), "команда, за которую решил человек, получила отказ");
        Assert.Empty(keys.Refused);
        Assert.Equal("Ctrl+F4", keys.Gesture("studio.close"));

        window.KeyPress(Key.W, RawInputModifiers.Control, PhysicalKey.W, string.Empty);
        window.KeyPress(Key.F4, RawInputModifiers.Control, PhysicalKey.F4, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["studio.close"], called);

        window.Close();
    }

    /// <summary>Команда, у которой человек сочетание снял, не зовётся ничем.</summary>
    [AvaloniaFact]
    public void A_command_the_person_left_without_a_gesture_has_none()
    {
        var called = new List<string>();
        var keys = new StudioShortcuts(Calls(called));
        var window = Shown(keys);

        keys.Personalize(Keymap(("studio.close", [])));

        Assert.True(keys.Bind("Ctrl+W", "studio.close"));
        Assert.Null(keys.Gesture("studio.close"));

        window.KeyPress(Key.W, RawInputModifiers.Control, PhysicalKey.W, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(called);

        window.Close();
    }

    /// <summary>Одно сочетание у двух команд в файле достаётся первой, а вторая узнаёт почему.</summary>
    [AvaloniaFact]
    public void One_gesture_given_to_two_commands_goes_to_the_first()
    {
        var keys = new StudioShortcuts(_ => true);

        keys.Personalize(Keymap(("studio.palette", ["Ctrl+K"]), ("hello.greet", ["Ctrl+K"])));

        Assert.Equal("studio.palette", Assert.Single(keys.All).CommandId);

        var refused = Assert.Single(keys.Refused);

        Assert.Equal("hello.greet", refused.CommandId);
        Assert.Equal("studio.palette", refused.Winner);
        Assert.True(refused.Personal, "отказ в файле человека не помечен его");
    }

    /// <summary>Сочетание, которое человек дал команде плагина, уходом плагина не снимается.</summary>
    /// <remarks>
    /// Сочетание принадлежит человеку, а не плагину: плагин вернётся — клавиша снова позовёт его
    /// команду, а пока его нет, она никого не зовёт и клавишу не съедает.
    /// </remarks>
    [AvaloniaFact]
    public void A_gesture_the_person_gave_a_plugin_command_stays_when_the_plugin_leaves()
    {
        var keys = new StudioShortcuts(_ => true);

        keys.Personalize(Keymap(("hello.greet", ["Ctrl+Alt+H"])));
        keys.Bind("Ctrl+Alt+G", "hello.greet", "arxis.hello");
        keys.RemoveOwnedBy("arxis.hello");

        Assert.Equal("Ctrl+Alt+H", keys.Gesture("hello.greet"));
    }

    /// <summary>Файл человека раздаётся раньше всех и один раз.</summary>
    /// <remarks>
    /// Раздай его после студии, и пересчёт задним числом отнимал бы клавишу у того, кто уже её
    /// держит, — порядок подъёма решал бы то, что решил человек.
    /// </remarks>
    [AvaloniaFact]
    public void The_person_is_heard_first_and_once()
    {
        var late = new StudioShortcuts(_ => true);

        late.Bind("Ctrl+W", "studio.close");

        Assert.Throws<InvalidOperationException>(() => late.Personalize(Keymap(("studio.close", ["Ctrl+F4"]))));

        var twice = new StudioShortcuts(_ => true);

        twice.Personalize(Keymap(("studio.close", ["Ctrl+F4"])));

        Assert.Throws<InvalidOperationException>(() => twice.Personalize(Keymap(("studio.close", ["Ctrl+F5"]))));
    }

    /// <summary>Файл человека из пар «команда — сочетания».</summary>
    private static StudioKeymap Keymap(params (string Command, string[] Gestures)[] entries) =>
        new([.. entries.Select(entry => new KeymapEntry(entry.Command, entry.Gestures))], []);

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
