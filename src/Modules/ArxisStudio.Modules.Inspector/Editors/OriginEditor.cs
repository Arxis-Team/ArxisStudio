using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>
/// Редактор точки опоры (<see cref="RelativePoint"/>): X и Y и единица — доли размера в процентах или точки, — как
/// Pivot у Unity, но с единицей Avalonia: <c>50%,50%</c> или <c>10,20</c>.
/// </summary>
/// <remarks>
/// Смена единицы чисел не пересчитывает: доли размера в точки без размера не переводятся, и число человек ставит сам.
/// Протяжку видно на цели на ходу, а пишется одно значение при отпускании (<see cref="FieldPreview"/>); смена единицы —
/// одна запись.
/// </remarks>
[PropertyEditor(typeof(RelativePoint))]
public sealed class OriginEditor : PropertyEditor
{
    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new OriginField(context, Context.Strings);
    }

    /// <summary>Читает точку: проценты — доли размера, иначе точки.</summary>
    /// <param name="text">Запись.</param>
    /// <param name="x">X: проценты или точки.</param>
    /// <param name="y">Y: проценты или точки.</param>
    /// <param name="relative">Доли размера.</param>
    /// <returns>Прочлась ли.</returns>
    internal static bool TryRead(string? text, out double x, out double y, out bool relative)
    {
        (x, y, relative) = (50, 50, true);

        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            var point = RelativePoint.Parse(text);

            relative = point.Unit == RelativeUnit.Relative;
            (x, y) = relative ? (point.Point.X * 100, point.Point.Y * 100) : (point.Point.X, point.Point.Y);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Пишет точку разметкой: <c>50%,50%</c> или <c>10,20</c>.</summary>
    /// <param name="x">X.</param>
    /// <param name="y">Y.</param>
    /// <param name="relative">Доли размера.</param>
    internal static string Write(double x, double y, bool relative)
    {
        var mark = relative ? "%" : string.Empty;

        return $"{AxNumberField.Format(x, 6)}{mark},{AxNumberField.Format(y, 6)}{mark}";
    }

    private sealed class OriginField : InspectorField
    {
        private readonly InspectorFieldContext _context;
        private readonly FieldPreview _preview;
        private readonly AxVectorField _point;
        private readonly AxSegmentedControl _unit;
        private readonly DockPanel _view = new();
        private bool _relative = true;
        private bool _showing;

        public OriginField(InspectorFieldContext context, IStudioStrings strings)
        {
            _context = context;
            _preview = new FieldPreview(context);

            var readOnly = context.Property.IsReadOnly;

            _point = new AxVectorField { Size = AxControlSize.Compact, Labels = ["X", "Y"], Step = 1, IsReadOnly = readOnly };
            _unit = new AxSegmentedControl { IsEnabled = !readOnly, VerticalAlignment = VerticalAlignment.Top };
            _unit.Items.Add(Unit("%", strings["inspector.origin.relative"], relative: true));
            _unit.Items.Add(Unit("px", strings["inspector.origin.absolute"], relative: false));

            AutomationProperties.SetName(_point, context.Property.Label);
            DockPanel.SetDock(_unit, Dock.Right);
            _view.Bind(DockPanel.HorizontalSpacingProperty, _view.GetResourceObservable("AxSpaceTight"));
            _view.Children.Add(_unit);
            _view.Children.Add(_point);

            _point.ValuesChanging += (_, e) => _preview.Show(Write(e.Values[0], e.Values[1], _relative));
            _point.ValuesCommitted += (_, e) => _ = _preview.CommitAsync(Write(e.Values[0], e.Values[1], _relative));
            _point.ScrubCancelled += (_, _) => _preview.Cancel();
            _unit.SelectionChanged += (_, _) => OnUnitChanged();
        }

        public override Control View => _view;

        public override Control? FocusTarget => _point.Cells.Count > 0 ? _point.Cells[0] : _point;

        public override bool HasDraft => _point.Cells.Any(static cell => cell.HasDraft);

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            // Набранное в ячейке с кареткой не затирается: значение покажется снова, когда набранное запишут.
            if (HasDraft && _view.IsKeyboardFocusWithin)
                return;

            TryRead(value.Text, out var x, out var y, out var relative);

            _showing = true;

            try
            {
                _relative = relative;
                _point.Values = [x, y];
                _point.MixedMask = value.IsMixed ? 0b11 : 0;
                _unit.SelectedItem = value.IsMixed
                    ? null
                    : _unit.Items.OfType<AxSegmentItem>().FirstOrDefault(item => Equals(item.Tag, relative));
            }
            finally
            {
                _showing = false;
            }
        }

        public override void Release() => _preview.Cancel();

        private static AxSegmentItem Unit(string text, string name, bool relative) => new()
        {
            Content = text,
            Tag = relative,
            [AutomationProperties.NameProperty] = name,
            [ToolTip.TipProperty] = name,
        };

        /// <summary>Единицу сменили — та же точка с новой единицей, одной записью; числа не пересчитываются.</summary>
        private void OnUnitChanged()
        {
            if (_showing || _unit.SelectedItem is not AxSegmentItem { Tag: bool relative } || relative == _relative)
                return;

            _relative = relative;

            var values = _point.Values ?? [];

            _ = _context.WriteAsync(Write(values.Count > 0 ? values[0] : 0, values.Count > 1 ? values[1] : 0, relative));
        }
    }
}
