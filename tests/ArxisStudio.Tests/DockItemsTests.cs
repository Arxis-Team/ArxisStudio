using ArxisStudio.Docking;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Реестр живых панелей.
/// </summary>
public class DockItemsTests
{
    /// <summary>Панель находится по имени, а неизвестное имя даёт пусто.</summary>
    [AvaloniaFact]
    public void A_panel_is_found_by_name()
    {
        var items = new DockItems();
        var panel = new DockItem("solution", new Border());

        items.Add("hello", panel);

        Assert.Same(panel, items.Find("solution"));
        Assert.Null(items.Find("нет.такой"));
        Assert.Null(items.Find(null));
    }

    /// <summary>
    /// Уходит только то, что положил этот хозяин.
    /// </summary>
    /// <remarks>
    /// Имена снятых возвращаются не для удобства: убрать их из дерева — второе
    /// обязательное действие, и забыть его нельзя, поэтому реестр сам говорит,
    /// что именно снял.
    /// </remarks>
    [AvaloniaFact]
    public void Only_this_owners_panels_leave()
    {
        var items = new DockItems();

        items.Add("hello", new DockItem("solution", new Border()));
        items.Add("hello", new DockItem("structure", new Border()));
        items.Add("friend", new DockItem("console", new Border()));

        var gone = items.RemoveOwnedBy("hello");

        Assert.Equal(["solution", "structure"], gone.Order());
        Assert.Equal(1, items.Count);
        Assert.NotNull(items.Find("console"));
        Assert.Null(items.Find("solution"));

        // Во второй раз тому же хозяину возвращать нечего: запись о нём ушла
        // вместе с панелью. Иначе список снятых лгал бы, а студия убирала бы из
        // дерева панели, которых там давно нет.
        Assert.Empty(items.RemoveOwnedBy("hello"));
    }

    /// <summary>Панель под тем же именем вытесняет прежнюю вместе с её хозяином.</summary>
    /// <remarks>
    /// Так бывает при перезагрузке плагина: панель поднимается заново, а имя у
    /// неё то же. Оставить прежнюю значило бы держать контрол из выгруженного
    /// контекста; оставить прежнего хозяина — снять новую панель по чужому
    /// сигналу.
    /// </remarks>
    [AvaloniaFact]
    public void A_panel_under_the_same_name_pushes_out_the_previous_one()
    {
        var items = new DockItems();
        var fresh = new DockItem("solution", new Border());

        items.Add("hello", new DockItem("solution", new Border()));
        items.Add("friend", fresh);

        Assert.Equal(1, items.Count);
        Assert.Same(fresh, items.Find("solution"));
        Assert.Empty(items.RemoveOwnedBy("hello"));
        Assert.Same(fresh, items.Find("solution"));
    }

    /// <summary>Известные имена — это то, по чему дерево отсеивает призраков.</summary>
    [AvaloniaFact]
    public void The_known_names_are_what_the_tree_sifts_by()
    {
        var items = new DockItems();

        items.Add("hello", new DockItem("solution", new Border()));

        var root = new DockGroup { Id = "left", Items = ["solution", "ghost"], Selected = "ghost" };
        var group = Assert.IsType<DockGroup>(DockTree.Keep(root, items.Known()));

        Assert.Equal(["solution"], group.Items);
    }
}
