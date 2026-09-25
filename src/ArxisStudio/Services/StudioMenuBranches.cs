using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Icons;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;

namespace ArxisStudio.Services;

/// <summary>
/// Собственные ветки студии в её меню: перезагрузка плагина, панели и наборы раскладки.
/// </summary>
/// <remarks>
/// Манифестами они не объявлены и объявлены быть не могут: пункты зависят от того, что сейчас
/// поднято, что стоит в раскладке и какой набор показан, — список собирается на каждом открытии
/// заново.
/// <para>
/// Жили они в окне студии, и проверить их было нечем: окно не строит ни один тест. Здесь меню
/// собирается над службой расширений и раскладкой, а что делать по щелчку, решает окно — его
/// вопросы и диалоги модальные и живут там.
/// </para>
/// </remarks>
/// <param name="plugins">Служба расширений: кого можно перезагрузить.</param>
/// <param name="dock">Раскладка: панели и наборы.</param>
internal sealed class StudioMenuBranches(StudioPlugins plugins, StudioDock dock)
{
    /// <summary>Перезагружает плагин по его пункту; аргумент — идентификатор.</summary>
    public required Func<string, Task> Reload { get; init; }

    /// <summary>Спрашивает имя и сохраняет под ним показанный набор.</summary>
    public required Func<Task> SaveLayout { get; init; }

    /// <summary>Собирает ветки в порядке меню: плагины, панели, раскладка.</summary>
    public IReadOnlyList<MenuItem> Build()
    {
        var branches = new List<MenuItem>();

        if (plugins.Reloadable is { Count: > 0 } reloadable)
            branches.Add(Plugins(reloadable));

        if (dock.Panels is { Count: > 0 } panels)
            branches.Add(Panels(panels));

        branches.Add(Layouts());

        return branches;
    }

    /// <summary>
    /// Ветка «Плагины»: перезагрузка каждого поднятого внешнего.
    /// </summary>
    /// <remarks>
    /// Не довёл перезагрузку до конца — прежняя копия осталась в памяти, контракт пересобран —
    /// плагин ждёт перезапуска, и спрашивает о нём тот, кто перезагружает: человек, нажавший
    /// «перезагрузить», ждёт ответа там, где нажимал.
    /// </remarks>
    private AxMenuItem Plugins(IReadOnlyList<Extensibility.InstalledPlugin> reloadable)
    {
        var branch = new AxMenuItem { Header = Localizer.Instance["menu.plugins"] };

        foreach (var plugin in reloadable)
        {
            var item = new AxMenuItem
            {
                Header = $"{Localizer.Instance["menu.reload"]} · {plugin.DisplayName}",
            };

            var id = plugin.Id;

            item.Click += async (_, _) => await Reload(id);

            branch.Items.Add(item);
        }

        return branch;
    }

    /// <summary>
    /// Ветка «Панели» — единственная дорога назад для скрытой панели.
    /// </summary>
    /// <remarks>
    /// Имя скрытой в дереве осталось, но на экране её нет, и попросить за неё некому, кроме
    /// человека. Галочка у стоящей — тем же способом, что и у показанного набора.
    /// </remarks>
    private AxMenuItem Panels(IReadOnlyList<StudioPanel> panels)
    {
        var branch = new AxMenuItem { Header = Localizer.Instance["menu.panels"] };

        foreach (var panel in panels)
        {
            var item = new AxMenuItem { Header = panel.Title };

            if (panel.Standing)
                item.Icon = Check();

            var id = panel.Id;
            var standing = panel.Standing;

            item.Click += (_, _) =>
            {
                if (standing)
                    dock.Hide(id);
                else
                    dock.Reopen(id);
            };

            branch.Items.Add(item);
        }

        return branch;
    }

    /// <summary>Ветка «Раскладка»: наборы, сохранение, сброс и удаление показанного.</summary>
    private AxMenuItem Layouts()
    {
        var branch = new AxMenuItem { Header = Localizer.Instance["menu.layout"] };

        foreach (var name in dock.Layouts)
        {
            var set = new AxMenuItem { Header = name };

            // Показанный набор помечен галочкой в колонке значков, которую
            // тема держит у каждого пункта: переключаться на самого себя
            // человеку незачем, поэтому щелчка у него и нет.
            if (string.Equals(name, dock.Layout, StringComparison.Ordinal))
            {
                set.Icon = Check();
            }
            else
            {
                var chosen = name;

                set.Click += (_, _) => dock.Switch(chosen);
            }

            branch.Items.Add(set);
        }

        branch.Items.Add(new Separator());

        var save = new AxMenuItem { Header = Localizer.Instance["menu.layout.save"] };
        var reset = new AxMenuItem { Header = Localizer.Instance["menu.layout.reset"] };

        save.Click += async (_, _) => await SaveLayout();
        reset.Click += (_, _) => dock.Reset();

        branch.Items.Add(save);
        branch.Items.Add(reset);

        // Стандартный набор не удаляется: он — то, куда возвращаются.
        if (!string.Equals(dock.Layout, DockLayout.DefaultName, StringComparison.Ordinal))
        {
            var forget = new AxMenuItem
            {
                Header = Localizer.Instance["menu.layout.delete"],
                IsDestructive = true,
            };

            forget.Click += (_, _) => dock.Forget();
            branch.Items.Add(forget);
        }

        return branch;
    }

    /// <summary>Галочка в колонке значков пункта.</summary>
    private static AxIcon Check() => new() { Size = AxIconSize.Small, Data = AxIcons.Check };
}
