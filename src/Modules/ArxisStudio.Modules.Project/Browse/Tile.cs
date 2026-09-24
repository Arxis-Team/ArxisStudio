using System.ComponentModel;
using ArxisStudio.Modules.Project.Model;
using Avalonia.Media.Imaging;

namespace ArxisStudio.Modules.Project.Browse;

/// <summary>Чем плитка рисует свой предмет.</summary>
internal enum TileLook
{
    /// <summary>Силуэт папки: папка проекта или решения.</summary>
    Folder,

    /// <summary>Силуэт документа: файл на диске.</summary>
    Document,

    /// <summary>Подложка со значком: предмет модели — проект, зависимость, группа.</summary>
    Plate,
}

/// <summary>
/// Предмет правой колонки: узел, каким его показывает плитка или строка списка.
/// </summary>
/// <remarks>
/// Живёт дольше снимка, как строка дерева: новый снимок того же решения переиспользует плитку с тем
/// же ключом, и выделение в колонке его переживает. Что на диске — силуэт, что в модели — подложка:
/// папку и файл узнают по форме, а пакет или платформа формы на диске не имеют, и значок их вида
/// стоит на подложке.
/// </remarks>
internal sealed class Tile : INotifyPropertyChanged
{
    /// <summary>Заводит плитку.</summary>
    /// <param name="node">Узел.</param>
    /// <param name="nested">Вложен ли файл в соседа — в колонке он стоит сразу за владельцем.</param>
    public Tile(Node node, bool nested)
    {
        Node = node;
        IsNested = nested;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Узел.</summary>
    public Node Node { get; private set; }

    /// <summary>Устойчивый ключ узла.</summary>
    public string Key => Node.Key;

    /// <summary>Подпись.</summary>
    public string Name => Node.Name;

    /// <summary>Вторая подпись: версия пакета, платформа.</summary>
    public string? Detail => Node.Detail;

    /// <summary>Есть ли вторая подпись.</summary>
    public bool HasDetail => Node.Detail is { Length: > 0 };

    /// <summary>Вложен ли файл в соседа.</summary>
    public bool IsNested { get; private set; }

    /// <summary>Контейнер ли это: по нему в колонке переходят, а не открывают его.</summary>
    public bool IsContainer => Node.IsContainer;

    /// <summary>Чем плитка рисует свой предмет.</summary>
    public TileLook Look => Node.Kind switch
    {
        NodeKind.Folder or NodeKind.SolutionFolder => TileLook.Folder,
        NodeKind.File => TileLook.Document,
        _ => TileLook.Plate,
    };

    /// <summary>Силуэт ли у плитки — или подложка со значком.</summary>
    public bool IsSilhouette => Look != TileLook.Plate;

    /// <summary>Подложка ли у плитки.</summary>
    public bool IsPlate => Look == TileLook.Plate;

    /// <summary>Вырезан и ждёт вставки — плитка приглушена.</summary>
    public bool IsCut
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            Raise(nameof(IsCut));
            Raise(nameof(ShowsPreview));
            Raise(nameof(ShowsSilhouette));
        }
    }

    /// <summary>Уменьшенная копия картинки; пусто — плитка рисует силуэт.</summary>
    /// <remarks>
    /// Ставит её колонка, когда плитка видна, а держит служба превью: растр освобождается там, где
    /// известно, что его больше никто не рисует, а не у плитки, которую список может пересоздать.
    /// </remarks>
    public Bitmap? Preview
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            field = value;
            Raise(nameof(Preview));
            Raise(nameof(ShowsPreview));
            Raise(nameof(ShowsSilhouette));
        }
    }

    /// <summary>Плитка рисует картинку: превью есть, и файл не вырезан.</summary>
    /// <remarks>
    /// Вырезанный файл показывает приглушённый силуэт: ключа прозрачности у темы нет, а приглушённый
    /// силуэт — уже знакомый знак «вырезан, ждёт вставки».
    /// </remarks>
    public bool ShowsPreview => Preview is not null && !IsCut;

    /// <summary>Плитка рисует силуэт: у предмета на диске, пока на его месте нет картинки.</summary>
    public bool ShowsSilhouette => IsSilhouette && !ShowsPreview;

    /// <summary>Сюда ляжет то, что несут из проводника, — плитка отмечена целью.</summary>
    public bool IsDropTarget
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            Raise(nameof(IsDropTarget));
        }
    }

    /// <summary>Подсказка и строка под колонкой: путь от решения, а у предмета модели — имя с версией.</summary>
    public string Hint => Node.Relative ?? (HasDetail ? $"{Node.Name} {Node.Detail}" : Node.Name);

    /// <summary>Имя — по нему ищет набор букв, и его же читает диктор.</summary>
    public override string ToString() => Node.Name;

    /// <summary>Ставит плитке узел нового снимка; сообщает только о том, что сменилось.</summary>
    internal void Update(Node node, bool nested)
    {
        if (!ReferenceEquals(Node, node))
        {
            Node = node;
            Raise(null);
        }

        if (IsNested != nested)
        {
            IsNested = nested;
            Raise(nameof(IsNested));
        }
    }

    private void Raise(string? property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
