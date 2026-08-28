using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Arxis.HelloPlugin;

/// <summary>
/// Рисовальщик отступов: четыре поля вместо строки «0,0,0,0».
/// </summary>
/// <remarks>
/// Пример того, ради чего рисовальщики и нужны: значение из четырёх чисел
/// правится по одному числу, а не пересчитывается человеком в уме каждый раз,
/// когда нужно подвинуть край.
/// </remarks>
[PropertyDrawer(typeof(Thickness))]
public sealed class ThicknessDrawer : PropertyDrawer
{
    private static readonly string[] Sides = ["Слева", "Сверху", "Справа", "Снизу"];

    /// <inheritdoc/>
    public override Control Build(IPropertyContext property)
    {
        ArgumentNullException.ThrowIfNull(property);

        var boxes = new AxTextBox[4];
        var updating = false;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        for (var side = 0; side < boxes.Length; side++)
        {
            var box = new AxTextBox { Width = 38, FontSize = 11.5 };

            ToolTip.SetTip(box, Sides[side]);

            box.LostFocus += (_, _) => Write();
            boxes[side] = box;
            row.Children.Add(box);
        }

        Read();
        property.Changed += (_, _) => Read();

        return row;

        void Read()
        {
            updating = true;

            try
            {
                var parts = Split(property.Value ?? property.Effective);

                for (var side = 0; side < boxes.Length; side++)
                    boxes[side].Text = parts[side];
            }
            finally
            {
                updating = false;
            }
        }

        void Write()
        {
            if (updating)
                return;

            var parts = boxes.Select(box => Number(box.Text)).ToArray();

            // Пустые поля означают «значения нет»: так свойство сбрасывается,
            // не заставляя человека искать отдельную кнопку.
            property.Set(parts.All(string.IsNullOrEmpty) ? null : string.Join(',', parts.Select(part => part.Length == 0 ? "0" : part)));
        }
    }

    /// <summary>
    /// Разбирает записанное значение на четыре стороны.
    /// </summary>
    /// <remarks>
    /// В разметке отступ пишут одним числом, двумя или четырьмя, и все три
    /// записи законны — поля должны показать то же, что показала бы раскладка.
    /// </remarks>
    private static string[] Split(string? text)
    {
        var parts = (text ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            1 => [parts[0], parts[0], parts[0], parts[0]],
            2 => [parts[0], parts[1], parts[0], parts[1]],
            4 => parts,
            _ => ["", "", "", ""],
        };
    }

    private static string Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value.ToString("0.##", CultureInfo.InvariantCulture)
            : string.Empty;
}
