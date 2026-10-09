using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>
/// Редактор вектора: отступ и скругление, точка, размер, прямоугольник — ячейками с подписями, как у Unity.
/// </summary>
/// <remarks>
/// Пишется кратчайшая запись, которую прочтёт разметка: отступ <c>8</c>, <c>4,8</c> или все четыре стороны,
/// скругление — одно число или четыре. Отступ и скругление можно связать: правка одной ячейки ставит её число во
/// все. Читается запись тем же разбором, что у Avalonia.
/// </remarks>
[PropertyEditor(typeof(Thickness), Fallback = true)]
[PropertyEditor(typeof(CornerRadius), Fallback = true)]
[PropertyEditor(typeof(Point), Fallback = true)]
[PropertyEditor(typeof(Size), Fallback = true)]
[PropertyEditor(typeof(Rect), Fallback = true)]
[PropertyEditor(typeof(Vector), Fallback = true)]
public sealed class VectorEditor : PropertyEditor
{
    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new VectorField(context, Shape.Of(context.Property.ValueType.FullName));
    }

    /// <summary>Запись вектора: подписи ячеек, разбор и кратчайшая запись.</summary>
    internal abstract class Shape
    {
        public abstract IReadOnlyList<string> Labels { get; }

        public virtual bool CanLink => false;

        public static Shape Of(string typeName) => typeName switch
        {
            "Avalonia.Thickness" => new ThicknessShape(),
            "Avalonia.CornerRadius" => new CornerShape(),
            "Avalonia.Rect" => new Plain(["X", "Y", "W", "H"], text => Rect.Parse(text) is var rect ? [rect.X, rect.Y, rect.Width, rect.Height] : []),
            "Avalonia.Size" => new Plain(["W", "H"], text => Size.Parse(text) is var size ? [size.Width, size.Height] : []),
            _ => new Plain(["X", "Y"], text => Point.Parse(text) is var point ? [point.X, point.Y] : []),
        };

        /// <summary>Составляющие записи; не прочлось — пусто.</summary>
        public double[] Read(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return [];

            try
            {
                return Parse(text);
            }
            catch (FormatException)
            {
                return [];
            }
        }

        /// <summary>Кратчайшая запись составляющих.</summary>
        public abstract string Write(IReadOnlyList<double> values);

        protected abstract double[] Parse(string text);

        protected static string Join(IEnumerable<double> values) =>
            string.Join(",", values.Select(value => AxNumberField.Format(value, 15)));
    }

    private sealed class ThicknessShape : Shape
    {
        public override IReadOnlyList<string> Labels => ["L", "T", "R", "B"];

        public override bool CanLink => true;

        public override string Write(IReadOnlyList<double> values)
        {
            if (values.All(value => value.Equals(values[0])))
                return Join([values[0]]);

            return values[0].Equals(values[2]) && values[1].Equals(values[3])
                ? Join([values[0], values[1]])
                : Join(values);
        }

        protected override double[] Parse(string text) =>
            Thickness.Parse(text) is var thickness ? [thickness.Left, thickness.Top, thickness.Right, thickness.Bottom] : [];
    }

    private sealed class CornerShape : Shape
    {
        public override IReadOnlyList<string> Labels => ["TL", "TR", "BR", "BL"];

        public override bool CanLink => true;

        public override string Write(IReadOnlyList<double> values) =>
            values.All(value => value.Equals(values[0])) ? Join([values[0]]) : Join(values);

        protected override double[] Parse(string text) =>
            CornerRadius.Parse(text) is var corners ? [corners.TopLeft, corners.TopRight, corners.BottomRight, corners.BottomLeft] : [];
    }

    private sealed class Plain(IReadOnlyList<string> labels, Func<string, double[]> parse) : Shape
    {
        public override IReadOnlyList<string> Labels => labels;

        public override string Write(IReadOnlyList<double> values) => Join(values);

        protected override double[] Parse(string text) => parse(text);
    }

    private sealed class VectorField : InspectorField
    {
        private readonly InspectorFieldContext _context;
        private readonly Shape _shape;
        private readonly AxVectorField _field;

        public VectorField(InspectorFieldContext context, Shape shape)
        {
            _context = context;
            _shape = shape;
            _field = new AxVectorField
            {
                Size = AxControlSize.Compact,
                Labels = shape.Labels,
                CanLink = shape.CanLink,
                IsReadOnly = context.Property.IsReadOnly,
                Minimum = context.Property.Constraints.Minimum ?? double.NegativeInfinity,
                Maximum = context.Property.Constraints.Maximum ?? double.PositiveInfinity,
                Step = context.Property.Constraints.Step ?? 1,
                Decimals = context.Property.Constraints.Decimals,
            };

            Avalonia.Automation.AutomationProperties.SetName(_field, context.Property.Label);
            _field.ValuesCommitted += (_, e) => _ = _context.WriteAsync(_shape.Write(e.Values));
        }

        public override Control View => _field;

        public override Control? FocusTarget => _field.Cells.Count > 0 ? _field.Cells[0] : _field;

        public override bool HasDraft => _field.Cells.Any(cell => cell.HasDraft);

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            // Набранное в ячейке с кареткой не затирается: значение покажется снова, когда набранное запишут.
            if (HasDraft && _field.IsKeyboardFocusWithin)
                return;

            var values = _shape.Read(value.Text);

            _field.Values = values.Length == _shape.Labels.Count ? values : new double[_shape.Labels.Count];
            _field.MixedMask = value.IsMixed ? (1 << _shape.Labels.Count) - 1 : 0;
        }
    }
}
