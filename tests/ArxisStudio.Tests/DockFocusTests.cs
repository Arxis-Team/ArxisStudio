using ArxisStudio.Controls;
using ArxisStudio.Docking;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Фокус клавиатуры при переключении вкладок.
/// </summary>
/// <remarks>
/// На тысячу девятьсот строк движка приходилась одна строка про фокус, и та
/// запрещала его призраку перетаскивания. Переключение вкладки подменяет
/// содержимое presenter'а: сфокусированный контрол отцеплялся, фокус пропадал
/// молча, и человек, пришедший с клавиатуры, начинал обход заново от окна.
/// <para>
/// Проверяется наблюдаемое: где оказался фокус после переключения, а не какие
/// свойства при этом выставились.
/// </para>
/// </remarks>
public class DockFocusTests
{
    /// <summary>
    /// Переключение вкладки уносит фокус в показанную панель.
    /// </summary>
    /// <remarks>
    /// Главное правило вехи. Без него фокус после щелчка по вкладке не
    /// принадлежит никому: контрол, на котором он стоял, отцеплен вместе с
    /// прежним содержимым.
    /// </remarks>
    [AvaloniaFact]
    public void Choosing_a_tab_carries_the_focus_into_the_panel_shown()
    {
        var left = Panel(out var inLeft, out _);
        var right = Panel(out var inRight, out _);
        var view = Shown(left, right, out _);

        Assert.True(inLeft.Focus(), "панель слева обязана брать фокус");

        Tabs(view).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.True(DockFocus.Holds(right), "фокус не доехал до показанной панели");
        Assert.False(DockFocus.Holds(left));

        // Хранителя у правой панели ещё нет, поэтому фокус берёт первый, кто может.
        Assert.True(inRight.IsFocused);
    }

    /// <summary>
    /// Панель получает обратно того, кто держал фокус у неё.
    /// </summary>
    /// <remarks>
    /// Вернуть фокус в панель мало: человек оставил каретку в определённом
    /// месте, и вернувшаяся на первый контрол каретка — это потеря места, а не
    /// сохранение фокуса.
    /// </remarks>
    [AvaloniaFact]
    public void A_panel_gets_back_the_one_who_held_its_focus()
    {
        var left = Panel(out _, out var deepInLeft);
        var right = Panel(out _, out _);
        var view = Shown(left, right, out _);

        Assert.True(deepInLeft.Focus());

        Tabs(view).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Tabs(view).SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        Assert.True(deepInLeft.IsFocused, "вернулись не к тому, кого оставили");
    }

    /// <summary>
    /// Показ панели не крадёт фокус у того, кто его держит в другом месте.
    /// </summary>
    /// <remarks>
    /// Раскладка меняется не только руками: панель показывает проснувшийся
    /// плагин, восстановление раскладки, возврат из оторванного окна. Фокус,
    /// выдернутый из редактора чужим пробуждением, — это потерянное нажатие.
    /// </remarks>
    [AvaloniaFact]
    public void Showing_a_panel_does_not_steal_focus_from_elsewhere()
    {
        var left = Panel(out _, out _);
        var right = Panel(out _, out _);
        var view = Shown(left, right, out var outside);

        Assert.True(outside.Focus(), "контрол вне раскладки обязан брать фокус");

        Tabs(view).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.True(outside.IsFocused, "показ панели забрал фокус у того, кто его держал");
        Assert.False(DockFocus.Holds(right));
    }

    /// <summary>Полоса вкладок единственной группы.</summary>
    private static AxTabStrip Tabs(DockView view) =>
        DockMouse.Tabs(view.View("group") ?? throw new InvalidOperationException("группы нет на экране"));

    /// <summary>Панель с двумя местами, куда может встать каретка.</summary>
    private static Control Panel(out Border first, out Border second)
    {
        first = new Border { Focusable = true, Height = 20 };
        second = new Border { Focusable = true, Height = 20 };

        return new StackPanel { Children = { first, second } };
    }

    /// <summary>Одна группа с двумя вкладками и контрол вне раскладки рядом.</summary>
    private static DockView Shown(Control left, Control right, out Border outside)
    {
        var items = new DockItems();

        items.Add("hello", new DockItem("left", left) { Title = "left" });
        items.Add("hello", new DockItem("right", right) { Title = "right" });

        var view = new DockView
        {
            Items = items,
            Root = new DockGroup { Id = "group", Items = ["left", "right"], Selected = "left" },
            Height = 400,
        };

        outside = new Border { Focusable = true, Height = 20 };

        var window = new Window
        {
            Content = new StackPanel { Children = { view, outside } },
            Width = 900,
            Height = 600,
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return view;
    }
}
