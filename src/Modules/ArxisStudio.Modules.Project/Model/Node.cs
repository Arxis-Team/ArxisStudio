using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Project.Model;

/// <summary>Что за узел стоит в дереве решения.</summary>
public enum NodeKind
{
    /// <summary>Корень: решение или одиночный проект, открытый без него.</summary>
    Solution,

    /// <summary>Папка решения — логическая, на диске её нет.</summary>
    SolutionFolder,

    /// <summary>Проект.</summary>
    Project,

    /// <summary>«Зависимости» проекта — всё, на что он ссылается.</summary>
    Dependencies,

    /// <summary>Группа зависимостей одного вида: пакеты, проекты, платформы.</summary>
    DependencyGroup,

    /// <summary>Одна зависимость.</summary>
    Dependency,

    /// <summary>Папка проекта на диске.</summary>
    Folder,

    /// <summary>Файл проекта на диске.</summary>
    File,
}

/// <summary>Вид зависимости — по нему зависимости делятся на группы.</summary>
public enum DependencyKind
{
    /// <summary>Платформа — <c>FrameworkReference</c>, прежде всего <c>Microsoft.NETCore.App</c>.</summary>
    Frameworks,

    /// <summary>Пакет NuGet.</summary>
    Packages,

    /// <summary>Другой проект.</summary>
    Projects,

    /// <summary>Сборка по пути.</summary>
    Assemblies,

    /// <summary>Анализатор.</summary>
    Analyzers,
}

/// <summary>Вид файла — по нему файл получает значок и цвет.</summary>
public enum FileKind
{
    /// <summary>Код на C#.</summary>
    CSharp,

    /// <summary>Разметка: <c>.axaml</c>, <c>.xaml</c>.</summary>
    Markup,

    /// <summary>XML: файлы проекта, манифест, ресурсы.</summary>
    Xml,

    /// <summary>JSON.</summary>
    Json,

    /// <summary>Картинка.</summary>
    Image,

    /// <summary>Текст: <c>.md</c>, <c>.txt</c>.</summary>
    Text,

    /// <summary>Всё остальное.</summary>
    Other,
}

/// <summary>
/// Узел дерева решения.
/// </summary>
/// <remarks>
/// Неизменяем после постройки: дерево строится вне потока интерфейса целиком и отдаётся готовым, а
/// показ берёт из него то, что раскрыто. Ключ узла — строка от путей, а не
/// <see cref="ProjectIdentity"/>: идентичность перевыдаётся с каждым сеансом, а раскрытое и
/// выделенное должно пережить и перезагрузку решения, и повторное открытие того же.
/// </remarks>
public sealed class Node
{
    private readonly List<Node> _children = [];

    /// <summary>
    /// Как сравнивают ключи узлов: без регистра. Ключ строится от путей, и окно всегда сравнивало его
    /// так — своей копией в каждом месте.
    /// </summary>
    public static StringComparer KeyComparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Устойчивый ключ: одинаков у того же узла в любом снимке того же решения.</summary>
    public required string Key { get; init; }

    /// <summary>Что за узел.</summary>
    public required NodeKind Kind { get; init; }

    /// <summary>Подпись.</summary>
    public required string Name { get; init; }

    /// <summary>Вторая подпись, тише первой: число проектов, версия пакета, платформа.</summary>
    public string? Detail { get; init; }

    /// <summary>Файл или папка на диске; у групп, зависимостей и папок решения — пусто.</summary>
    public CanonicalPath Path { get; init; } = CanonicalPath.None;

    /// <summary>Путь от папки решения — для подсказки и копирования.</summary>
    public string? Relative { get; init; }

    /// <summary>Вид файла; у прочих узлов — <see cref="FileKind.Other"/>.</summary>
    public FileKind FileKind { get; init; } = FileKind.Other;

    /// <summary>Вид группы или зависимости.</summary>
    public DependencyKind? Dependency { get; init; }

    /// <summary>Проект, которому узел принадлежит; у корня и папок решения — пусто.</summary>
    public CanonicalPath Project { get; init; } = CanonicalPath.None;

    /// <summary>Проект не загрузился: подпись тоном ошибки, а причина — подсказкой.</summary>
    public bool IsBroken { get; init; }

    /// <summary>Причина, по которой проект не загрузился.</summary>
    public string? Problem { get; init; }

    /// <summary>Узел, в котором этот лежит; у корня — пусто.</summary>
    public Node? Parent { get; private set; }

    /// <summary>Дети в том порядке, в каком их показывают.</summary>
    public IReadOnlyList<Node> Children => _children;

    /// <summary>Контейнер ли это — то, что стоит в левой колонке двух колонок.</summary>
    public bool IsContainer => Kind is not (NodeKind.File or NodeKind.Dependency);

    /// <inheritdoc/>
    public override string ToString() => Name;

    /// <summary>Тот же ли это узел — в этом снимке или в другом: ключи совпадают.</summary>
    /// <param name="other">Другой узел; пусто — не тот же.</param>
    public bool Is(Node? other) => other is not null && KeyComparer.Equals(Key, other.Key);

    /// <summary>Кладёт ребёнка; только пока дерево строится.</summary>
    internal void Add(Node child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    /// <summary>Убирает ребёнка — когда файл уходит под своего владельца.</summary>
    internal bool Remove(Node child)
    {
        if (!_children.Remove(child))
            return false;

        child.Parent = null;

        return true;
    }

    /// <summary>Переставляет детей; только пока дерево строится.</summary>
    internal void Order(Comparison<Node> comparison) => _children.Sort(comparison);

    /// <summary>Предки узла от ближнего к корню.</summary>
    public IEnumerable<Node> Ancestors()
    {
        for (var node = Parent; node is not null; node = node.Parent)
            yield return node;
    }

    /// <summary>Узел и все его потомки, сверху вниз.</summary>
    public IEnumerable<Node> Descendants()
    {
        yield return this;

        foreach (var child in _children)
        {
            foreach (var node in child.Descendants())
                yield return node;
        }
    }
}
