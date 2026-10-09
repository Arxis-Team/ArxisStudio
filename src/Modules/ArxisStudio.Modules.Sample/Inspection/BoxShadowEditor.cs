using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace ArxisStudio.Modules.Sample.Inspection;

/// <summary>
/// Редактор тени (образец): смещение, размытие и разлёт первой тени — ячейками с протяжкой, цвет — полем цвета.
/// </summary>
/// <remarks>
/// <para>
/// Образец того, как плагин дополняет инспектор своим редактором — как <c>PropertyDrawer</c> у Unity. Класс,
/// помеченный <see cref="PropertyEditorAttribute"/>, студия находит сама, строит через шов сбоев и снимает с
/// выгрузкой; поле у каждой строки своё (<see cref="CreateField"/>) и пишет только через свой контекст — цель оно не
/// видит.
/// </para>
/// <para>
/// Протяжку ячеек и тягу цвета поле показывает на цели предпросмотром (<see cref="InspectorFieldContext.BeginPreview"/>),
/// а пишет одним значением при отпускании; Esc — отмена. Цель без предпросмотра получает запись при отпускании.
/// </para>
/// <para>
/// Тень пишется так, как её читает разметка: <c>2 4 8 0 #40000000</c>, с <c>inset</c> впереди у внутренней; вторая и
/// дальше остаются как были.
/// </para>
/// </remarks>
[PropertyEditor(typeof(BoxShadows))]
public sealed class BoxShadowEditor : PropertyEditor
{
    /// <inheritdoc/>
    public override InspectorFieldLayout LayoutOf(InspectorProperty property) => InspectorFieldLayout.Below;

    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new ShadowField(context);
    }

    private sealed class ShadowField : InspectorField
    {
        private readonly InspectorFieldContext _context;
        private readonly AxVectorField _numbers;
        private readonly AxColorField _color;
        private readonly DockPanel _view;
        private bool _inset;
        private string _rest = string.Empty;
        private IInspectorPreview? _preview;

        public ShadowField(InspectorFieldContext context)
        {
            _context = context;

            var readOnly = context.Property.IsReadOnly;

            _numbers = new AxVectorField
            {
                Size = AxControlSize.Compact,
                Labels = ["X", "Y", "Blur", "Spread"],
                Decimals = 0,
                IsReadOnly = readOnly,
                [AutomationProperties.NameProperty] = context.Property.Label,
            };
            _color = new AxColorField
            {
                Size = AxControlSize.Compact,
                AllowsAlpha = true,
                IsReadOnly = readOnly,
                VerticalAlignment = VerticalAlignment.Top,
                [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxGapIconTextThickness"),
            };
            _view = new DockPanel { LastChildFill = true };

            DockPanel.SetDock(_color, Dock.Right);
            _view.Children.Add(_color);
            _view.Children.Add(_numbers);

            _numbers.ValuesChanging += (_, e) => Preview(Text(e.Values, _color.Color));
            _numbers.ValuesCommitted += (_, e) => _ = CommitAsync(Text(e.Values, _color.Color));
            _numbers.ScrubCancelled += (_, _) => Cancel();
            _color.ColorChanging += (_, e) => Preview(Text(_numbers.Values ?? [], e.Color));
            _color.ColorCommitted += (_, e) => _ = CommitAsync(Text(_numbers.Values ?? [], e.Color));
            _color.ColorCancelled += (_, _) => Cancel();
        }

        public override Control View => _view;

        public override Control? FocusTarget => _numbers.Cells.Count > 0 ? _numbers.Cells[0] : _numbers;

        public override bool HasDraft => _numbers.Cells.Any(static cell => cell.HasDraft);

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (HasDraft && _numbers.IsKeyboardFocusWithin)
                return;

            var shadow = Read(value.Text, out var count, out var all);

            _inset = shadow.IsInset;
            _rest = count > 1 ? string.Join(", ", Enumerable.Range(1, count - 1).Select(at => all[at].ToString())) : string.Empty;
            _numbers.Values = [shadow.OffsetX, shadow.OffsetY, shadow.Blur, shadow.Spread];
            _numbers.MixedMask = value.IsMixed ? 0b1111 : 0;
            _color.Color = shadow.Color;
            _color.IsMixed = value.IsMixed;
        }

        public override void Release() => Cancel();

        /// <summary>Первая тень записи; пустая или непрочитанная — нулевая, чёрная с прозрачностью.</summary>
        private static BoxShadow Read(string? text, out int count, out BoxShadows all)
        {
            all = default;
            count = 0;

            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    all = BoxShadows.Parse(text);
                    count = all.Count;
                }
                catch (FormatException)
                {
                    count = 0;
                }
            }

            return count > 0 ? all[0] : new BoxShadow { Color = Color.FromArgb(0x40, 0, 0, 0) };
        }

        /// <summary>Тень записью разметки: <c>[inset] x y размытие разлёт цвет</c>, остальные — следом.</summary>
        private string Text(IReadOnlyList<double> values, Color color)
        {
            static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

            var first = string.Join(' ', values.Take(4).Select(Number)) + " " + AxColorField.Format(color);

            if (_inset)
                first = "inset " + first;

            return _rest.Length > 0 ? first + ", " + _rest : first;
        }

        private void Preview(string text)
        {
            _preview ??= _context.BeginPreview();
            _preview?.Show(text);
        }

        private async Task CommitAsync(string text)
        {
            var preview = _preview;

            _preview = null;

            if (preview is null)
            {
                await _context.WriteAsync(text).ConfigureAwait(true);
                return;
            }

            using (preview)
                await preview.CommitAsync(text).ConfigureAwait(true);
        }

        private void Cancel()
        {
            _preview?.Dispose();
            _preview = null;
        }
    }
}
