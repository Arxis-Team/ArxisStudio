using System.Globalization;
using ArxisStudio.Markup.Xaml;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Designer;

/// <summary>
/// Переводит жест на канве в свойства разметки.
/// </summary>
/// <remarks>
/// Канва сообщает о правке прямоугольником «было — стало» в своих координатах:
/// форма лежит на ней в каком-то месте, и точка отсчёта у канвы своя. В разметке
/// же написано, чего элемент просит у своего родителя, и одно и то же смещение
/// записывается по-разному в зависимости от того, кто этот родитель. Здесь и
/// живёт этот перевод.
/// <para>
/// Смещение между двумя прямоугольниками от точки отсчёта не зависит, поэтому
/// новое положение считается как старое плюс смещение — а старое берётся из
/// документа, а не с контрола: контрол канва уже подвинула, и в его свойствах
/// лежат её координаты, а не координаты его родителя.
/// </para>
/// </remarks>
public static class GeometryWriter
{
    /// <summary>
    /// Собирает свойства, которыми записывается новая геометрия элемента.
    /// </summary>
    /// <param name="node">Узел, чей контрол двигали или растягивали.</param>
    /// <param name="oldBounds">Прямоугольник до жеста, в координатах канвы.</param>
    /// <param name="newBounds">Прямоугольник после жеста, в координатах канвы.</param>
    /// <returns>Пары «свойство — значение»; пусто, если писать нечего.</returns>
    public static IReadOnlyList<(string Name, string? Text)> Describe(HierarchyNode node, Rect oldBounds, Rect newBounds)
    {
        ArgumentNullException.ThrowIfNull(node);

        var values = new List<(string, string?)>();
        var target = node.Control;

        // Размер от точки отсчёта не зависит, поэтому берётся с контрола: канва
        // уже учла и привязку к сетке, и минимальный размер самого контрола.
        if (!Same(oldBounds.Width, newBounds.Width))
            values.Add(("Width", Number(target is null || double.IsNaN(target.Width) ? newBounds.Width : target.Width)));

        if (!Same(oldBounds.Height, newBounds.Height))
            values.Add(("Height", Number(target is null || double.IsNaN(target.Height) ? newBounds.Height : target.Height)));

        var dx = newBounds.X - oldBounds.X;
        var dy = newBounds.Y - oldBounds.Y;

        if (Same(dx, 0) && Same(dy, 0))
            return values;

        // На Canvas положение — это свойства самого Canvas; в остальных
        // раскладках элемент двигается только отступом, потому что место ему
        // отводит родитель.
        if (target?.Parent is Canvas)
        {
            values.Add(("Canvas.Left", Number(Read(node, "Canvas.Left") + dx)));
            values.Add(("Canvas.Top", Number(Read(node, "Canvas.Top") + dy)));
            return values;
        }

        var margin = ReadThickness(node, "Margin");

        values.Add(("Margin", Text(new Thickness(
            margin.Left + dx,
            margin.Top + dy,
            margin.Right - dx,
            margin.Bottom - dy))));

        return values;
    }

    /// <summary>Читает число, записанное в разметке; ноль, если его там нет.</summary>
    private static double Read(HierarchyNode node, string name) =>
        node.Element.GetAttribute(XamlQualifiedName.Parse(name))?.GetValueText() is { } text &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    /// <summary>Читает отступ, записанный в разметке; нулевой, если его там нет.</summary>
    private static Thickness ReadThickness(HierarchyNode node, string name)
    {
        var text = node.Element.GetAttribute(XamlQualifiedName.Parse(name))?.GetValueText();

        if (string.IsNullOrWhiteSpace(text))
            return default;

        try
        {
            return Thickness.Parse(text);
        }
        catch (FormatException)
        {
            return default;
        }
    }

    /// <summary>
    /// Совпадают ли величины с точностью, различимой на экране.
    /// </summary>
    /// <remarks>
    /// Раскладка считает в дробных величинах, и жест, ничего не сдвинувший,
    /// всё равно даёт разницу в сотых. Записанная в разметку, она превратилась
    /// бы в правку, которой человек не делал.
    /// </remarks>
    private static bool Same(double left, double right) => Math.Abs(left - right) < 0.5;

    private static string Number(double value) =>
        Math.Round(value, 1).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Text(Thickness value) =>
        $"{Number(value.Left)},{Number(value.Top)},{Number(value.Right)},{Number(value.Bottom)}";
}
