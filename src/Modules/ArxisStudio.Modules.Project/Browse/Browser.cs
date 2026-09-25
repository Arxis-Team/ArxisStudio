using System.ComponentModel;
using System.Globalization;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Tree;
using Avalonia.Collections;

namespace ArxisStudio.Modules.Project.Browse;

/// <summary>
/// Правая колонка окна проекта: содержимое текущего контейнера или найденное поиском.
/// </summary>
/// <remarks>
/// Как правая колонка Project в Unity: слева дерево папок, справа — что лежит в выбранной, крупно, и
/// путь до неё крошками. Контейнер помнится ключом, а не узлом: новый снимок того же решения
/// переносит колонку в тот же контейнер, а пропавший — к ближайшему уцелевшему предку, и человек не
/// оказывается вдруг в корне оттого, что в соседней папке добавили файл.
/// <para>
/// Вложенный файл стоит сразу за владельцем, плоско: <c>App.axaml</c>, за ним <c>App.axaml.cs</c>.
/// Раскрывать плитку внутри плитки — это дерево в колонке, которая затем и заведена, чтобы дерева в
/// ней не было.
/// </para>
/// <para>
/// Поиск ищет по всему решению, как «Search: All» у Unity, и показывает не больше
/// <see cref="Cap"/> найденного: плитки раскладываются без виртуализации, и тысяча совпадений в
/// одном запросе — это окно, которое перестаёт отвечать на каждую букву.
/// </para>
/// </remarks>
internal sealed class Browser
{
    /// <summary>Сколько найденного колонка показывает самое большее.</summary>
    public const int Cap = 500;

    private Node? _root;
    private string? _query;

    /// <summary>
    /// Колонка переложена: новый снимок, переход или поиск.
    /// </summary>
    /// <remarks>
    /// Приходит и тогда, когда список предметов не изменился: плитки того же ключа переживают снимок, и
    /// перезапись картинки, сделанная окном, другого следа не оставляет. По нему превью сверяются с
    /// диском.
    /// </remarks>
    public event EventHandler? Refreshed;

    /// <summary>Предметы колонки — источник для плиток и для списка.</summary>
    public AvaloniaList<Tile> Items { get; } = [];

    /// <summary>Путь от корня до текущего контейнера — источник для крошек.</summary>
    public AvaloniaList<Segment> Segments { get; } = [];

    /// <summary>Текущий контейнер; пусто — решения нет.</summary>
    public Node? Current { get; private set; }

    /// <summary>Идёт ли поиск: колонка показывает найденное, а не контейнер.</summary>
    public bool IsSearching => _query is not null;

    /// <summary>Сколько нашлось — всего, а не только показанного.</summary>
    public int Found { get; private set; }

    /// <summary>
    /// Показывает новый снимок — или ничего, когда решение закрыто.
    /// </summary>
    /// <param name="root">Корень нового дерева.</param>
    public void Show(Node? root)
    {
        var was = Current;

        _root = root;
        Current = root is null ? null : Resolve(was) ?? root;
        Refresh();
    }

    /// <summary>Переходит в контейнер; поиск при этом снимается — колонка показывает место.</summary>
    /// <param name="container">Контейнер того же дерева.</param>
    /// <returns>Перешла ли колонка: лист контейнером не бывает.</returns>
    public bool Go(Node container)
    {
        ArgumentNullException.ThrowIfNull(container);

        if (!container.IsContainer || _root is null)
            return false;

        Current = container;
        _query = null;
        Refresh();

        return true;
    }

    /// <summary>Поднимается к родителю текущего контейнера.</summary>
    /// <returns>Поднялась ли: у корня родителя нет.</returns>
    public bool Up() => Current?.Parent is { } parent && Go(parent);

    /// <summary>Ищет по всему решению.</summary>
    /// <param name="query">Запрос; пусто — показать текущий контейнер.</param>
    public void Search(string? query)
    {
        _query = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        Refresh();
    }

    /// <summary>Место узла среди плиток его родителя — так, как их разложила бы колонка.</summary>
    /// <param name="node">Узел.</param>
    /// <returns>Место; −1 — у узла нет родителя.</returns>
    /// <remarks>Файл занимает место вместе с вложенными: они стоят следом за ним.</remarks>
    public static int Place(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Parent is not { } parent)
            return -1;

        var at = 0;

        foreach (var child in parent.Children)
        {
            if (ReferenceEquals(child, node))
                return at;

            at += child.Kind == NodeKind.File ? child.Descendants().Count() : 1;
        }

        return -1;
    }

    /// <summary>Узел с таким ключом в показанном дереве.</summary>
    /// <param name="key">Ключ.</param>
    private Node? Locate(string key) =>
        _root?.Descendants().FirstOrDefault(node => Node.KeyComparer.Equals(node.Key, key));

    /// <summary>Тот же контейнер в новом дереве, а если его нет — ближайший уцелевший предок.</summary>
    private Node? Resolve(Node? was)
    {
        if (was is null)
            return null;

        foreach (var node in was.Ancestors().Prepend(was))
        {
            if (Locate(node.Key) is { IsContainer: true } found)
                return found;
        }

        return null;
    }

    private void Refresh()
    {
        Segments.Clear();

        if (Current is not null && _query is null)
            Segments.AddRange(Current.Ancestors().Reverse().Append(Current).Select(node => new Segment(node)));

        var next = new List<(Node Node, bool Nested)>();

        if (_query is not null && _root is not null)
        {
            var found = _root.Descendants()
                .Skip(1)
                .Where(node => CultureInfo.CurrentCulture.CompareInfo.IndexOf(node.Name, _query, CompareOptions.IgnoreCase) >= 0)
                .ToList();

            Found = found.Count;
            next.AddRange(found.Take(Cap).Select(node => (node, false)));
        }
        else if (Current is not null)
        {
            Found = 0;

            foreach (var child in Current.Children)
            {
                next.Add((child, false));

                if (child.Kind == NodeKind.File)
                    next.AddRange(child.Descendants().Skip(1).Select(nested => (nested, true)));
            }
        }
        else
        {
            Found = 0;
        }

        var existing = new Dictionary<string, Tile>(Node.KeyComparer);

        foreach (var tile in Items)
            existing.TryAdd(tile.Key, tile);

        var tiles = new List<Tile>(next.Count);

        foreach (var (node, nested) in next)
        {
            if (existing.Remove(node.Key, out var kept))
                kept.Update(node, nested);
            else
                kept = new Tile(node, nested);

            tiles.Add(kept);
        }

        Splice.Into(Items, tiles);
        Refreshed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Сегмент крошек: контейнер на пути от корня до текущего.</summary>
/// <param name="node">Контейнер.</param>
internal sealed class Segment(Node node) : INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Контейнер.</summary>
    public Node Node { get; } = node;

    /// <summary>Сюда ляжет то, что несут, — сегмент отмечен целью.</summary>
    public bool IsDropTarget
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDropTarget)));
        }
    }

    /// <summary>Подпись сегмента — её показывает крошка и читает диктор.</summary>
    public override string ToString() => Node.Name;
}
