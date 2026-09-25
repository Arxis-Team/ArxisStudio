using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using ArxisStudio.ViewModels;

namespace ArxisStudio.Settings;

/// <summary>
/// Список страницы плагинов: группы, строки в порядке показа, отбор поиском, свёрнутость и выбор.
/// </summary>
/// <remarks>
/// Вынесен из <see cref="PluginsPage"/>: у страницы своя работа — каталог на диске и применение к
/// живой студии, — а здесь только то, что человек видит в списке и как по нему ходит. Страница
/// отдаёт свойства списка разметке как свои.
/// </remarks>
internal sealed class PluginRows : INotifyPropertyChanged
{
    /// <summary>Какие группы человек свернул или раскрыл сам — это переживает пересборку списка.</summary>
    private readonly Dictionary<string, bool> _folded = new(StringComparer.Ordinal);

    private IReadOnlyList<PluginCard> _cards = [];
    private IReadOnlyList<PluginCard> _builtIn = [];
    private string? _query;
    private PluginCard? _selected;
    private bool _arranging;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Группы списка в порядке показа; пустых нет.</summary>
    public IReadOnlyList<PluginGroup> Groups { get; private set; } = [];

    /// <summary>
    /// Строки списка: заголовок группы, затем её видимые плагины, и так по группам.
    /// </summary>
    /// <remarks>
    /// Список один на все группы, как в Rider: стрелки идут по плагинам подряд. Свёрнутая группа
    /// оставляет в нём только заголовок; поиск оставляет найденные строки и раскрывает группы.
    /// </remarks>
    public ObservableCollection<object> Rows { get; } = [];

    /// <summary>
    /// Выбранная строка — её показывают подробности справа.
    /// </summary>
    /// <remarks>
    /// Пишет сюда список. Заголовок группы выбрать нельзя, и пустоту от списка страница не
    /// принимает, пока выбранный плагин где-то есть: список теряет строку, когда её группу
    /// свернули или пересобрали, а подробности при этом менять незачем.
    /// </remarks>
    public object? Selected
    {
        get => _selected;
        set
        {
            if (value is PluginGroup || (value is null && (_arranging || (_selected is not null && !Rows.Contains(_selected)))))
                return;

            Choose(value as PluginCard);
        }
    }

    /// <summary>Выбранный плагин; null — не выбрано ничего.</summary>
    public PluginCard? Card => _selected;

    /// <summary>Выбран плагин — подробности есть что показать.</summary>
    public bool HasCard => _selected is not null;

    /// <summary>Какие группы человек свернул или раскрыл сам: ключ группы и раскрыта ли она.</summary>
    public IReadOnlyDictionary<string, bool> Folded => new Dictionary<string, bool>(_folded, StringComparer.Ordinal);

    /// <summary>
    /// Показывает новые карточки: отбор, группы, выбор и строки.
    /// </summary>
    /// <param name="cards">Установленные плагины.</param>
    /// <param name="builtIn">Встроенные модули.</param>
    /// <remarks>
    /// Список собирается заново после установки и удаления, а поиск в окне остаётся тем же: новая
    /// строка встаёт под тот отбор, что был у прежних. Выбор переживает пересборку: плагин, который
    /// смотрели, остаётся выбранным и после установки соседа, и после «Сохранить».
    /// </remarks>
    public void Show(IReadOnlyList<PluginCard> cards, IReadOnlyList<PluginCard> builtIn)
    {
        var wanted = _selected?.Plugin.Id;

        _cards = cards;
        _builtIn = builtIn;

        foreach (var card in All())
            card.Narrow(_query);

        Group();

        Choose(All().FirstOrDefault(card => string.Equals(card.Plugin.Id, wanted, StringComparison.Ordinal)));
        Arrange();
    }

    /// <summary>Отбирает строки поиском; пока он идёт, группы раскрыты и не сворачиваются.</summary>
    /// <param name="query">Что искать; пусто — показать всё.</param>
    public void Narrow(string? query)
    {
        _query = SettingsSearch.Normalize(query);

        foreach (var card in All())
            card.Narrow(_query);

        foreach (var group in Groups)
            group.CanFold = _query is null;

        Arrange();
    }

    /// <summary>
    /// Выбирает плагин по идентификатору; незнакомый ничего не меняет.
    /// </summary>
    /// <param name="pluginId">Кого выбрать.</param>
    /// <remarks>Так окно настроек возвращает выбор, с которым студию перезапустили.</remarks>
    public void Pick(string? pluginId)
    {
        if (All().FirstOrDefault(card => string.Equals(card.Plugin.Id, pluginId, StringComparison.Ordinal)) is { } found)
            Choose(found);
    }

    /// <summary>Возвращает группам то, как их свернул и раскрыл человек.</summary>
    /// <param name="folded">Ключ группы и раскрыта ли она.</param>
    /// <remarks>
    /// Группам, а не новым группам: группа подписана на свои карточки, пока жива, и пересборка
    /// поверх тех же карточек оставляла прежние группы висеть на них — считать галочки, которых
    /// никто не видит. Группы заводятся заново только с карточками, в <see cref="Show"/>.
    /// </remarks>
    public void Fold(IReadOnlyDictionary<string, bool> folded)
    {
        ArgumentNullException.ThrowIfNull(folded);

        foreach (var (key, open) in folded)
            _folded[key] = open;

        foreach (var group in Groups)
        {
            if (_folded.TryGetValue(group.Key, out var open))
                group.IsExpanded = open;
        }

        Arrange();
    }

    private IEnumerable<PluginCard> All() => _cards.Concat(_builtIn);

    /// <summary>
    /// Раскладывает плагины по группам: внешние, языковые пакеты, встроенные.
    /// </summary>
    /// <remarks>
    /// Встроенные свёрнуты, если есть что-то ещё, — они справочные и стоят последними; одни они —
    /// раскрыты, иначе страница показывала бы пустоту под заголовком. Что человек свернул или
    /// раскрыл сам, помнится до закрытия окна.
    /// </remarks>
    private void Group()
    {
        foreach (var group in Groups)
            group.Toggled -= OnGroupToggled;

        var external = _cards.Where(card => !card.IsLanguagePack).ToList();
        var languages = _cards.Where(card => card.IsLanguagePack).ToList();
        var only = external.Count == 0 && languages.Count == 0;

        Groups =
        [
            .. new[]
            {
                Make("external", "plugins.group.external", external, counts: true, open: true),
                Make("languages", "plugins.group.languages", languages, counts: true, open: true),
                Make("builtin", "plugins.group.builtin", _builtIn, counts: false, open: only),
            }.Where(group => group.Cards.Count > 0),
        ];

        foreach (var group in Groups)
            group.Toggled += OnGroupToggled;

        Notify(nameof(Groups));

        PluginGroup Make(string key, string title, IReadOnlyList<PluginCard> cards, bool counts, bool open) =>
            new(key, Localizer.Instance[title], cards, counts, _folded.TryGetValue(key, out var kept) ? kept : open)
            {
                CanFold = _query is null,
            };
    }

    /// <summary>
    /// Собирает строки списка из групп: заголовок, затем видимые плагины, если группа раскрыта
    /// или идёт поиск.
    /// </summary>
    /// <remarks>
    /// Выбранный плагин, которого поиск больше не показывает, уступает выбор первому видимому:
    /// подробности идут за отбором. Спрятанный свёрнутой группой — остаётся выбранным: человек
    /// свернул группу, а не передумал смотреть на плагин.
    /// <para>
    /// Строки правятся на месте, а не собираются заново: заголовок, по которому щёлкнули, остаётся
    /// тем же контролом, и каретка, взятая им при щелчке, не пропадает вместе со строкой.
    /// </para>
    /// </remarks>
    private void Arrange()
    {
        var wanted = new List<object>();

        foreach (var group in Groups)
        {
            var shown = group.Cards.Where(card => card.IsShown).ToList();

            if (shown.Count == 0)
                continue;

            wanted.Add(group);

            if (group.IsExpanded || _query is not null)
                wanted.AddRange(shown);
        }

        _arranging = true;

        try
        {
            for (var at = Rows.Count - 1; at >= 0; at--)
            {
                if (!wanted.Contains(Rows[at]))
                    Rows.RemoveAt(at);
            }

            for (var at = 0; at < wanted.Count; at++)
            {
                if (at < Rows.Count && ReferenceEquals(Rows[at], wanted[at]))
                    continue;

                var from = Rows.IndexOf(wanted[at]);

                if (from >= 0)
                    Rows.Move(from, at);
                else
                    Rows.Insert(at, wanted[at]);
            }
        }
        finally
        {
            _arranging = false;
        }

        if (_selected is not { IsShown: true })
            Choose(Rows.OfType<PluginCard>().FirstOrDefault());

        // Список, потерявший строку на время пересборки, получает выбор обратно.
        Notify(nameof(Selected));
    }

    private void Choose(PluginCard? card)
    {
        if (ReferenceEquals(card, _selected))
            return;

        _selected = card;

        Notify(nameof(Selected));
        Notify(nameof(Card));
        Notify(nameof(HasCard));
    }

    private void OnGroupToggled(object? sender, EventArgs e)
    {
        if (sender is not PluginGroup group)
            return;

        _folded[group.Key] = group.IsExpanded;
        Arrange();
    }

    private void Notify([CallerMemberName] string? property = null) => PropertyChanged.Raise(this, property);
}
