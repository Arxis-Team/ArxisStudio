using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Поверхность панели: сбой раскладки остаётся внутри неё.
/// </summary>
/// <remarks>
/// Avalonia считает дерево целиком и чужого исключения ни от кого не ждёт:
/// контрол, упавший на замере, роняет весь проход, а с ним окно студии — со
/// всеми открытыми документами. План поэтому и требует вставлять панель не
/// прямо в дерево, а через защитный контейнер.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginSurfaceTests
{
    /// <summary>Исправная панель проходит насквозь.</summary>
    [AvaloniaFact]
    public void A_panel_that_works_is_measured_as_itself()
    {
        var surface = new PluginSurface(new Border { Width = 120, Height = 40 });
        var window = Shown(surface);

        // Спрашиваем размер у панели, а не у поверхности: поверхность стоит
        // содержимым окна и растянута по нему.
        Assert.False(surface.IsBroken);
        Assert.Equal(120, surface.Child!.Bounds.Width);
        Assert.Equal(40, surface.Child.Bounds.Height);

        window.Close();
    }

    /// <summary>Упавшая на замере панель не роняет окно и сообщает о себе.</summary>
    [AvaloniaFact]
    public void A_panel_that_throws_while_measuring_is_caught()
    {
        Exception? reported = null;
        var surface = new PluginSurface(new BrokenPanel(), error => reported = error);
        var window = Shown(surface);

        Assert.True(surface.IsBroken);
        Assert.IsType<InvalidOperationException>(reported);
        Assert.Equal("панель сломана", reported!.Message);

        window.Close();
    }

    /// <summary>
    /// Вместо упавшей панели встаёт заглушка со словом о случившемся.
    /// </summary>
    /// <remarks>
    /// Пустое место сказало бы человеку только то, что панель исчезла, — и
    /// молчание об этом ничем не лучше падения студии.
    /// </remarks>
    [AvaloniaFact]
    public void The_stub_says_what_happened_and_offers_a_restart()
    {
        var surface = new PluginSurface(new BrokenPanel(), reload: () => { });
        var window = Shown(surface);

        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var texts = surface.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();

        // Сверяемся со словарём, а не с русским словом: язык студии
        // переключается, а правило «заглушка говорит, что случилось» от языка
        // не зависит.
        Assert.Contains(texts, text => text == Localizer.Instance["panel.crashed"]);
        Assert.Contains("панель сломана", texts);
        Assert.Single(surface.GetVisualDescendants().OfType<Button>());

        window.Close();
    }

    /// <summary>Перезапуск ставит на место построенную заново панель.</summary>
    [AvaloniaFact]
    public void A_restart_puts_a_fresh_panel_back()
    {
        PluginSurface? surface = null;

        surface = new PluginSurface(
            new BrokenPanel(),
            reload: () => surface!.Reset(new Border { Width = 80, Height = 20 }));

        var window = Shown(surface);

        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var restart = surface.GetVisualDescendants().OfType<Button>().Single();

        restart.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.False(surface.IsBroken);
        Assert.Equal(80, surface.Child!.Bounds.Width);

        window.Close();
    }

    private static Window Shown(Control content)
    {
        var window = new Window { Content = content, Width = 300, Height = 200 };

        window.Show();
        window.UpdateLayout();

        return window;
    }

    /// <summary>Панель, падающая на каждом замере.</summary>
    private sealed class BrokenPanel : Control
    {
        protected override Size MeasureOverride(Size availableSize) =>
            throw new InvalidOperationException("панель сломана");
    }
}
