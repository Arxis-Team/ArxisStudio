using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>
/// Редактор шрифта: список семейств, каждое — своим начертанием, и своё имя набором.
/// </summary>
/// <remarks>
/// <para>
/// В поле — имя семейства («Inter»), а не запись «compositefont:…»: семейство темы Avalonia составное, и его запись
/// человеку не прочесть; у «$Default» — имя шрифта, который за ним стоит. В списке — варианты метаданных (шрифты системы у
/// таблицы Avalonia), а без них — шрифты системы сами, и ещё шрифт по умолчанию и нынешнее семейство: шрифт темы или
/// приложения в системе может и не стоять. Каждое имя написано своим шрифтом, а список виртуализован: семейств сотни, а
/// на экране — десяток.
/// </para>
/// <para>
/// Пишется имя: выбранное в списке — когда список закрывается, набранное — на Enter и уходе каретки; пустое снимает
/// значение, Esc возвращает показанное. Набор не пишет на каждой букве: имя, совпавшее со строкой списка, — ещё не выбор,
/// а список, открытый с набранным и закрытый без выбора, его не пишет.
/// </para>
/// </remarks>
[PropertyEditor(typeof(FontFamily))]
public sealed class FontEditor : PropertyEditor
{
    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new FontField(context);
    }

    /// <summary>Шрифты системы по имени, по алфавиту; платформы со шрифтами нет — пусто.</summary>
    internal static IReadOnlyList<string> SystemFamilies()
    {
        try
        {
            return
            [
                .. FontManager.Current.SystemFonts
                    .Select(static family => family.Name)
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.CurrentCultureIgnoreCase),
            ];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>
    /// Имя семейства значения: у составного — первое имя, как его показывает тема, а у шрифта по умолчанию — имя шрифта,
    /// который за ним стоит.
    /// </summary>
    /// <param name="value">Значение у цели.</param>
    internal static string NameOf(InspectorValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Shared is FontFamily family)
            return Named(family.Name);

        var text = TextField.Written(value);

        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        try
        {
            return Named(FontFamily.Parse(text).Name);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return text;
        }
    }

    /// <summary>Имя, как его видит человек: «$Default» — имя шрифта по умолчанию.</summary>
    private static string Named(string name) =>
        string.Equals(name, FontFamily.DefaultFontFamilyName, StringComparison.Ordinal) && DefaultName() is { Length: > 0 } actual
            ? actual
            : name;

    /// <summary>Имя шрифта по умолчанию; менеджера шрифтов нет — пусто.</summary>
    private static string DefaultName()
    {
        try
        {
            return FontManager.Current.DefaultFontFamily.Name;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private sealed class FontField : InspectorField
    {
        private readonly InspectorFieldContext _context;
        private readonly AxComboBox _box;
        private readonly IReadOnlyList<string> _families;
        private string _shown = string.Empty;
        private IDisposable? _hold;
        private bool _showing;
        private bool _writing;

        public FontField(InspectorFieldContext context)
        {
            _context = context;
            _families = Families(context.Property);
            _box = new AxComboBox
            {
                Size = AxControlSize.Compact,
                IsEditable = !context.Property.IsReadOnly,
                IsEnabled = !context.Property.IsReadOnly,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = _families,
                ItemTemplate = new FuncDataTemplate<string>(static (name, _) => new TextBlock { Text = name, FontFamily = Family(name) }),
            };

            AutomationProperties.SetName(_box, context.Property.Label);
            _box.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
            _box.DropDownClosed += (_, _) => OnClosed();
            _box.LostFocus += (_, _) => Dispatcher.UIThread.Post(OnBlur, DispatcherPriority.Background);
            _box.PropertyChanged += (_, e) =>
            {
                if (e.Property == ComboBox.TextProperty)
                    Track();
            };
        }

        public override Control View => _box;

        public override Control? FocusTarget => _box;

        public override bool HasDraft => !_showing && !_writing && !string.Equals((_box.Text ?? string.Empty).Trim(), _shown, StringComparison.Ordinal);

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            // Набранное с кареткой не затирается: значение покажется снова, когда набранное запишут.
            if (HasDraft && _box.IsKeyboardFocusWithin)
                return;

            var name = value.IsMixed ? string.Empty : NameOf(value);

            _showing = true;

            try
            {
                _shown = name;

                // Нынешнее семейство — в списке всегда, даже если в системе его нет.
                IReadOnlyList<string> items = name.Length == 0 || _families.Contains(name, StringComparer.OrdinalIgnoreCase)
                    ? _families
                    : [name, .. _families];

                if (!ReferenceEquals(_box.ItemsSource, items) && !(_box.ItemsSource is IReadOnlyList<string> shown && shown.SequenceEqual(items)))
                    _box.ItemsSource = items;

                _box.SelectedItem = items.FirstOrDefault(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
                _box.Text = name;
                _box.PlaceholderText = value.IsMixed ? "—" : null;
            }
            finally
            {
                _showing = false;
            }

            Track();
        }

        public override void Release()
        {
            _hold?.Dispose();
            _hold = null;
        }

        /// <summary>Семейства списка: шрифт по умолчанию, затем варианты метаданных или шрифты системы.</summary>
        private static IReadOnlyList<string> Families(InspectorProperty property)
        {
            var choices = property.Constraints.Choices.Count > 0
                ? property.Constraints.Choices.Select(static choice => choice.Value)
                : SystemFamilies();

            return [.. new[] { DefaultName() }.Concat(choices).Where(static name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>Семейство для образца строки: имя, которое не прочтётся, пишется шрифтом по умолчанию.</summary>
        private static FontFamily Family(string? name)
        {
            try
            {
                return string.IsNullOrWhiteSpace(name) ? FontFamily.Default : new FontFamily(name);
            }
            catch (ArgumentException)
            {
                return FontFamily.Default;
            }
        }

        private void OnKey(object? sender, KeyEventArgs e)
        {
            if (e.KeyModifiers != KeyModifiers.None || _box.IsDropDownOpen)
                return;

            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && HasDraft)
            {
                Put(_shown);
                _context.Report(InspectorCheck.Valid);
                e.Handled = true;
            }
        }

        private void OnBlur()
        {
            if (!_box.IsKeyboardFocusWithin && !_box.IsDropDownOpen)
                Commit();
        }

        /// <summary>
        /// Закрытый список пишет выбранное в нём; набранное, с которым список открыли и закрыли без выбора, — ещё
        /// черновик: его пишут Enter и уход каретки.
        /// </summary>
        private void OnClosed()
        {
            if (_box.SelectedItem is string picked && string.Equals(picked, (_box.Text ?? string.Empty).Trim(), StringComparison.Ordinal))
                Commit();
        }

        /// <summary>Имя в поле — в значение: пустое снимает его, непрочтённое остаётся с причиной.</summary>
        private void Commit()
        {
            if (!HasDraft || _context.Property.IsReadOnly)
                return;

            var name = (_box.Text ?? string.Empty).Trim();

            if (name.Length > 0)
            {
                var check = _context.Check(name);

                _context.Report(check);

                if (!check.IsValid)
                    return;
            }

            _ = WriteAsync(name);
        }

        private async Task WriteAsync(string name)
        {
            _writing = true;

            try
            {
                if (await _context.WriteAsync(name.Length > 0 ? name : null).ConfigureAwait(true))
                    _shown = name;
            }
            finally
            {
                _writing = false;
                Track();
            }
        }

        /// <summary>Держит цель, пока набрано своё, и отпускает, когда набранного нет.</summary>
        private void Track()
        {
            if (HasDraft)
                _hold ??= _context.Hold();
            else
                Release();
        }

        private void Put(string text)
        {
            _showing = true;

            try
            {
                _box.Text = text;
            }
            finally
            {
                _showing = false;
            }

            Track();
        }
    }
}
