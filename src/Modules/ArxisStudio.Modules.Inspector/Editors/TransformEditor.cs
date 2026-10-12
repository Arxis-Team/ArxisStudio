using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Sdk;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>
/// Редактор преобразования полями, как Transform у Unity: сдвиг, поворот, масштаб и наклон — строками под подписью.
/// </summary>
/// <remarks>
/// <para>
/// Поля пишут запись Avalonia — <c>translate(10px, 20px) rotate(45deg) scale(1.5) skew(10deg, 0deg)</c> (
/// <see cref="TransformText"/>); тождество снимает атрибут. Протяжку и шаг любого поля видно на цели на ходу, а пишется
/// одно значение при отпускании (<see cref="FieldPreview"/>). Масштаб можно связать: правка одной оси ставит её число в
/// обе.
/// </para>
/// <para>
/// Запись, которую поля не передадут, — <c>matrix</c>, повтор, другой порядок — стоит текстом, и правят её текстом;
/// «Изменить как текст» открывает текстом и запись полей. Заданное элементом — только чтение, как всякое значение
/// элементом.
/// </para>
/// </remarks>
[PropertyEditor(typeof(ITransform))]
public sealed class TransformEditor : PropertyEditor
{
    /// <inheritdoc/>
    public override InspectorFieldLayout LayoutOf(InspectorProperty property) => InspectorFieldLayout.Below;

    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TransformField(context, Context.Strings);
    }

    private sealed class TransformField : InspectorField, ITextualField
    {
        private readonly InspectorFieldContext _context;
        private readonly FieldPreview _preview;
        private readonly AxVectorField _translate;
        private readonly AxNumberField _rotate;
        private readonly AxVectorField _scale;
        private readonly AxVectorField _skew;
        private readonly StackPanel _rows = new();
        private readonly TextField _text;
        private readonly Grid _view = new();
        private TransformParts _parts = TransformParts.Identity;
        private bool _readable = true;
        private bool _textual;

        public TransformField(InspectorFieldContext context, IStudioStrings strings)
        {
            _context = context;
            _preview = new FieldPreview(context);
            _text = new TextField(context, multiline: false);

            _translate = Vector(strings["inspector.transform.translate"], step: 1, link: false);
            _rotate = new AxNumberField { Size = AxControlSize.Compact, Step = 1 };
            _scale = Vector(strings["inspector.transform.scale"], step: 0.1, link: true);
            _skew = Vector(strings["inspector.transform.skew"], step: 1, link: false);

            AutomationProperties.SetName(_rotate, strings["inspector.transform.rotate"]);

            // Строки — шагом строк списка: поле и зазор, как у соседних строк инспектора.
            _rows.Bind(StackPanel.SpacingProperty, _rows.GetResourceObservable("AxSpaceTight"));
            _rows.Children.Add(Row(strings["inspector.transform.translate"], _translate));
            _rows.Children.Add(Row(strings["inspector.transform.rotate"], _rotate, _rotate));
            _rows.Children.Add(Row(strings["inspector.transform.scale"], _scale));
            _rows.Children.Add(Row(strings["inspector.transform.skew"], _skew));

            _view.Children.Add(_rows);
            _view.Children.Add(_text.View);

            Wire(_translate, static (parts, values) => parts with { X = values[0], Y = values[1] });
            Wire(_scale, static (parts, values) => parts with { ScaleX = values[0], ScaleY = values[1] });
            Wire(_skew, static (parts, values) => parts with { SkewX = values[0], SkewY = values[1] });

            _rotate.ValueChanging += (_, e) => Preview(_parts with { Angle = e.Value });
            _rotate.ValueCommitted += (_, e) => Commit(_parts with { Angle = e.Value });
            _rotate.ScrubCancelled += (_, _) => _preview.Cancel();

            // Текст, открытый «Изменить как текст», уходит с каретки: записанное покажется полями, если они его передадут.
            _text.View.LostFocus += (_, _) => Dispatcher.UIThread.Post(Settle, DispatcherPriority.Background);

            ShowMode();
        }

        public override Control View => _view;

        public override Control? FocusTarget => Textual ? _text.FocusTarget : _translate.Cells.Count > 0 ? _translate.Cells[0] : _translate;

        public override bool HasDraft => Textual
            ? _text.HasDraft
            : _translate.Cells.Concat(_scale.Cells).Concat(_skew.Cells).Append(_rotate).Any(static cell => cell.HasDraft);

        /// <summary>Стоит ли запись текстом: поля её не передадут или её открыли текстом.</summary>
        private bool Textual => _textual || !_readable;

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            // Набранное с кареткой не затирается: значение покажется снова, когда набранное запишут.
            if (HasDraft && _view.IsKeyboardFocusWithin)
                return;

            _readable = TransformText.TryRead(value.IsMixed ? null : TextField.Written(value), out var parts) || value.IsMixed;
            _parts = parts;

            var mixed = value.IsMixed ? 0b11 : 0;

            _translate.Values = [parts.X, parts.Y];
            _translate.MixedMask = mixed;
            _rotate.Value = parts.Angle;
            _rotate.IsMixed = value.IsMixed;
            _scale.Values = [parts.ScaleX, parts.ScaleY];
            _scale.MixedMask = mixed;
            _skew.Values = [parts.SkewX, parts.SkewY];
            _skew.MixedMask = mixed;
            _text.Show(value);
            ShowMode();
        }

        public bool EditAsText()
        {
            if (_context.Property.IsReadOnly)
                return false;

            _textual = true;
            ShowMode();
            Dispatcher.UIThread.Post(() => _text.FocusTarget?.Focus(Avalonia.Input.NavigationMethod.Tab), DispatcherPriority.Loaded);

            return true;
        }

        public override void Release()
        {
            _preview.Cancel();
            _text.Release();
        }

        /// <summary>Строка поля под подписью строки свойства: подпись колонкой списка, ступенью глубже.</summary>
        private static AxPropertyRow Row(string label, Control field, AxNumberField? scrub = null) => new()
        {
            Label = label,
            Indent = 1,
            Content = field,
            ScrubTarget = scrub,
        };

        private static AxVectorField Vector(string name, double step, bool link)
        {
            var vector = new AxVectorField
            {
                Size = AxControlSize.Compact,
                Labels = ["X", "Y"],
                Step = step,
                CanLink = link,
            };

            AutomationProperties.SetName(vector, name);

            return vector;
        }

        /// <summary>Правка вектора — в запись целиком: остальные поля берутся показанными.</summary>
        private void Wire(AxVectorField vector, Func<TransformParts, IReadOnlyList<double>, TransformParts> apply)
        {
            vector.ValuesChanging += (_, e) => Preview(apply(_parts, e.Values));
            vector.ValuesCommitted += (_, e) => Commit(apply(_parts, e.Values));
            vector.ScrubCancelled += (_, _) => _preview.Cancel();
        }

        /// <summary>Протянутое — на цель, без записи; тождество цель видит как «нет преобразования».</summary>
        private void Preview(TransformParts parts) => _preview.Show(TransformText.Write(parts) ?? "none");

        /// <summary>Поля — в запись одной правкой; тождество снимает атрибут.</summary>
        private void Commit(TransformParts parts)
        {
            _parts = parts;
            _ = _preview.CommitAsync(TransformText.Write(parts));
        }

        /// <summary>Текст, открытый по просьбе, закрывается, когда каретка ушла и набранного нет.</summary>
        private void Settle()
        {
            if (!_textual || _text.HasDraft || _text.View.IsKeyboardFocusWithin)
                return;

            _textual = false;
            ShowMode();
        }

        private void ShowMode()
        {
            _rows.IsVisible = !Textual;
            _text.View.IsVisible = Textual;
        }
    }
}
