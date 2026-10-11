using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>
/// Редактор числа в границах — ползунком и полем рядом, как <c>[Range]</c> у Unity: подсказка <see cref="Hint"/>.
/// </summary>
/// <remarks>
/// <para>
/// Подсказку ставят метаданные — <c>Opacity</c> у таблицы Avalonia — и источники: дизайнер у свойства проекта, обе
/// границы которого из <c>[Range]</c>, и объект плагина. Свойство без конечных границ редактор отдаёт числовому полю:
/// тянуть по дорожке без концов нечего.
/// </para>
/// <para>
/// Протяжку ползунка видно на цели на каждом движении, а пишется одно значение при отпускании; Esc возвращает цель к
/// тому, что было (<see cref="FieldPreview"/>). Шаг клавишей и набранное в поле — по одной записи. Ползунок идёт
/// делениями шага метаданных, а без шага — сотой долей диапазона, у целых — единицей.
/// </para>
/// </remarks>
[PropertyEditor(typeof(double), Hint = Hint)]
[PropertyEditor(typeof(float), Hint = Hint)]
[PropertyEditor(typeof(decimal), Hint = Hint)]
[PropertyEditor(typeof(int), Hint = Hint)]
[PropertyEditor(typeof(long), Hint = Hint)]
[PropertyEditor(typeof(short), Hint = Hint)]
[PropertyEditor(typeof(byte), Hint = Hint)]
[PropertyEditor(typeof(uint), Hint = Hint)]
[PropertyEditor(typeof(ulong), Hint = Hint)]
[PropertyEditor(typeof(ushort), Hint = Hint)]
[PropertyEditor(typeof(sbyte), Hint = Hint)]
public sealed class SliderEditor : PropertyEditor
{
    /// <summary>Подсказка редактора: число в границах — ползунком.</summary>
    public const string Hint = "slider";

    /// <inheritdoc/>
    /// <remarks>Число с конечными границами: без них дорожке не на чем стоять.</remarks>
    public override bool Accepts(InspectorProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return NumberEditor.IsNumber(property.ValueType) &&
               property.Constraints is { Minimum: { } minimum, Maximum: { } maximum } &&
               double.IsFinite(minimum) && double.IsFinite(maximum) && maximum > minimum;
    }

    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new SliderField(context);
    }

    private sealed class SliderField : InspectorField
    {
        private readonly InspectorFieldContext _context;
        private readonly AxRangeField _field;
        private readonly FieldPreview _preview;

        public SliderField(InspectorFieldContext context)
        {
            _context = context;
            _preview = new FieldPreview(context);

            var property = context.Property;
            var constraints = property.Constraints;
            var whole = NumberEditor.IsWhole(property.ValueType);
            var minimum = constraints.Minimum ?? 0;
            var maximum = constraints.Maximum ?? 1;

            _field = new AxRangeField
            {
                Size = AxControlSize.Compact,
                Minimum = minimum,
                Maximum = maximum,
                Step = constraints.Step ?? (whole ? 1 : (maximum - minimum) / 100),
                Decimals = whole ? 0 : constraints.Decimals,
                IsReadOnly = property.IsReadOnly,
            };

            _field.ValueChanging += (_, e) => _preview.Show(TextOf(e.Value));
            _field.ValueCommitted += (_, e) => _ = _preview.CommitAsync(TextOf(e.Value));
            _field.ScrubCancelled += (_, _) => _preview.Cancel();
            _field.Cleared += (_, _) => _ = _context.WriteAsync(null);
        }

        public override Control View => _field;

        // Enter и F2 ставят каретку в число: в поле набирают, а ползунок тянут мышью и стрелками.
        public override Control? FocusTarget => _field.NumberField;

        public override AxNumberField? ScrubTarget => _context.Property.IsReadOnly ? null : _field.NumberField;

        public override bool HasDraft => _field.HasDraft;

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            // Протягиваемое и набранное в поле с кареткой не затираются: значение покажется, когда их запишут.
            if (_field.IsScrubbing || (HasDraft && _field.IsKeyboardFocusWithin))
                return;

            _field.Value = NumberEditor.TryRead(value.Text, out var number) && double.IsFinite(number) ? number : 0;
            _field.IsMixed = value.IsMixed;
        }

        public override void Release() => _preview.Cancel();

        /// <summary>Число текстом разметки: инвариантно, со знаками поля.</summary>
        private string TextOf(double value) => AxNumberField.Format(value, _field.Decimals ?? 15);
    }
}
