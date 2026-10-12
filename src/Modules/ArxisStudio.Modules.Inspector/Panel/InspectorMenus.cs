using System.Net;
using System.Text.RegularExpressions;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Catalog;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
/// <para>
/// <b>Буфер обмена — текст разметки.</b> Значение кладётся так, как его пишут, а заданные свойства — атрибутами:
/// <c>Width="150" Margin="8,4"</c>. Такой текст вставляется в XAML руками и обратно в инспектор — другому элементу,
/// другой форме: вставка берёт те свойства, что у цели есть и пишутся, одной правкой.
/// </para>
/// </remarks>
/// <param name="list">Список — дорога к цели.</param>
/// <param name="catalog">Каталог вкладов.</param>
/// <param name="owner">Чьё окно даёт буфер обмена.</param>
internal sealed partial class InspectorMenus(InspectorList list, InspectorCatalog catalog, Visual owner)
{
    /// <summary>Пункты меню строки.</summary>
    /// <param name="row">Строка.</param>
    public IReadOnlyList<Control> ForRow(InspectorRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var words = list.Words;
        var copied = row.Value is { IsMixed: false } value ? value.Written ?? value.Text : null;
        var items = new List<Control>
        {
            Item(words["inspector.reset"], row.CanReset, row.Reset, new KeyGesture(Key.Delete)),
            Item(words["inspector.copyValue"], !string.IsNullOrEmpty(copied), () => _ = CopyAsync(copied!)),
            Item(words["inspector.pasteValue"], !row.Property.IsReadOnly && row.Property.AcceptsText, () => _ = PasteValueAsync(row)),
        };

        if (row.IsExpression)
        {
            var writable = !row.Property.IsReadOnly;

            items.Add(Item(words["inspector.expression.edit"], writable, () => row.EditAsText(), new KeyGesture(Key.F2)));
            items.Add(Item(words["inspector.expression.literal"], writable && row.Value?.Text is not null, row.Replace));
        }
        else if (row.CanEditFieldAsText)
        {
            // Поля преобразования передают не всякую запись: целиком её правят текстом, как выражение.
            items.Add(Item(words["inspector.expression.edit"], true, () => row.EditFieldAsText()));
        }

        Extend(items, row.Property, row.Value);

        return items;
    }

    /// <summary>
    /// Пункты меню ⋮: свои — сбросить, скопировать и вставить заданное, свернуть и развернуть разделы, — затем цели и
    /// вкладов.
    /// </summary>
    public IReadOnlyList<Control> ForTarget()
    {
        var words = list.Words;
        var grouped = !list.Alphabetical && !list.IsSearching;
        var written = list.WrittenProperties();
        var shown = list.Target is not null;
        var items = new List<Control>
        {
            Item(words["inspector.resetAll"], written.Count > 0, () => _ = ResetAsync(written)),
            Item(words["inspector.copyProperties"], written.Count > 0, () => _ = CopyAsync(Attributes(written))),
            Item(words["inspector.pasteProperties"], shown, () => _ = PastePropertiesAsync()),
            new AxSeparator(),
            Item(words["inspector.expandAll"], grouped, () => list.FoldAll(folded: false)),
            Item(words["inspector.collapseAll"], grouped, () => list.FoldAll(folded: true)),
        };

        Extend(items, null, null);

        return items;
    }

    /// <summary>
    /// Пункты меню ⋮ раздела — как меню компонента у Unity: сбросить раздел одной правкой, скопировать его значения и
    /// вставить их, свернуть и развернуть разделы, — затем цели и вкладов, которые видят раздел.
    /// </summary>
    /// <param name="category">Раздел.</param>
    public IReadOnlyList<Control> ForSection(string category)
    {
        ArgumentNullException.ThrowIfNull(category);

        var words = list.Words;
        var grouped = !list.Alphabetical && !list.IsSearching;
        var written = list.WrittenProperties()
            .Where(property => string.Equals(list.CategoryOf(property), category, StringComparison.Ordinal))
            .ToList();
        var items = new List<Control>
        {
            Item(words["inspector.resetSection"], written.Count > 0, () => _ = ResetAsync(written)),
            Item(words["inspector.copySection"], written.Count > 0, () => _ = CopyAsync(Attributes(written))),
            Item(words["inspector.pasteSection"], list.Target is not null, () => _ = PastePropertiesAsync(category)),
            new AxSeparator(),
            Item(words["inspector.expandAll"], grouped, () => list.FoldAll(folded: false)),
            Item(words["inspector.collapseAll"], grouped, () => list.FoldAll(folded: true)),
        };

        Extend(items, null, null, category);

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
    private void Extend(List<Control> items, InspectorProperty? property, InspectorValue? value, string? category = null)
    {
        if (list.Type is not { } type)
            return;

        var menu = new Menu(list, property, value, type, category);

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

    /// <summary>Сбрасывает заданное — одной правкой.</summary>
    private Task<bool> ResetAsync(IReadOnlyList<InspectorProperty> written) =>
        list.WriteAsync([.. written.Select(static property => new InspectorWrite(property.Id, null))], null);

    /// <summary>Заданное — атрибутами разметки, как их пишет документ: <c>Width="150" Margin="8,4"</c>.</summary>
    private string Attributes(IReadOnlyList<InspectorProperty> written)
    {
        var parts = new List<string>();

        foreach (var property in written)
        {
            // Разное у выбранных переносить нечем: одно значение стёрло бы остальные.
            if (list.ValueOf(property.Id) is not { IsMixed: false } value || (value.Written ?? value.Text) is not { } text)
                continue;

            parts.Add($"{property.Id}=\"{Escape(text)}\"");
        }

        return string.Join(' ', parts);
    }

    /// <summary>Вставляет значение из буфера в строку.</summary>
    private async Task PasteValueAsync(InspectorRow row)
    {
        if (await ReadAsync().ConfigureAwait(true) is { Length: > 0 } text)
            await row.WriteTextAsync(text.TrimEnd('\r', '\n')).ConfigureAwait(true);
    }

    /// <summary>
    /// Вставляет свойства из буфера — атрибуты разметки, — те, что у цели есть, пишутся и прочтутся, одной правкой; из
    /// меню раздела — только свойства этого раздела.
    /// </summary>
    private async Task PastePropertiesAsync(string? category = null)
    {
        if (await ReadAsync().ConfigureAwait(true) is not { Length: > 0 } text)
            return;

        var name = list.Header()?.NameProperty;
        var writes = new List<InspectorWrite>();

        foreach (Match match in AttributePattern().Matches(text))
        {
            var id = match.Groups["name"].Value;
            var value = WebUtility.HtmlDecode(match.Groups["value"].Value);

            if (string.Equals(id, name, StringComparison.Ordinal) ||
                list.PropertyOf(id) is not { IsReadOnly: false } property ||
                (category is not null && !string.Equals(list.CategoryOf(property), category, StringComparison.Ordinal)) ||
                !list.Check(id, value).IsValid ||
                writes.Any(write => string.Equals(write.PropertyId, id, StringComparison.Ordinal)))
            {
                continue;
            }

            writes.Add(new InspectorWrite(id, value));
        }

        if (writes.Count > 0)
            await list.WriteAsync(writes, null).ConfigureAwait(true);
    }

    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(owner)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
    }

    private async Task<string?> ReadAsync() =>
        TopLevel.GetTopLevel(owner)?.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync().ConfigureAwait(true) : null;

    /// <summary>Значение атрибута так, как его пишет разметка: кавычка, амперсанд и угол — сущностями.</summary>
    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal);

    /// <summary>Атрибут разметки: имя — со строкой владельца или приставкой, — и значение в кавычках.</summary>
    [GeneratedRegex(@"(?<name>[\p{L}_][\p{L}\p{Nd}_.:-]*)\s*=\s*""(?<value>[^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex AttributePattern();

    private static AxMenuItem Item(string header, bool enabled, Action act, KeyGesture? gesture = null)
    {
        var item = new AxMenuItem { Header = header, InputGesture = gesture, IsEnabled = enabled };

        item.Click += (_, _) => act();

        return item;
    }

    /// <summary>Меню, которое дополняют: пункт помнит, кто его добавил, и зовётся через его шов.</summary>
    private sealed class Menu(
        InspectorList list, InspectorProperty? property, InspectorValue? value, InspectorTypeInfo type, string? category)
        : InspectorMenuContext
    {
        public List<Control> Items { get; } = [];

        public IStudioGuarded<object>? Owner { get; set; }

        public override InspectorProperty? Property => property;

        public override InspectorValue? Value => value;

        public override string? Category => category;

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
