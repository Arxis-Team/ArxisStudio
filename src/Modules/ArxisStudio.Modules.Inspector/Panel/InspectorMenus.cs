using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Catalog;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ArxisStudio.Modules.Inspector.Panel;

/// <summary>
/// Меню инспектора: меню строки свойства и меню ⋮ всей цели — свои пункты, пункты цели и вклады
/// <see cref="InspectorMenu"/>.
/// </summary>
/// <remarks>
/// <para>
/// Пункты, которым нечего делать, выключены, а не спрятаны: меню не прыгает от строки к строке, и рука находит
/// «Сбросить» там же, где в прошлый раз.
/// </para>
/// <para>
/// Меню дополняют чужие: цель (<see cref="InspectorTarget.FillMenu"/>) и вклады плагинов. Каждый зовётся через свой
/// шов, и пункт, который он добавил, тоже: упавший пункт засчитывается тому, кто его добавил, а не инспектору.
/// Пункты собираются отдельно от показа — попап отдельное окно, которого у безголового прогона нет.
/// </para>
/// </remarks>
/// <param name="list">Список — дорога к цели.</param>
/// <param name="catalog">Каталог вкладов.</param>
internal sealed class InspectorMenus(InspectorList list, InspectorCatalog catalog)
{
    /// <summary>Пункты меню строки.</summary>
    /// <param name="row">Строка.</param>
    public IReadOnlyList<Control> ForRow(InspectorRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var words = list.Words;
        var items = new List<Control>
        {
            Item(words["inspector.reset"], row.CanReset, row.Reset, new KeyGesture(Key.Delete)),
        };

        if (row.IsExpression)
        {
            var writable = !row.Property.IsReadOnly;

            items.Add(Item(words["inspector.expression.edit"], writable, () => row.EditAsText(), new KeyGesture(Key.F2)));
            items.Add(Item(words["inspector.expression.literal"], writable && row.Value?.Text is not null, row.Replace));
        }

        Extend(items, row.Property, row.Value);

        return items;
    }

    /// <summary>Пункты меню ⋮: свои — сворачивание разделов, — затем цели и вкладов.</summary>
    public IReadOnlyList<Control> ForTarget()
    {
        var words = list.Words;
        var grouped = !list.Alphabetical && !list.IsSearching;
        var items = new List<Control>
        {
            Item(words["inspector.expandAll"], grouped, () => list.FoldAll(folded: false)),
            Item(words["inspector.collapseAll"], grouped, () => list.FoldAll(folded: true)),
        };

        Extend(items, null, null);

        return items;
    }

    /// <summary>Показывает меню.</summary>
    /// <param name="anchor">К чему привязать.</param>
    /// <param name="items">Пункты.</param>
    /// <param name="atPointer">Просили мышью: меню встаёт под указателем.</param>
    public static void Show(Control anchor, IReadOnlyList<Control> items, bool atPointer)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
            return;

        var flyout = new AxMenuFlyout();

        foreach (var item in items)
            flyout.Items.Add(item);

        flyout.ShowAt(anchor, atPointer);
    }

    /// <summary>Пункты цели и вкладов — после своих, за чертой.</summary>
    private void Extend(List<Control> items, InspectorProperty? property, InspectorValue? value)
    {
        if (list.Type is not { } type)
            return;

        var menu = new Menu(list, property, value, type);

        if (list.Guard is { } target)
        {
            menu.Owner = target;
            target.Run("fills a menu", it => it.FillMenu(menu));
        }

        foreach (var contribution in catalog.Menus)
        {
            menu.Owner = contribution;
            contribution.Run("fills a menu", it => it.Fill(menu));
        }

        while (menu.Items.Count > 0 && menu.Items[^1] is AxSeparator)
            menu.Items.RemoveAt(menu.Items.Count - 1);

        if (menu.Items.Count == 0)
            return;

        items.Add(new AxSeparator());
        items.AddRange(menu.Items);
    }

    private static AxMenuItem Item(string header, bool enabled, Action act, KeyGesture? gesture = null)
    {
        var item = new AxMenuItem { Header = header, InputGesture = gesture, IsEnabled = enabled };

        item.Click += (_, _) => act();

        return item;
    }

    /// <summary>Меню, которое дополняют: пункт помнит, кто его добавил, и зовётся через его шов.</summary>
    private sealed class Menu(InspectorList list, InspectorProperty? property, InspectorValue? value, InspectorTypeInfo type)
        : InspectorMenuContext
    {
        public List<Control> Items { get; } = [];

        public IStudioGuarded<object>? Owner { get; set; }

        public override InspectorProperty? Property => property;

        public override InspectorValue? Value => value;

        public override InspectorTypeInfo Type => type;

        public override int Count => list.Count;

        public override void Add(string title, Action action, Geometry? icon = null, bool isEnabled = true)
        {
            ArgumentException.ThrowIfNullOrEmpty(title);
            ArgumentNullException.ThrowIfNull(action);

            var owner = Owner;
            var item = new AxMenuItem
            {
                Header = title,
                IsEnabled = isEnabled,
                Icon = icon is null ? null : new AxIcon { Data = icon },
            };

            item.Click += (_, _) =>
            {
                if (owner is null)
                    action();
                else
                    owner.Run("runs a menu item", _ => action());
            };

            Items.Add(item);
        }

        public override void AddSeparator()
        {
            if (Items.Count > 0 && Items[^1] is not AxSeparator)
                Items.Add(new AxSeparator());
        }

        public override Task<bool> WriteAsync(IReadOnlyList<InspectorWrite> writes, string? label = null) =>
            list.WriteAsync(writes, label);
    }
}
