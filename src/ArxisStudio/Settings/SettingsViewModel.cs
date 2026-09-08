using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArxisStudio.Extensibility;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;

namespace ArxisStudio.Settings;

/// <summary>
/// Узел дерева разделов — проекция страницы, а не сама страница.
/// </summary>
/// <remarks>
/// Поиск прячет часть дерева, и прятать его правкой самих страниц нельзя:
/// страница держит накопленные правки, а узел — только показ. Отфильтровав
/// проекцию, окно ничего не теряет.
/// </remarks>
/// <param name="Page">Страница, которую узел показывает.</param>
/// <param name="Children">Показанные дети — не обязательно все дети страницы.</param>
public sealed record SettingsNode(ISettingsPage Page, IReadOnlyList<SettingsNode> Children)
{
    /// <summary>Подпись в дереве.</summary>
    public string Title => Page.Title;

    /// <summary>Значок в дереве.</summary>
    public Avalonia.Media.Geometry? Icon => Page.Icon;
}

/// <summary>
/// Что показывает окно настроек и что оно помнит между «Сохранить» и «Отменой».
/// </summary>
/// <remarks>
/// Правки копят сами страницы, а модель окна их только собирает: так «Отмена»
/// возвращает всё разом, а «Сохранить» пишет в объявленном порядке — сперва
/// настройки студии, потом расширения.
/// </remarks>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly List<ISettingsPage> _pages = [];
    private readonly List<string> _problems = [];

    private string _search = string.Empty;
    private SettingsNode? _selected;
    private string? _complaint;

    /// <summary>
    /// Собирает страницы поверх служб студии.
    /// </summary>
    /// <param name="studio">Настройки студии.</param>
    /// <param name="values">Общее хранилище настроек расширений.</param>
    /// <param name="extensions">Кто объявляет настройки: модули, затем плагины.</param>
    /// <param name="announce">Кому сказать о записанном; null — молча.</param>
    /// <param name="plugins">Менеджер плагинов; null — окно без него.</param>
    /// <remarks>
    /// Менеджер приходит собранным, а не строится здесь: ему нужны диалоги
    /// выбора папки и вопросы человеку, а это дело окна. Необязателен он не
    /// ради теста, а ради правды: без живого хоста менеджер соврал бы о
    /// применённом, и лучше не показать его вовсе.
    /// </remarks>
    public SettingsViewModel(
        ISettingsStore studio,
        PluginSettingsStore values,
        IReadOnlyList<InstalledPlugin> extensions,
        Action<string, string>? announce = null,
        PluginsPage? plugins = null)
    {
        ArgumentNullException.ThrowIfNull(studio);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(extensions);

        // Страница расширения заводится только тому, кто настройки объявил:
        // пустой узел в дереве обещал бы страницу и открывал пустоту.
        var pages = extensions
            .Where(extension => extension.Manifest?.Contributions.Settings.Count > 0)
            .Select(extension => new ExtensionPage(extension, values, announce))
            .ToList();

        var appearance = new AppearancePage(studio)
        {
            Complain = Say,
            Relabelled = () =>
            {
                foreach (var page in pages)
                    page.Relabel();

                Refresh();
            },
        };

        _pages.Add(appearance);

        // Плагины между оформлением и настройками расширений: сперва студия
        // целиком, потом состав, потом подстройка того, что в составе.
        if (plugins is not null)
            _pages.Add(plugins);

        if (pages.Count > 0)
            _pages.Add(new ExtensionsPage(pages));

        Refresh();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;


    /// <summary>Дерево разделов, каким его сейчас видно.</summary>
    public ObservableCollection<SettingsNode> Nodes { get; } = [];

    /// <summary>Что открыто справа.</summary>
    public SettingsNode? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
                return;

            _selected = value;
            Notify();
            Notify(nameof(Page));
        }
    }

    /// <summary>Страница, показанная справа; null — не выбрано ничего.</summary>
    public ISettingsPage? Page => _selected?.Page;

    /// <summary>Строка поиска.</summary>
    public string Search
    {
        get => _search;
        set
        {
            if (string.Equals(_search, value, StringComparison.Ordinal))
                return;

            _search = value ?? string.Empty;
            Notify();
            Refresh();
        }
    }

    /// <summary>Поиск ничего не нашёл.</summary>
    public bool IsEmpty => Nodes.Count == 0;

    /// <summary>Что сказать человеку в подвале; null — сказать нечего.</summary>
    public string? Complaint
    {
        get => _complaint;
        private set
        {
            _complaint = value;
            Notify();
            Notify(nameof(HasComplaint));
        }
    }

    /// <summary>Есть что сказать.</summary>
    public bool HasComplaint => !string.IsNullOrEmpty(_complaint);

    /// <summary>Хоть где-то есть несохранённое.</summary>
    public bool HasChanges => _pages.Any(page => page.HasChanges);

    /// <summary>
    /// Пишет накопленное.
    /// </summary>
    /// <returns><c>true</c>, если записалось всё и окно можно закрывать.</returns>
    /// <remarks>
    /// Порядок объявлен: сперва настройки студии — одна запись файла, — потом
    /// расширения, в порядке дерева. Непрошедшее не прячется: строка остаётся
    /// правленой, причина попадает в подвал, окно не закрывается. Сказать
    /// «сохранено» о том, что не записалось, хуже, чем не закрыться.
    /// </remarks>
    public async Task<bool> SaveAsync()
    {
        _problems.Clear();

        foreach (var page in _pages)
            await page.CommitAsync(_problems);

        Complaint = _problems.Count == 0
            ? null
            : $"{Localizer.Instance["settings.save.failed"]}: {string.Join("; ", _problems)}";

        Notify(nameof(HasChanges));
        return _problems.Count == 0;
    }

    /// <summary>Возвращает всё, что было при открытии окна.</summary>
    public void Revert()
    {
        foreach (var page in _pages)
            page.Revert();

        Complaint = null;
        Notify(nameof(HasChanges));
    }

    /// <summary>
    /// Отбирает страницы по строке поиска.
    /// </summary>
    /// <param name="pages">Все страницы верхнего уровня.</param>
    /// <param name="query">Что ищут; пусто — всё дерево.</param>
    /// <remarks>
    /// Правило простое и объяснимое: страница видна, если совпала сама или
    /// совпал кто-то под ней. Совпавшая ветка показывает всех детей — человек
    /// искал её целиком; ветка, уцелевшая ради ребёнка, показывает только
    /// совпавших.
    /// <para>
    /// Чистая функция и статическая нарочно: у поиска своя проверка, без окна
    /// и без единого контрола.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<SettingsNode> Filter(IReadOnlyList<ISettingsPage> pages, string? query)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (string.IsNullOrWhiteSpace(query))
            return [.. pages.Select(page => new SettingsNode(page, All(page)))];

        var needle = query.Trim();
        var found = new List<SettingsNode>();

        foreach (var page in pages)
        {
            if (Matches(page, needle))
            {
                found.Add(new SettingsNode(page, All(page)));
                continue;
            }

            var children = page.Children
                .Where(child => Matches(child, needle))
                .Select(child => new SettingsNode(child, All(child)))
                .ToList();

            if (children.Count > 0)
                found.Add(new SettingsNode(page, children));
        }

        return found;
    }

    private static IReadOnlyList<SettingsNode> All(ISettingsPage page) =>
        [.. page.Children.Select(child => new SettingsNode(child, All(child)))];

    private static bool Matches(ISettingsPage page, string needle) =>
        page.Terms.Any(term => term.Contains(needle, StringComparison.CurrentCultureIgnoreCase));

    private void Say(string complaint) => Complaint = complaint;

    /// <summary>Пересобирает дерево и удерживает выбранное, если оно уцелело.</summary>
    /// <summary>Открывает раздел по имени его страницы.</summary>
    /// <param name="pageId">Имя страницы; неизвестное — оставляет как есть.</param>
    /// <remarks>
    /// Так в менеджер плагинов ведёт строка «Плагины» в полосе Welcome: окно
    /// одно, а вход в него не один, и каждый вправе назвать свой раздел.
    /// </remarks>
    public void Select(string pageId)
    {
        if (Flat(Nodes).FirstOrDefault(node => string.Equals(node.Page.Id, pageId, StringComparison.Ordinal)) is { } found)
            Selected = found;
    }

    private void Refresh()
    {
        var wanted = _selected?.Page.Id;

        Nodes.Clear();

        foreach (var node in Filter(_pages, _search))
            Nodes.Add(node);

        Notify(nameof(IsEmpty));

        Selected = Flat(Nodes).FirstOrDefault(node => node.Page.Id == wanted)
            ?? Flat(Nodes).FirstOrDefault(node => node.Children.Count == 0);
    }

    private static IEnumerable<SettingsNode> Flat(IEnumerable<SettingsNode> nodes) =>
        nodes.SelectMany(node => Flat(node.Children).Prepend(node));

    private void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
