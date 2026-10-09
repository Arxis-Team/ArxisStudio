using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>
/// Редактор числа: поле с протяжкой за подпись строки, шагом стрелками и выражениями; «авто» — у размеров.
/// </summary>
/// <remarks>
/// Целые типы — без знаков после точки и с шагом в единицу. «Авто» пишется сбросом, когда по умолчанию свойство и
/// так «авто» (<c>Width</c>), — так разметка остаётся без лишнего <c>Width="NaN"</c>; иначе — <c>NaN</c>. Пустое поле
/// у числа без «авто» — тоже сброс: значение просят снять.
/// </remarks>
[PropertyEditor(typeof(double), Fallback = true)]
[PropertyEditor(typeof(float), Fallback = true)]
[PropertyEditor(typeof(decimal), Fallback = true)]
[PropertyEditor(typeof(int), Fallback = true)]
[PropertyEditor(typeof(long), Fallback = true)]
[PropertyEditor(typeof(short), Fallback = true)]
[PropertyEditor(typeof(byte), Fallback = true)]
[PropertyEditor(typeof(uint), Fallback = true)]
[PropertyEditor(typeof(ulong), Fallback = true)]
[PropertyEditor(typeof(ushort), Fallback = true)]
[PropertyEditor(typeof(sbyte), Fallback = true)]
public sealed class NumberEditor : PropertyEditor
{
    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new NumberField(context);
    }

    /// <summary>Целый ли тип значения.</summary>
    /// <param name="type">Тип.</param>
    internal static bool IsWhole(InspectorTypeInfo type) => type.FullName is
        "System.Int32" or "System.Int64" or "System.Int16" or "System.Byte" or
        "System.UInt32" or "System.UInt64" or "System.UInt16" or "System.SByte";

    private sealed class NumberField : InspectorField
    {
        private readonly InspectorFieldContext _context;
        private readonly AxNumberField _field;
        private readonly bool _autoIsDefault;

        public NumberField(InspectorFieldContext context)
        {
            _context = context;

            var property = context.Property;
            var constraints = property.Constraints;
            var whole = IsWhole(property.ValueType);

            _field = new AxNumberField
            {
                Size = AxControlSize.Compact,
                AllowsAuto = constraints.AllowsAuto,
                Minimum = constraints.Minimum ?? double.NegativeInfinity,
                Maximum = constraints.Maximum ?? double.PositiveInfinity,
                Step = constraints.Step ?? 1,
                Decimals = whole ? 0 : constraints.Decimals,
                IsReadOnly = property.IsReadOnly,
            };

            _autoIsDefault = TryRead(property.DefaultText, out var fallback) && double.IsNaN(fallback);
            _field.ValueCommitted += (_, e) => _ = WriteAsync(e.Value);
            _field.Cleared += (_, _) => _ = _context.WriteAsync(null);
        }

        public override Control View => _field;

        public override Control? FocusTarget => _field;

        public override AxNumberField? ScrubTarget => _context.Property.IsReadOnly ? null : _field;

        public override bool HasDraft => _field.HasDraft;

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            // Набранное в поле с кареткой не затирается: значение покажется снова, когда набранное запишут.
            if (HasDraft && _field.IsKeyboardFocusWithin)
                return;

            _field.Value = TryRead(value.Text, out var number) ? number : 0;
            _field.IsMixed = value.IsMixed;
        }

        /// <summary>Читает число записи разметки: инвариантно, «авто» — <c>NaN</c>.</summary>
        internal static bool TryRead(string? text, out double value)
        {
            value = double.NaN;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.Trim();

            if (string.Equals(trimmed, "Auto", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "NaN", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private Task<bool> WriteAsync(double value)
        {
            if (double.IsNaN(value))
                return _context.WriteAsync(_autoIsDefault ? null : "NaN");

            return _context.WriteAsync(AxNumberField.Format(value, _field.Decimals ?? 15));
        }
    }
}
