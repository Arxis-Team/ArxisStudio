using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>
/// Редактор цвета и кисти — полем цвета, как у Unity: полоса цвета с полоской прозрачности, пипетка, выбор по щелчку.
/// </summary>
/// <remarks>
/// <para>
/// Кисть в разметке — чаще всего цвет (<c>Background="#2B3A55"</c>), и правят её как цвет. Незаданная кисть — «нет
/// цвета»: перечёркнутая полоса, а выбранный цвет её задаёт. Цвет стиля или темы — цвет, а откуда он, говорят тон и
/// подсказка подписи строки.
/// </para>
/// <para>
/// Градиент цветом не поправишь, а написать его можно только элементом: его видно образцом самой кисти, а правят его в
/// XAML. Выражение — привязку, ресурс — показывает строка жетоном, и до поля оно не доходит.
/// </para>
/// <para>
/// Тягу по выбору цвета видно на цели на каждом движении, а пишется она одной правкой при отпускании
/// (<see cref="FieldPreview"/>); пипетка пишет сразу, одной правкой.
/// </para>
/// </remarks>
[PropertyEditor(typeof(Color), Fallback = true)]
[PropertyEditor(typeof(IBrush), Fallback = true)]
[PropertyEditor(typeof(ISolidColorBrush), Fallback = true)]
[PropertyEditor(typeof(SolidColorBrush), Fallback = true)]
public sealed class ColorEditor : PropertyEditor
{
    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new ColorFieldHost(context, Context.Strings["inspector.gradient"]);
    }

    private sealed class ColorFieldHost : InspectorField
    {
        private readonly AxColorField _color;
        private readonly AxColorSwatch _gradientSwatch = new();
        private readonly Border _gradient;
        private readonly FieldPreview _preview;
        private readonly Border _host = new();

        public ColorFieldHost(InspectorFieldContext context, string gradientTip)
        {
            _preview = new FieldPreview(context);
            _color = new AxColorField
            {
                Size = AxControlSize.Compact,
                IsReadOnly = context.Property.IsReadOnly,
                AllowsAlpha = true,
            };
            _color.ColorChanging += (_, e) => _preview.Show(AxColorField.Format(e.Color));
            _color.ColorCommitted += (_, e) => _ = _preview.CommitAsync(e.Text ?? AxColorField.Format(e.Color));
            _color.ColorCancelled += (_, _) => _preview.Cancel();

            // Градиент — образцом в рамке поля: видно, что это кисть, но нажать её нечем.
            _gradient = new Border { Child = _gradientSwatch };
            _gradient[!Border.BorderBrushProperty] = _gradient.GetResourceObservable("AxStrokeControlBrush").ToBinding();
            _gradient[!Border.BorderThicknessProperty] = _gradient.GetResourceObservable("AxHairlineThickness").ToBinding();
            _gradient[!Border.CornerRadiusProperty] = _gradient.GetResourceObservable("AxCornerRadiusSmall").ToBinding();
            _gradient[!Border.PaddingProperty] = _gradient.GetResourceObservable("AxColorSwatchInset").ToBinding();
            _gradient[!Border.BackgroundProperty] = _gradient.GetResourceObservable("AxSurfaceBaseBrush").ToBinding();
            _gradient[!Layoutable.MinHeightProperty] = _gradient.GetResourceObservable("AxControlHeightCompact").ToBinding();
            ToolTip.SetTip(_gradient, gradientTip);

            _host.Child = _color;
        }

        public override Control View => _host;

        public override Control? FocusTarget => ReferenceEquals(_host.Child, _color) ? _color.Swatch : null;

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.IsMixed)
            {
                _color.IsNone = false;
                _color.IsMixed = true;
                _host.Child = _color;
                return;
            }

            _color.IsMixed = false;

            // Кисть, которая не цвет, служба отдаёт неизменяемым снимком: её видно как есть.
            if (value.Shared is IBrush brush and not ISolidColorBrush)
            {
                _gradientSwatch.Brush = brush;
                _host.Child = _gradient;
                return;
            }

            if (value.Shared is Color shared)
            {
                _color.Color = shared;
                _color.IsNone = false;
            }
            else if (AxColorField.TryParse(value.Text, out var color))
            {
                _color.Color = color;
                _color.IsNone = false;
            }
            else
            {
                // Значения нет вовсе — кисть не задана: «нет цвета», а выбранный цвет её задаст.
                _color.IsNone = true;
            }

            _host.Child = _color;
        }

        public override void Release() => _preview.Cancel();
    }
}
