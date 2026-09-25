using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.ViewModels;

/// <summary>
/// Строка списка плагинов: заголовок группы или сам плагин.
/// </summary>
/// <remarks>
/// Список один на все группы, как в Rider: стрелки идут по плагинам подряд, не спотыкаясь о
/// границы групп. Заголовок — такая же строка списка, только выбрать её нельзя, и стиль строки
/// узнаёт её по этому признаку.
/// </remarks>
public interface IPluginRow
{
    /// <summary>Строка — заголовок группы, а не плагин.</summary>
    bool IsHeader { get; }

    /// <summary>Имя строки для средств доступности.</summary>
    string Label { get; }

    /// <summary>Состояние строки для средств доступности; пусто — сказать нечего.</summary>
    string Status { get; }
}

/// <summary>
/// Группа списка плагинов: внешние, языковые пакеты, встроенные.
/// </summary>
/// <remarks>
/// Счётчик — как «User-installed (1 of 1 enabled)» у Rider: сколько включено и сколько всего. Он
/// живой — идёт за галочками, ещё не сохранёнными, — потому что показывает то, что человек
/// сейчас видит в списке, а не то, что записано на диске. Встроенные не выключаются, и у них
/// счётчик — просто сколько.
/// <para>
/// Свернуть группу нельзя, пока идёт поиск: свёрнутая спрятала бы найденное, как спрятала бы его
/// свёрнутая ветка дерева настроек.
/// </para>
/// </remarks>
public sealed class PluginGroup : IPluginRow, INotifyPropertyChanged
{
    private readonly bool _counts;
    private bool _expanded;
    private bool _folds = true;

    /// <summary>Заводит группу над её плагинами.</summary>
    /// <param name="key">Имя группы для памяти о свёрнутости: <c>external</c>, <c>languages</c>, <c>builtin</c>.</param>
    /// <param name="title">Подпись группы.</param>
    /// <param name="cards">Плагины группы в порядке показа.</param>
    /// <param name="counts">Считать включённых: у встроенных их не выключают, и считать нечего.</param>
    /// <param name="expanded">Раскрыта ли группа.</param>
    public PluginGroup(string key, string title, IReadOnlyList<PluginCard> cards, bool counts, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(cards);

        Key = key;
        Title = title;
        Cards = cards;
        _counts = counts;
        _expanded = expanded;

        foreach (var card in cards)
            card.PropertyChanged += OnCardChanged;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Группу свернули или раскрыли — список пересобирается.</summary>
    public event EventHandler? Toggled;

    /// <inheritdoc/>
    public bool IsHeader => true;

    /// <summary>Имя группы — по нему страница помнит, свёрнута ли она.</summary>
    public string Key { get; }

    /// <summary>Подпись группы.</summary>
    public string Title { get; }

    /// <inheritdoc/>
    public string Label => Title;

    /// <inheritdoc/>
    public string Status => CounterHint ?? Counter;

    /// <summary>Плагины группы в порядке показа.</summary>
    public IReadOnlyList<PluginCard> Cards { get; }

    /// <summary>Счётчик в заголовке: «2 из 3» — включено из всех, у встроенных — сколько всего.</summary>
    public string Counter =>
        _counts
            ? string.Format(CultureInfo.CurrentCulture, Localizer.Instance["plugins.group.counter"], On, Cards.Count)
            : Cards.Count.ToString(CultureInfo.CurrentCulture);

    /// <summary>Счётчик словами — для подсказки и диктора: «2 из 3» без слова «включено» читается не всеми.</summary>
    public string? CounterHint =>
        _counts ? string.Format(CultureInfo.CurrentCulture, Localizer.Instance["plugins.group.counter.hint"], On, Cards.Count) : null;

    /// <summary>Группа раскрыта.</summary>
    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
                return;

            _expanded = value;
            Notify();
            Toggled?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Группу можно свернуть — пока не идёт поиск.</summary>
    public bool CanFold
    {
        get => _folds;
        internal set
        {
            if (_folds == value)
                return;

            _folds = value;
            Notify();
        }
    }

    /// <summary>
    /// Заголовок набором букв не находится: подпись у него есть, а выбрать его нельзя.
    /// </summary>
    public override string ToString() => string.Empty;

    private int On => Cards.Count(card => card.IsOn);

    private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PluginCard.IsOn))
            return;

        Notify(nameof(Counter));
        Notify(nameof(CounterHint));
        Notify(nameof(Status));
    }

    private void Notify([CallerMemberName] string? property = null) => PropertyChanged.Raise(this, property);
}
