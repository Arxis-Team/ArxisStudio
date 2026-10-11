using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>
/// Список вариантов: значение выбирают из названных — длинное перечисление, курсор, всё, у чего метаданные назвали
/// варианты единственно возможными (<see cref="InspectorConstraints.ChoicesOnly"/>).
/// </summary>
/// <remarks>
/// <para>
/// Показывается подпись варианта, а пишется его значение. У перечисления с синонимами — <c>Normal</c> и
/// <c>Regular</c> у начертания — вариант узнаётся и по числу: написанное «Regular» стоит вариантом «Normal».
/// </para>
/// <para>
/// У свойства, значение которого может и отсутствовать, — <c>Cursor</c>, — первый пункт «По умолчанию»: незаданное
/// стоит им, а не пустым списком, и выбрать его — снять атрибут (запись 373).
/// </para>
/// </remarks>
internal sealed class ChoiceList : InspectorField
{
    private readonly InspectorFieldContext _context;
    private readonly IReadOnlyList<InspectorChoice> _choices;
    private readonly AxComboBox _list;
    private readonly bool _hasDefault;
    private bool _showing;

    /// <summary>Пункт «По умолчанию»: его значение — снять атрибут.</summary>
    private static readonly object DefaultMark = new();

    /// <summary>Заводит список.</summary>
    /// <param name="context">Свойство строки и дорога к цели.</param>
    /// <param name="choices">Варианты по порядку.</param>
    /// <param name="defaultTitle">
    /// Подпись пункта «По умолчанию»; пусто — пункта нет. Ставится, только если значения у свойства может не быть.
    /// </param>
    public ChoiceList(InspectorFieldContext context, IReadOnlyList<InspectorChoice> choices, string? defaultTitle = null)
    {
        _context = context;
        _choices = choices;
        _list = new AxComboBox
        {
            Size = AxControlSize.Compact,
            IsEnabled = !context.Property.IsReadOnly,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _hasDefault = defaultTitle is not null && context.Property.ValueType.IsNullable && !context.Property.IsReadOnly;

        if (_hasDefault)
            _list.Items.Add(new AxComboBoxItem { Content = defaultTitle, Tag = DefaultMark });

        foreach (var choice in choices)
        {
            var item = new AxComboBoxItem { Content = choice.Title ?? choice.Value, Tag = choice.Value };

            if (choice.Description is { } description)
                ToolTip.SetTip(item, description);

            _list.Items.Add(item);
        }

        AutomationProperties.SetName(_list, context.Property.Label);
        _list.SelectionChanged += (_, _) =>
        {
            if (_showing)
                return;

            if (_list.SelectedItem is AxComboBoxItem { Tag: string value })
                _ = _context.WriteAsync(value);
            else if (_list.SelectedItem is AxComboBoxItem { Tag: var mark } && ReferenceEquals(mark, DefaultMark))
                _ = _context.WriteAsync(null);
        };
    }

    /// <inheritdoc/>
    public override Control View => _list;

    /// <inheritdoc/>
    public override Control? FocusTarget => _list;

    /// <inheritdoc/>
    public override void Show(InspectorValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _showing = true;

        try
        {
            var at = value.IsMixed ? -1 : IndexOf(value);

            _list.SelectedIndex = at >= 0
                ? at + (_hasDefault ? 1 : 0)
                : _hasDefault && !value.IsMixed && !value.IsWritten ? 0 : -1;
        }
        finally
        {
            _showing = false;
        }
    }

    /// <summary>Вариант значения: по тексту, а у перечисления — и по числу, под каким бы синонимом его ни написали.</summary>
    private int IndexOf(InspectorValue value)
    {
        for (var at = 0; at < _choices.Count; at++)
        {
            if (string.Equals(_choices[at].Value, value.Text, StringComparison.OrdinalIgnoreCase))
                return at;
        }

        if (value.Shared is not Enum shared)
            return -1;

        var type = shared.GetType();

        for (var at = 0; at < _choices.Count; at++)
        {
            if (Enum.TryParse(type, _choices[at].Value, ignoreCase: true, out var parsed) && Equals(parsed, shared))
                return at;
        }

        return -1;
    }
}
