using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>
/// Редактор цвета: цвет и сплошная кисть — полем цвета с выбором под ним; кисть, которая не цвет, — текстом.
/// </summary>
/// <remarks>
/// Кисть в разметке — чаще всего цвет (<c>Background="#2B3A55"</c>), и правят её как цвет. Градиент и ресурс цветом
/// не прочтёшь: такую кисть поле показывает текстом, как написано, и правят её текстом. Набранное имя («Red»)
/// пишется именем — как его набрали. Тягу по спектру и ползункам видно на цели на каждом движении, а пишется
/// она одной правкой при отпускании (<see cref="FieldPreview"/>).
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

        return new ColorFieldHost(context);
    }

    private sealed class ColorFieldHost : InspectorField
    {
        private readonly AxColorField _color;
        private readonly TextField _text;
        private readonly FieldPreview _preview;
        private readonly Border _host = new();

        public ColorFieldHost(InspectorFieldContext context)
        {
            _preview = new FieldPreview(context);
            _color = new AxColorField
            {
                Size = AxControlSize.Compact,
                IsReadOnly = context.Property.IsReadOnly,
                AllowsAlpha = true,
            };
            _text = new TextField(context, multiline: false);
            _color.ColorChanging += (_, e) => _preview.Show(AxColorField.Format(e.Color));
            _color.ColorCommitted += (_, e) => _ = _preview.CommitAsync(e.Text ?? AxColorField.Format(e.Color));
            _color.ColorCancelled += (_, _) => _preview.Cancel();
            _host.Child = _color;
        }

        public override Control View => _host;

        public override Control? FocusTarget => _host.Child == _color ? _color.Input : _text.FocusTarget;

        public override bool HasDraft => _host.Child == _text.View && _text.HasDraft;

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            var written = TextField.Written(value);

            if (value.IsMixed || (value.Source != InspectorValueSource.Expression && AxColorField.TryParse(written, out _)))
            {
                if (AxColorField.TryParse(written, out var color))
                    _color.Color = color;

                _color.IsMixed = value.IsMixed;
                _host.Child = _color;
            }
            else
            {
                // Градиент, ресурс, привязка — не цвет: показывается как написано, и правится текстом.
                _text.Show(value);
                _host.Child = _text.View;
            }
        }

        public override void Release()
        {
            _preview.Cancel();
            _text.Release();
        }
    }
}
