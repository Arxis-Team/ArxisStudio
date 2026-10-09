using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Catalog;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>Редактор текста: строки и всё, что пишется текстом и своего редактора не нашло.</summary>
/// <remarks>Многострочный текст встаёт под подписью во всю ширину.</remarks>
[PropertyEditor(typeof(string), Fallback = true)]
[PropertyEditor(typeof(object), Hint = InspectorCatalog.TextHint, Fallback = true)]
public sealed class TextEditor : PropertyEditor
{
    /// <inheritdoc/>
    public override InspectorFieldLayout LayoutOf(InspectorProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property.Constraints.Multiline ? InspectorFieldLayout.Below : InspectorFieldLayout.Inline;
    }

    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TextField(context, context.Property.Constraints.Multiline);
    }
}

/// <summary>Редактор «только чтение»: значение — выделяемым текстом, а не выключенным полем.</summary>
[PropertyEditor(typeof(object), Hint = InspectorCatalog.ReadOnlyHint, Fallback = true)]
public sealed class ReadOnlyEditor : PropertyEditor
{
    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context) => new ReadOnlyField();

    private sealed class ReadOnlyField : InspectorField
    {
        private readonly AxTextBox _box = new() { IsReadOnly = true, Size = AxControlSize.Compact };

        public override Control View => _box;

        public override Control? FocusTarget => _box;

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            _box.Text = value.IsMixed ? string.Empty : TextField.Written(value);
            _box.PlaceholderText = value.IsMixed ? "—" : null;
        }
    }
}
