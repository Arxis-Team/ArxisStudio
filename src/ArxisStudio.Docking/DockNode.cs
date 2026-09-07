using System.Text.Json.Serialization;

namespace ArxisStudio.Docking;

/// <summary>
/// Куда растёт деление: вдоль или поперёк.
/// </summary>
public enum DockOrientation
{
    /// <summary>Дети стоят слева направо.</summary>
    Horizontal,

    /// <summary>Дети стоят сверху вниз.</summary>
    Vertical,
}

/// <summary>
/// Узел раскладки: либо деление, либо группа вкладок.
/// </summary>
/// <remarks>
/// Дерево — чистые данные, и это его главное свойство. Оно переживает выключение
/// плагина, уезжает в файл и возвращается при следующем запуске; узел, удержавший
/// живой контрол, не дал бы контексту плагина выгрузиться никогда. Поэтому в дереве
/// лежат идентификаторы, а сами контролы — в отдельном регистре, откуда их
/// снимают по владельцу в момент ухода.
/// <para>
/// Узлы неизменяемы, а правки живут в <see cref="DockTree"/> чистыми функциями,
/// возвращающими новое дерево. Так их проверяют без единого контрола и без окна.
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DockSplit), "split")]
[JsonDerivedType(typeof(DockGroup), "group")]
public abstract class DockNode
{
    /// <summary>Все группы этого поддерева, сверху вниз.</summary>
    public abstract IEnumerable<DockGroup> Groups();
}

/// <summary>
/// Деление: несколько узлов в ряд или в столбец.
/// </summary>
/// <remarks>
/// Деление n-арное, а не парное. Четыре области в ряд — это один узел с четырьмя
/// детьми, а не три вложенных пары: так тянется любая граница, а не только соседняя,
/// и так же устроено дерево у Unity.
/// </remarks>
public sealed class DockSplit : DockNode
{
    /// <summary>Куда растёт деление.</summary>
    public DockOrientation Orientation { get; init; }

    /// <summary>Дети по порядку.</summary>
    public IReadOnlyList<DockNode> Children { get; init; } = [];

    /// <summary>
    /// Доли детей — в том же порядке.
    /// </summary>
    /// <remarks>
    /// Доли, а не пиксели, и сумма приводится к единице. Между сеансами меняются
    /// размер окна, монитор и масштаб; ширина, снятая с прошлого монитора, поставила
    /// бы панель во весь экран.
    /// </remarks>
    public IReadOnlyList<double> Weights { get; init; } = [];

    /// <inheritdoc/>
    public override IEnumerable<DockGroup> Groups() => Children.SelectMany(child => child.Groups());
}

/// <summary>
/// Группа вкладок: несколько панелей в одном месте, видна одна.
/// </summary>
public sealed class DockGroup : DockNode
{
    /// <summary>
    /// Имя группы — по нему на неё ссылаются.
    /// </summary>
    /// <remarks>
    /// Имя нужно трём вещам: указателю на область документов, пожеланию плагина
    /// «встань рядом с той панелью» и переиспользованию уже построенного вида при
    /// перекладке — иначе контрол панели пересоздавался бы на каждое движение.
    /// </remarks>
    public string Id { get; init; } = string.Empty;

    /// <summary>Идентификаторы панелей по порядку вкладок.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>
    /// Какая вкладка выбрана; null — группа пуста.
    /// </summary>
    /// <remarks>
    /// Идентификатор, а не номер. Номер разъезжается, стоит отсеять неизвестную
    /// панель, — ровно та болезнь, от которой умирает связь документов со своими
    /// вкладками в нынешней оболочке, и повторять её на новом месте незачем.
    /// </remarks>
    public string? Selected { get; init; }

    /// <inheritdoc/>
    public override IEnumerable<DockGroup> Groups() => [this];
}
