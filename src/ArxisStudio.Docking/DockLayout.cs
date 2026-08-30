using System.Text.Json.Serialization;

namespace ArxisStudio.Docking;

/// <summary>
/// Одно рабочее место: дерево главного окна и плавающие окна при нём.
/// </summary>
public sealed class DockWorkspace
{
    /// <summary>Корень дерева главного окна.</summary>
    public DockNode Root { get; init; } = new DockGroup { Id = "root" };

    /// <summary>Оторванные окна.</summary>
    public IReadOnlyList<DockWindow> Floating { get; init; } = [];

    /// <summary>
    /// Группа, в которую открываются документы; null — такой нет.
    /// </summary>
    /// <remarks>
    /// Документы ничем не выделены как тип: выделен указатель. В названной группе
    /// может стоять и обычная панель, а документ — уехать в боковую. Когда группы
    /// нет, студия заводит новую и указывает сюда; пункт меню «показать область
    /// документов» делает то же самое явно.
    /// </remarks>
    public string? DocumentHome { get; init; }
}

/// <summary>
/// Оторванное окно: своё дерево и место на экране.
/// </summary>
public sealed class DockWindow
{
    /// <summary>Корень дерева этого окна.</summary>
    public DockNode Root { get; init; } = new DockGroup { Id = "float" };

    /// <summary>Положение слева, в пикселях экрана.</summary>
    public double X { get; init; }

    /// <summary>Положение сверху, в пикселях экрана.</summary>
    public double Y { get; init; }

    /// <summary>Ширина окна.</summary>
    public double Width { get; init; } = 420;

    /// <summary>Высота окна.</summary>
    public double Height { get; init; } = 320;
}

/// <summary>
/// Всё, что студия помнит о раскладке: наборы и выбранный из них.
/// </summary>
/// <remarks>
/// Версия формата здесь есть, хотя у прочих файлов студии её нет, и это осознанное
/// исключение. Настройки и список недавних проектов — плоские мешки: незнакомое поле
/// безвредно, потерянное стоит одного щелчка. Раскладка — дерево со смыслом, и
/// читатель, молча принявший чужие доли, отдаёт человеку сломанное рабочее место,
/// которое собирают обратно десятком перетаскиваний. Поэтому файл новее известного
/// не читается вовсе: лучше стандартная раскладка и строка в журнале, чем догадки.
/// </remarks>
public sealed class DockLayout
{
    /// <summary>Версия формата, которую понимает эта студия.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Имя раскладки, с которой студия начинает.</summary>
    public const string DefaultName = "default";

    /// <summary>Версия формата этого файла.</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Имя показанного набора.</summary>
    public string Active { get; init; } = DefaultName;

    /// <summary>Наборы по именам.</summary>
    public IReadOnlyDictionary<string, DockWorkspace> Layouts { get; init; } =
        new Dictionary<string, DockWorkspace>(StringComparer.Ordinal);

    /// <summary>Показанный набор; если такого имени нет — первый попавшийся.</summary>
    /// <remarks>
    /// Вычисляется, а потому в файл не едет: записанный набор при чтении лёг бы
    /// мёртвым дубликатом рядом с настоящими.
    /// </remarks>
    [JsonIgnore]
    public DockWorkspace? Current =>
        Layouts.TryGetValue(Active, out var workspace) ? workspace : Layouts.Values.FirstOrDefault();
}
