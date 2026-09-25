using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Свои ветки меню студии: панели и наборы раскладки.
/// </summary>
/// <remarks>
/// Ветки жили в окне студии, которое не строит ни один тест, и держались одной живой проверкой.
/// Здесь они собираются над службой расширений и раскладкой — теми же, что у окна.
/// <para>
/// Очередь общая: подписи веток берутся из словарей, а <c>Localizer</c> один на процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioMenuBranchesTests : IDisposable
{
    private readonly StudioPluginsHarness _studio = new();

    public void Dispose()
    {
        _studio.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Стоящая панель отмечена галочкой, скрытая — нет, а щелчок меняет одно на другое.</summary>
    [AvaloniaFact]
    public void The_panels_branch_marks_the_standing_and_toggles_them()
    {
        _studio.Show();
        _studio.Dock.Shown();
        _studio.Dock.Add("probe", "probe:one", new PluginPlacement { Side = "left" }, "Первая", PluginStrings.Studio, new Border());
        _studio.Dock.Add("probe", "probe:two", new PluginPlacement { Side = "right" }, "Вторая", PluginStrings.Studio, new Border());
        _studio.Dock.Hide("probe:two");

        var panels = Branch("menu.panels");
        var one = Item(panels, "Первая");
        var two = Item(panels, "Вторая");

        Assert.NotNull(one.Icon);
        Assert.Null(two.Icon);

        one.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        two.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(
            [new StudioPanel("probe:one", "Первая", false), new StudioPanel("probe:two", "Вторая", true)],
            _studio.Dock.Panels);
    }

    /// <summary>
    /// Показанный набор отмечен и не переключает сам на себя, а удалить можно только не стандартный.
    /// </summary>
    [AvaloniaFact]
    public void The_layout_branch_marks_the_shown_set_and_deletes_only_a_custom_one()
    {
        var delete = Localizer.Instance["menu.layout.delete"];

        Assert.DoesNotContain(Branch("menu.layout").Items.OfType<AxMenuItem>(), item => Equals(item.Header, delete));

        _studio.Dock.SaveAs("Отладка");

        var layouts = Branch("menu.layout");

        Assert.NotNull(Item(layouts, "Отладка").Icon);
        Assert.Null(Item(layouts, DockLayout.DefaultName).Icon);

        Item(layouts, delete).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(DockLayout.DefaultName, _studio.Dock.Layout);
        Assert.Equal([DockLayout.DefaultName], _studio.Dock.Layouts);
    }

    /// <summary>Без поднятых внешних плагинов ветки «Плагины» нет: перезагружать нечего.</summary>
    [AvaloniaFact]
    public void Without_a_raised_plugin_there_is_nothing_to_reload()
    {
        Assert.DoesNotContain(
            Branches().Build().OfType<AxMenuItem>(),
            branch => Equals(branch.Header, Localizer.Instance["menu.plugins"]));
    }

    private StudioMenuBranches Branches() =>
        new(_studio.Plugins ?? _studio.Build(), _studio.Dock)
        {
            Reload = _ => Task.CompletedTask,
            SaveLayout = () => Task.CompletedTask,
        };

    private AxMenuItem Branch(string key) =>
        Assert.Single(Branches().Build().OfType<AxMenuItem>(), branch => Equals(branch.Header, Localizer.Instance[key]));

    private static AxMenuItem Item(AxMenuItem branch, string header) =>
        Assert.Single(branch.Items.OfType<AxMenuItem>(), item => Equals(item.Header, header));
}
