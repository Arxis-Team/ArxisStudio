using ArxisStudio.Docking;
using ArxisStudio.Sdk.Plugins;

namespace ArxisStudio.Services;

/// <summary>
/// Куда раскладка ставит панель по пожеланию манифеста и с чего она начинается.
/// </summary>
/// <remarks>
/// Чистые правила над деревом: ни видов, ни окон, ни каретки. Вынесены из <see cref="StudioDock"/>,
/// чтобы правило места читалось отдельно от того, как раскладка живёт на экране.
/// </remarks>
internal static class DockPlacement
{
    /// <summary>
    /// Раскладка, с которой студия начинает.
    /// </summary>
    /// <remarks>
    /// Доли взяты с прежней оболочки, где они были зашиты в шаблон: 262 и 302
    /// пикселя по краям от полутора тысяч ширины и 212 снизу. Стороны заведены
    /// заранее и пустыми: пока в них никто не встал, вид их не показывает, зато
    /// пришедшая панель попадает в место с готовым размером, а не делит пополам
    /// область документов.
    /// </remarks>
    public static DockNode Skeleton() => new DockSplit
    {
        Orientation = DockOrientation.Horizontal,
        Weights = [0.18, 0.60, 0.22],
        Children =
        [
            new DockGroup { Id = "left" },
            new DockSplit
            {
                Orientation = DockOrientation.Vertical,
                Weights = [0.74, 0.26],
                Children = [new DockGroup { Id = StudioDock.Documents }, new DockGroup { Id = "bottom" }],
            },
            new DockGroup { Id = "right" },
        ],
    };

    /// <summary>
    /// Ставит панель туда, куда она просилась.
    /// </summary>
    /// <param name="root">Дерево.</param>
    /// <param name="id">Имя панели.</param>
    /// <param name="where">Пожелание из манифеста.</param>
    /// <param name="home">Нынешний дом документов.</param>
    /// <param name="standing">Группы, которые не сносятся, даже опустев.</param>
    /// <returns>Новое дерево.</returns>
    /// <remarks>
    /// Соседство сильнее стороны: «встань рядом с деревом решения» — пожелание
    /// точное, и спрашивать после него про сторону незачем. Названного соседа
    /// может не быть на экране вовсе — плагин не поставили или выключили, —
    /// и тогда работает сторона.
    /// <para>
    /// Долю слушают только у первой панели на пустой стороне. У занятой размер
    /// уже есть — его дал сосед или мышь человека, — и отбирать его новичок не
    /// вправе. У дома документов не слушают вовсе: это не сторона, которую
    /// заводят под панель, а область, что была в окне до неё.
    /// </para>
    /// </remarks>
    public static DockNode Place(
        DockNode root,
        string id,
        PluginPlacement where,
        string home,
        IReadOnlySet<string> standing)
    {
        if (where.Near is { Length: > 0 } near && DockTree.Holder(root, near) is { } neighbour)
            return DockTree.Attach(root, neighbour.Id, id);

        var side = where.Side.ToLowerInvariant();

        // «В центр» указывает на дом документов, где бы он ни был: имя группы
        // задаёт файл раскладки, и слово «documents» может не значить в ней
        // ничего. Иначе документ, вернувшийся из закрытого окна, заводил бы себе
        // одноимённую группу у правого края и оставался в ней навсегда. Слов два:
        // «center» — для манифеста, «documents» — внутреннее имя, которым уже
        // пользуются чужие манифесты и файлы раскладки.
        var centre = string.Equals(side, StudioDock.Center, StringComparison.Ordinal)
            || string.Equals(side, StudioDock.Documents, StringComparison.Ordinal);

        if (centre)
            side = home;

        if (DockTree.Group(root, side) is not { } waiting)
        {
            return DockTree.Widen(
                DockTree.Insert(root, Anchor(root, home), Side(side), id, side), side, where.Size, standing);
        }

        var next = DockTree.Attach(root, side, id);

        return waiting.Items.Count == 0 && !centre
            ? DockTree.Widen(next, side, where.Size, standing)
            : next;
    }

    /// <summary>От какой группы отмерять место для новой: от дома документов, а нет его — от первой.</summary>
    /// <param name="root">Дерево.</param>
    /// <param name="home">Нынешний дом документов.</param>
    public static string Anchor(DockNode root, string home) =>
        DockTree.Group(root, home)?.Id ?? root.Groups().First().Id;

    /// <summary>Сторона по названию; незнакомое слово уводит вправо.</summary>
    private static DockSide Side(string side) => side switch
    {
        "left" => DockSide.Left,
        "top" => DockSide.Top,
        "bottom" => DockSide.Bottom,
        _ => DockSide.Right,
    };
}
