using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Reactive;

namespace ArxisStudio.Modules.Project.Dialogs;

/// <summary>Разновидность пункта строкой списка — со значком пункта, если своего у неё нет.</summary>
/// <param name="Id">Идентификатор из манифеста.</param>
/// <param name="Title">Строка списка.</param>
/// <param name="Icon">Значок.</param>
public sealed record CreateVariant(string Id, string Title, Geometry? Icon);

/// <summary>Ответ диалога создания.</summary>
/// <param name="Typed">Набранное имя — с каталогами, если пункт их разрешает.</param>
/// <param name="Variant">Выбранная разновидность; пусто — у пункта их нет.</param>
internal sealed record CreateAnswer(string Typed, string? Variant);

/// <summary>
/// Имя того, что создаёт пункт «Добавить ▸», — и разновидность, если пункт их предлагает.
/// </summary>
/// <remarks>
/// Проверка идёт на каждую букву и на каждую смену разновидности: у класса и пары
/// «разметка с кодом» под одним именем лягут разные файлы, и занятым может оказаться только один из
/// них. Кнопка знает то же, что строка под полем, и Enter с негодным именем не делает ничего.
/// </remarks>
public partial class CreateDialog : AxDialog
{
    private StudioNewItem? _item;
    private IStudioStrings? _strings;
    private Func<string, string?, NameCheck> _check = static (_, _) => default;

    /// <summary>Собирает диалог из разметки.</summary>
    public CreateDialog()
    {
        InitializeComponent();

        // Слушается свойство, а не правка: имя ставит и сам диалог, открываясь.
        Chosen.GetObservable(TextBox.TextProperty).Subscribe(new AnonymousObserver<string?>(_ => Refresh()));
        Variants.SelectionChanged += (_, _) => Refresh();

        Opened += (_, _) =>
        {
            Chosen.Focus();

            if (_item is { } item && Chosen.Text is { } text)
            {
                Chosen.SelectionStart = 0;
                Chosen.SelectionEnd = Math.Min(text.Length, Renaming.Selected(text, item.Kind == NewItemKind.Directory));
            }
        };

        Cancel.Click += (_, _) => Close(null);
        Confirm.Click += (_, _) => Submit();

        // Стрелки в поле ходят по разновидностям, как в попапе Rider: каретка остаётся в имени.
        Chosen.AddHandler(KeyDownEvent, (_, key) =>
        {
            if (key.KeyModifiers != KeyModifiers.None || !Variants.IsVisible || Variants.ItemCount == 0)
                return;

            var step = key.Key switch
            {
                Key.Up => -1,
                Key.Down => 1,
                _ => 0,
            };

            if (step == 0)
                return;

            Variants.SelectedIndex = Math.Clamp(Variants.SelectedIndex + step, 0, Variants.ItemCount - 1);
            key.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Form.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter)
            {
                Submit();
                key.Handled = true;
            }
        };
    }

    /// <summary>Выбранная разновидность; пусто — у пункта их нет.</summary>
    internal string? Variant => (Variants.SelectedItem as CreateVariant)?.Id;

    /// <summary>
    /// Спрашивает имя.
    /// </summary>
    /// <param name="owner">Окно, которому принадлежит диалог.</param>
    /// <param name="item">Пункт.</param>
    /// <param name="suggested">Предложенное имя.</param>
    /// <param name="strings">Словари модуля.</param>
    /// <param name="check">Проверка набранного при выбранной разновидности.</param>
    /// <returns>Ответ; null — человек передумал.</returns>
    internal static Task<CreateAnswer?> AskAsync(
        Window owner, StudioNewItem item, string suggested, IStudioStrings strings, Func<string, string?, NameCheck> check)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var dialog = new CreateDialog();

        dialog.Prepare(item, suggested, strings, check);

        return dialog.ShowDialog<CreateAnswer?>(owner);
    }

    /// <summary>Ставит диалогу пункт; поле получает предложенное имя, список — разновидности.</summary>
    internal void Prepare(StudioNewItem item, string suggested, IStudioStrings strings, Func<string, string?, NameCheck> check)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(suggested);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(check);

        _item = item;
        _strings = strings;
        _check = check;

        Title = Format("project.add.title", item.Title);
        Hint.IsVisible = item.Nested;

        var variants = item.Variants.Select(variant => new CreateVariant(variant.Id, variant.Title, variant.Icon ?? item.Icon)).ToList();

        Variants.ItemsSource = variants;
        Variants.IsVisible = variants.Count > 0;
        Variants.SelectedIndex = variants.Count > 0 ? 0 : -1;

        Chosen.Text = suggested;
        Refresh();
    }

    private void Refresh()
    {
        if (_item is null)
            return;

        var check = _check(Chosen.Text ?? string.Empty, Variant);

        Confirm.IsEnabled = check.IsFine;
        Problem.Text = NameWords.Say(check, Format);
        Problem.IsVisible = Problem.Text is not null;
    }

    /// <summary>Строка словаря со вставками; без словарей — сам ключ, как в предпросмотре разметки.</summary>
    private string Format(string key, params object[] values) => _strings is null ? key : _strings.Format(key, values);

    private void Submit()
    {
        if (Confirm.IsEnabled && Chosen.Text is { } chosen)
            Close(new CreateAnswer(chosen, Variant));
    }
}
