using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using Avalonia.Automation;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>
/// Редактор жетонов: слова через пробел — классы стиля — чипами, а правка пишет весь список одним значением:
/// <c>Classes="primary large"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Классы стиля (<see cref="Classes"/>) он правит по типу значения; свой список слов плагин отдаёт ему подсказкой
/// (<see cref="Hint"/>). Снятый последний жетон снимает и значение: пустой атрибут разметке ни к чему.
/// </para>
/// <para>
/// У нескольких выбранных с разными жетонами поле только читается и пустое, с прочерком: одно значение на всех
/// стёрло бы у каждого его собственные жетоны.
/// </para>
/// </remarks>
[PropertyEditor(typeof(Classes), Fallback = true)]
[PropertyEditor(typeof(string), Hint = Hint, Fallback = true)]
public sealed class TokensEditor : PropertyEditor
{
    /// <summary>Подсказка, которой свойство просит этот редактор.</summary>
    public const string Hint = "tokens";

    /// <summary>Чем поле отмечает разные значения у выбранных.</summary>
    private const string MixedMark = "—";

    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TokensField(context);
    }

    private sealed class TokensField : InspectorField
    {
        private readonly InspectorFieldContext _context;
        private readonly AxTokenField _field;
        private bool _showing;

        public TokensField(InspectorFieldContext context)
        {
            _context = context;
            _field = new AxTokenField { Size = AxControlSize.Compact, IsReadOnly = context.Property.IsReadOnly };

            AutomationProperties.SetName(_field, context.Property.Label);
            _field.TokensChanged += (_, e) =>
            {
                if (!_showing)
                    _ = _context.WriteAsync(e.Tokens.Count == 0 ? null : string.Join(' ', e.Tokens));
            };
        }

        public override Control View => _field;

        public override Control? FocusTarget => _field.Input ?? (Control)_field;

        public override bool HasDraft => !string.IsNullOrEmpty(_field.Input?.Text);

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            _showing = true;

            try
            {
                _field.Tokens = value.IsMixed ? [] : AxTokenField.Split(value.Text);
                _field.IsReadOnly = _context.Property.IsReadOnly || value.IsMixed;
                _field.PlaceholderText = value.IsMixed ? MixedMark : null;
            }
            finally
            {
                _showing = false;
            }
        }
    }
}
