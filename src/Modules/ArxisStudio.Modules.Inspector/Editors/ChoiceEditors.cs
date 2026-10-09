using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>Редактор флажка: «да» и «нет», а у разных значений — третье, неопределённое состояние.</summary>
[PropertyEditor(typeof(bool), Fallback = true)]
public sealed class BoolEditor : PropertyEditor
{
    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new BoolField(context);
    }

    private sealed class BoolField : InspectorField
    {
        private readonly InspectorFieldContext _context;
        private readonly AxCheckBox _box;
        private bool _showing;

        public BoolField(InspectorFieldContext context)
        {
            _context = context;
            _box = new AxCheckBox
            {
                IsEnabled = !context.Property.IsReadOnly,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _box.IsCheckedChanged += (_, _) =>
            {
                if (!_showing && _box.IsChecked is { } value)
                    _ = _context.WriteAsync(value ? "True" : "False");
            };
        }

        public override Control View => _box;

        public override Control? FocusTarget => _box;

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            _showing = true;

            try
            {
                // Разные значения — третье состояние: щелчок по нему ставит всем одно.
                _box.IsThreeState = value.IsMixed;
                _box.IsChecked = value.IsMixed ? null : bool.TryParse(value.Text, out var on) && on;
            }
            finally
            {
                _showing = false;
            }
        }
    }
}

/// <summary>
/// Редактор перечисления: до четырёх значений — сегментами, больше — списком; флаги — текстом через запятую.
/// </summary>
/// <remarks>
/// Сегменты видны все сразу и выбираются одним щелчком — как выравнивание в Figma; список держит длинные
/// перечисления. Флаги складываются, и их пишут так, как пишет разметка: <c>Left, Top</c>.
/// <para>
/// Сегменты не сжимаются: их наименьшая ширина — та, что им нужна, и поле называет её строке. Колонке значения
/// тесно — сегменты уходят под подпись во всю ширину, а не обрезаются у края.
/// </para>
/// </remarks>
[PropertyEditor(typeof(Enum), ForDerived = true, Fallback = true)]
public sealed class EnumEditor : PropertyEditor
{
    /// <summary>Сколько значений ещё помещается сегментами.</summary>
    public const int MaxSegments = 4;

    /// <inheritdoc/>
    public override bool Accepts(InspectorProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property.ValueType.IsEnum;
    }

    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var type = context.Property.ValueType;

        if (type.IsFlags)
            return new TextField(context, multiline: false);

        return type.EnumNames.Count <= MaxSegments ? new SegmentsField(context) : new ListField(context);
    }

    private sealed class SegmentsField : InspectorField
    {
        private readonly InspectorFieldContext _context;
        private readonly AxSegmentedControl _segments;
        private bool _showing;

        public SegmentsField(InspectorFieldContext context)
        {
            _context = context;
            _segments = new AxSegmentedControl { IsEnabled = !context.Property.IsReadOnly, HorizontalAlignment = HorizontalAlignment.Left };

            foreach (var name in context.Property.ValueType.EnumNames)
                _segments.Items.Add(new AxSegmentItem { Content = name, Tag = name });

            _segments.SelectionChanged += (_, _) =>
            {
                if (!_showing && _segments.SelectedItem is AxSegmentItem { Tag: string name })
                    _ = _context.WriteAsync(name);
            };
            _segments.AttachedToVisualTree += (_, _) => Fit();
        }

        public override Control View => _segments;

        public override Control? FocusTarget => _segments;

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            _showing = true;

            try
            {
                _segments.SelectedItem = value.IsMixed
                    ? null
                    : _segments.Items.OfType<AxSegmentItem>().FirstOrDefault(item => Equals(item.Tag, value.Text));
            }
            finally
            {
                _showing = false;
            }
        }

        /// <summary>Называет строке наименьшую ширину сегментов — ту, что им нужна: тема и кегль известны только на экране.</summary>
        private void Fit()
        {
            _segments.ClearValue(Layoutable.MinWidthProperty);
            _segments.Measure(Size.Infinity);
            _segments.MinWidth = Math.Ceiling(_segments.DesiredSize.Width);
        }
    }

    private sealed class ListField : InspectorField
    {
        private readonly InspectorFieldContext _context;
        private readonly AxComboBox _list;
        private bool _showing;

        public ListField(InspectorFieldContext context)
        {
            _context = context;
            _list = new AxComboBox
            {
                Size = AxControlSize.Compact,
                IsEnabled = !context.Property.IsReadOnly,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = context.Property.ValueType.EnumNames,
            };

            _list.SelectionChanged += (_, _) =>
            {
                if (!_showing && _list.SelectedItem is string name)
                    _ = _context.WriteAsync(name);
            };
        }

        public override Control View => _list;

        public override Control? FocusTarget => _list;

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            _showing = true;

            try
            {
                _list.SelectedItem = value.IsMixed ? null : value.Text;
            }
            finally
            {
                _showing = false;
            }
        }
    }
}
