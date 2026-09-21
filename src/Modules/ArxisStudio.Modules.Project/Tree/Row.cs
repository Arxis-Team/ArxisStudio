using System.ComponentModel;
using ArxisStudio.Modules.Project.Model;

namespace ArxisStudio.Modules.Project.Tree;

/// <summary>
/// Строка плоского дерева: узел и его глубина.
/// </summary>
/// <remarks>
/// Строка живёт дольше снимка: новый снимок того же решения переиспользует строку с тем же ключом,
/// и список, выделение и прокрутка её не теряют. Узел в строке при этом новый — старый остаётся в
/// прежнем дереве вместе со всем, что в нём было.
/// </remarks>
internal sealed class Row : INotifyPropertyChanged
{
    /// <summary>Заводит строку.</summary>
    /// <param name="node">Узел.</param>
    /// <param name="depth">Глубина: у корня ноль.</param>
    public Row(Node node, int depth)
    {
        Node = node;
        Depth = depth;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Узел, который строка показывает.</summary>
    public Node Node { get; private set; }

    /// <summary>Устойчивый ключ узла.</summary>
    public string Key => Node.Key;

    /// <summary>Глубина: отступ строки — столько шагов лестницы дерева.</summary>
    public int Depth { get; private set; }

    /// <summary>Есть ли у узла дети в том, что сейчас показано: у листа шеврона нет.</summary>
    public bool HasChildren { get; private set; }

    /// <summary>Раскрыт ли узел.</summary>
    public bool IsExpanded { get; private set; }

    /// <summary>Подпись.</summary>
    public string Name => Node.Name;

    /// <summary>Вторая подпись.</summary>
    public string? Detail => Node.Detail;

    /// <summary>Есть ли вторая подпись у узла, который загрузился.</summary>
    public bool HasDetail => Node.Detail is { Length: > 0 } && !Node.IsBroken;

    /// <summary>Проект не загрузился: вторая подпись — тоном ошибки.</summary>
    public bool IsBroken => Node.IsBroken;

    /// <summary>Подсказка: путь от решения или причина, по которой проект не загрузился.</summary>
    public string? Hint => Node.Problem ?? Node.Relative;

    /// <summary>Вырезан и ждёт вставки — строка приглушена.</summary>
    public bool IsCut
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            Raise(nameof(IsCut));
        }
    }

    /// <summary>Имя строки — по нему ищет набор букв в списке, и его же читает диктор.</summary>
    public override string ToString() => Node.Name;

    /// <summary>Ставит строке узел нового снимка и её место в показанном; сообщает только о том, что сменилось.</summary>
    internal void Update(Node node, int depth, bool hasChildren, bool expanded)
    {
        var renamed = !ReferenceEquals(Node, node);

        Node = node;

        if (renamed)
        {
            Raise(nameof(Node));
            Raise(nameof(Name));
            Raise(nameof(Detail));
            Raise(nameof(HasDetail));
            Raise(nameof(IsBroken));
            Raise(nameof(Hint));
        }

        if (Depth != depth)
        {
            Depth = depth;
            Raise(nameof(Depth));
        }

        if (HasChildren != hasChildren)
        {
            HasChildren = hasChildren;
            Raise(nameof(HasChildren));
        }

        if (IsExpanded != expanded)
        {
            IsExpanded = expanded;
            Raise(nameof(IsExpanded));
        }
    }

    private void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
