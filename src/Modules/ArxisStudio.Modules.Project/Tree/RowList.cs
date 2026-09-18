using System.Globalization;
using ArxisStudio.Modules.Project.Model;
using Avalonia.Collections;

namespace ArxisStudio.Modules.Project.Tree;

/// <summary>
/// Плоское дерево: видимые узлы подряд, каждый со своей глубиной.
/// </summary>
/// <remarks>
/// Дерево решения показывается списком строк, а не деревом контролов: список виртуализован и держит
/// в памяти столько строк, сколько видно, а дерево контролов заводит контрол на каждый раскрытый
/// узел. Для решения на тысячи файлов разница — окно, которое отвечает, и окно, которое думает.
/// <para>
/// Любая перемена — раскрытие, свёртка, новый снимок, поиск — сводится к одному правилу: строки,
/// которые остались на месте, остаются теми же объектами, а меняется только середина между общим
/// началом и общим концом. Раскрытие — одна вставка, свёртка — одно удаление, новый файл — одна
/// вставка; список получает одно событие вместо сброса, и выделение с прокруткой не теряются.
/// </para>
/// <para>
/// Раскрытое помнится ключами узлов, а не строками: строки уходят вместе со свёрнутой веткой, а
/// ключ переживает и свёртку родителя, и перезагрузку решения.
/// </para>
/// </remarks>
internal sealed class RowList
{
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _shown;
    private Func<Node, bool>? _scope;
    private string? _query;
    private Node? _root;

    /// <summary>Видимые строки — источник для списка.</summary>
    public AvaloniaList<Row> Rows { get; } = [];

    /// <summary>Корень показанного дерева.</summary>
    public Node? Root => _root;

    /// <summary>Идёт ли поиск: дерево сужено до совпадений и их предков.</summary>
    public bool IsFiltered => _shown is not null;

    /// <summary>Сколько узлов нашлось поиском.</summary>
    public int Found { get; private set; }

    /// <summary>
    /// Показывает дерево — новое или новый снимок прежнего.
    /// </summary>
    /// <param name="root">Корень.</param>
    /// <param name="forget">Забыть раскрытое: открыто другое решение, и прежние ключи ему чужие.</param>
    /// <remarks>
    /// Раскрытое решения, открытого впервые, — корень и проекты: так Rider показывает решение, и так
    /// видно, из чего оно состоит, не щёлкая по каждому проекту.
    /// </remarks>
    public void Show(Node? root, bool forget)
    {
        if (forget)
            _expanded.Clear();

        _root = root;

        if (root is not null && (forget || _expanded.Count == 0))
        {
            _expanded.Add(root.Key);

            foreach (var node in root.Descendants().Where(node => node.Kind is NodeKind.Project or NodeKind.SolutionFolder))
                _expanded.Add(node.Key);
        }

        if (_shown is not null)
            Filter(_query);
        else
            Sync();
    }

    /// <summary>
    /// Ограничивает показ: в строки попадают только узлы, для которых условие верно.
    /// </summary>
    /// <param name="scope">Условие; <c>null</c> — показывать всё.</param>
    /// <remarks>Левая колонка двух колонок показывает только контейнеры — папки, проекты, решение.</remarks>
    public void Limit(Func<Node, bool>? scope)
    {
        _scope = scope;
        Sync();
    }

    /// <summary>
    /// Сужает дерево до узлов, чьё имя содержит запрос, и их предков.
    /// </summary>
    /// <param name="query">Запрос; пусто — показывать всё, как было раскрыто до поиска.</param>
    /// <remarks>
    /// Предки совпадений раскрыты на время поиска, но раскрытое человеком не трогается: снятый
    /// поиск возвращает дерево ровно таким, каким оно было.
    /// </remarks>
    public void Filter(string? query)
    {
        _query = string.IsNullOrWhiteSpace(query) ? null : query.Trim();

        if (_query is null || _root is null)
        {
            _shown = null;
            Found = 0;
            Sync();
            return;
        }

        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = 0;

        foreach (var node in _root.Descendants())
        {
            if (_scope is not null && !_scope(node))
                continue;

            if (CultureInfo.CurrentCulture.CompareInfo.IndexOf(node.Name, _query, CompareOptions.IgnoreCase) < 0)
                continue;

            found++;
            shown.Add(node.Key);

            foreach (var ancestor in node.Ancestors())
                shown.Add(ancestor.Key);
        }

        _shown = shown;
        Found = found;
        Sync();
    }

    /// <summary>Раскрыт ли узел в показанном.</summary>
    /// <param name="node">Узел.</param>
    public bool IsExpanded(Node node) =>
        _shown is not null ? _shown.Contains(node.Key) && Children(node).Any() : _expanded.Contains(node.Key);

    /// <summary>Раскрывает или сворачивает узел строки.</summary>
    /// <param name="row">Строка.</param>
    public void Toggle(Row row)
    {
        if (row.IsExpanded)
            Collapse(row);
        else
            Expand(row);
    }

    /// <summary>Раскрывает узел строки.</summary>
    /// <param name="row">Строка.</param>
    public void Expand(Row row)
    {
        if (!row.HasChildren || _shown is not null)
            return;

        _expanded.Add(row.Key);
        Sync();
    }

    /// <summary>Сворачивает узел строки.</summary>
    /// <param name="row">Строка.</param>
    public void Collapse(Row row)
    {
        if (_shown is not null)
            return;

        _expanded.Remove(row.Key);
        Sync();
    }

    /// <summary>Раскрывает узел и всю его ветку.</summary>
    /// <param name="row">Строка.</param>
    public void ExpandBranch(Row row)
    {
        if (_shown is not null)
            return;

        foreach (var node in row.Node.Descendants().Where(node => node.Children.Count > 0))
            _expanded.Add(node.Key);

        Sync();
    }

    /// <summary>Сворачивает ветку узла.</summary>
    /// <param name="row">Строка.</param>
    public void CollapseBranch(Row row)
    {
        if (_shown is not null)
            return;

        foreach (var node in row.Node.Descendants())
            _expanded.Remove(node.Key);

        Sync();
    }

    /// <summary>
    /// Сворачивает всё, кроме корня: так дерево возвращается к виду «решение и его проекты».
    /// </summary>
    public void CollapseAll()
    {
        _expanded.Clear();

        if (_root is not null)
            _expanded.Add(_root.Key);

        Sync();
    }

    /// <summary>Раскрывает предков узла, чтобы его строка стала видна.</summary>
    /// <param name="node">Узел.</param>
    public void Reveal(Node node)
    {
        foreach (var ancestor in node.Ancestors())
            _expanded.Add(ancestor.Key);

        Sync();
    }

    /// <summary>Строка узла с таким ключом, если она видна.</summary>
    /// <param name="key">Ключ.</param>
    public Row? Find(string key) =>
        Rows.FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Строка родителя, если она видна.</summary>
    /// <param name="row">Строка.</param>
    public Row? ParentOf(Row row) => row.Node.Parent is { } parent ? Find(parent.Key) : null;

    /// <summary>Узел с таким ключом во всём дереве, видим он или нет.</summary>
    /// <param name="key">Ключ.</param>
    public Node? Locate(string key) =>
        _root?.Descendants().FirstOrDefault(node => string.Equals(node.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Дети узла в том, что сейчас показано.</summary>
    private IEnumerable<Node> Children(Node node)
    {
        IEnumerable<Node> children = node.Children;

        if (_scope is not null)
            children = children.Where(_scope);

        if (_shown is not null)
            children = children.Where(child => _shown.Contains(child.Key));

        return children;
    }

    /// <summary>Видимые узлы подряд, каждый со своей глубиной.</summary>
    private List<(Node Node, int Depth)> Flatten()
    {
        var visible = new List<(Node, int)>();

        if (_root is null || (_shown is not null && !_shown.Contains(_root.Key)))
            return visible;

        Walk(_root, 0);

        return visible;

        void Walk(Node node, int depth)
        {
            visible.Add((node, depth));

            if (!IsExpanded(node))
                return;

            foreach (var child in Children(node))
                Walk(child, depth + 1);
        }
    }

    /// <summary>
    /// Сводит строки к тому, что должно быть видно, одной правкой середины.
    /// </summary>
    private void Sync()
    {
        var wanted = Flatten();
        var existing = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in Rows)
            existing.TryAdd(row.Key, row);

        var next = new List<Row>(wanted.Count);

        foreach (var (node, depth) in wanted)
        {
            var row = existing.Remove(node.Key, out var kept) ? kept : new Row(node, depth);

            row.Update(node, depth, Children(node).Any(), IsExpanded(node));
            next.Add(row);
        }

        var prefix = 0;

        while (prefix < Rows.Count && prefix < next.Count && ReferenceEquals(Rows[prefix], next[prefix]))
            prefix++;

        var suffix = 0;

        while (suffix < Rows.Count - prefix
               && suffix < next.Count - prefix
               && ReferenceEquals(Rows[Rows.Count - 1 - suffix], next[next.Count - 1 - suffix]))
        {
            suffix++;
        }

        var removed = Rows.Count - prefix - suffix;

        if (removed > 0)
            Rows.RemoveRange(prefix, removed);

        var inserted = next.Count - prefix - suffix;

        if (inserted > 0)
            Rows.InsertRange(prefix, next.GetRange(prefix, inserted));
    }
}
