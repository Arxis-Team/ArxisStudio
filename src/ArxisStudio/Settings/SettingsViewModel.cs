using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArxisStudio.Extensibility;
using ArxisStudio.Shell;
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
/// <para>
/// Узел говорит и о несохранённом на своей странице — точкой, как Rider отмечает правленый
/// раздел. Спрашивает он страницу, а не помнит сам: узлы пересобираются на каждый запрос
/// поиска, и память узла пропадала бы вместе с ним.
/// </para>
/// </remarks>
public sealed class SettingsNode : INotifyPropertyChanged
{
    /// <summary>Заводит узел над страницей.</summary>
    /// <param name="page">Страница, которую узел показывает.</param>
    /// <param name="children">Показанные дети — не обязательно все дети страницы.</param>
    public SettingsNode(ISettingsPage page, IReadOnlyList<SettingsNode> children)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(children);

        Page = page;
        Children = children;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Страница, которую узел показывает.</summary>
    public ISettingsPage Page { get; }

    /// <summary>Показанные дети — не обязательно все дети страницы.</summary>
    public IReadOnlyList<SettingsNode> Children { get; }

    /// <summary>Подпись в дереве.</summary>
    public string Title => Page.Title;

    /// <summary>Значок в дереве.</summary>
    public Avalonia.Media.Geometry? Icon => Page.Icon;

    /// <summary>На странице узла есть несохранённое.</summary>
    public bool IsModified => Page.HasChanges;

    /// <summary>
    /// Состояние узла для средств доступности: точку глазом видно, а диктору о ней надо сказать.
    /// </summary>
    public string ModifiedStatus => IsModified ? Localizer.Instance["settings.modified"] : string.Empty;

    /// <summary>Страница могла сменить несохранённое — узел спрашивает её заново.</summary>
    public void Touch()
    {
        PropertyChanged.Raise(this, nameof(IsModified), nameof(ModifiedStatus));
    }

    /// <summary>Подпись узла — её показывает крошка в шапке страницы и читает диктор.</summary>
    public override string ToString() => Title;
}

/// <summary>
/// Что показывает окно настроек и что оно помнит между «Сохранить» и «Отменой».
/// </summary>
/// <remarks>
/// Правки копят сами страницы, а модель окна их только собирает: так «Отмена»
/// возвращает всё разом, а «Сохранить» пишет в объявленном порядке — сперва
/// настройки студии, потом расширения.
/// <para>
/// Окно помнит и дорогу: страницы, по которым человек прошёл, лежат в истории, и «назад»
/// возвращает туда, откуда пришли, — как стрелки в шапке настроек Rider. Нужны они с тех пор,
/// как одна страница ведёт на другую: плагин — на свои настройки, ветка — на страницу ребёнка.
/// </para>
/// </remarks>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly List<ISettingsPage> _pages = [];
    private readonly List<string> _problems = [];
    private readonly List<string> _back = [];
    private readonly List<string> _forward = [];

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
    /// <param name="keys">Страница сочетаний клавиш; null — окно без неё.</param>
    /// <remarks>
    /// Менеджер приходит собранным, а не строится здесь: ему нужны диалоги
    /// выбора папки и вопросы человеку, а это дело окна. Необязателен он не
    /// ради теста, а ради правды: без живого хоста менеджер соврал бы о
    /// применённом, и лучше не показать его вовсе.
    /// <para>
    /// Страница клавиш необязательна по той же причине: сочетания раздаёт окно
    /// студии, и из Welcome, где его ещё нет, показать было бы нечего.
    /// </para>
    /// </remarks>
    public SettingsViewModel(
        ISettingsStore studio,
        PluginSettingsStore values,
        IReadOnlyList<InstalledPlugin> extensions,
        Action<string, string>? announce = null,
        PluginsPage? plugins = null,
        KeysPage? keys = null)
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

        // Клавиши — рядом с оформлением: это тоже настройка всей студии, а не
        // расширения, и стоит она до состава плагинов.
        if (keys is not null)
            _pages.Add(keys);

        // Плагины между оформлением и настройками расширений: сперва студия
        // целиком, потом состав, потом подстройка того, что в составе.
        if (plugins is not null)
            _pages.Add(plugins);

        if (pages.Count > 0)
            _pages.Add(new ExtensionsPage(pages));

        // Ветка расширений собирает события детей сама, поэтому слушать довольно верхний уровень.
        foreach (var page in _pages)
            page.Changed += OnPageChanged;

        Refresh();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;


    /// <summary>Дерево разделов, каким его сейчас видно.</summary>
    public ObservableCollection<SettingsNode> Nodes { get; } = [];

    /// <summary>
    /// Что открыто справа.
    /// </summary>
    /// <remarks>
    /// Сюда пишет дерево, когда человек выбирает раздел, и такой выбор ложится в историю.
    /// Выбор, который сделало само окно, — после поиска, по «назад» или при открытии на
    /// названном разделе, — историю не трогает.
    /// </remarks>
    public SettingsNode? Selected
    {
        get => _selected;
        set => Show(value, remember: true);
    }

    /// <summary>Страница, показанная справа; null — не выбрано ничего.</summary>
    public ISettingsPage? Page => _selected?.Page;

    /// <summary>Узлы от корня дерева до выбранного — путь в шапке страницы.</summary>
    public IReadOnlyList<SettingsNode> Path =>
        _selected is { } selected ? PathTo(Nodes, selected) ?? [selected] : [];

    /// <summary>Есть куда вернуться.</summary>
    public bool CanGoBack => _back.Count > 0;

    /// <summary>Есть куда пойти обратно после «назад».</summary>
    public bool CanGoForward => _forward.Count > 0;

    /// <summary>На открытой странице есть что сбросить.</summary>
    public bool CanReset => _selected?.Page.HasChanges == true;

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
    /// <param name="live">
    /// Применить записанное к работающей студии; <c>false</c> — только записать: студия
    /// перезапускается и прочтёт его сама.
    /// </param>
    /// <returns><c>true</c>, если записалось всё и окно можно закрывать.</returns>
    /// <remarks>
    /// Порядок объявлен: сперва настройки студии — одна запись файла, — потом
    /// расширения, в порядке дерева. Непрошедшее не прячется: строка остаётся
    /// правленой, причина попадает в подвал, окно не закрывается. Сказать
    /// «сохранено» о том, что не записалось, хуже, чем не закрыться.
    /// </remarks>
    public async Task<bool> SaveAsync(bool live = true)
    {
        _problems.Clear();

        foreach (var page in _pages)
            await page.CommitAsync(_problems, live);

        Complaint = _problems.Count == 0
            ? null
            : $"{Localizer.Instance["settings.save.failed"]}: {string.Join("; ", _problems)}";

        Changes();
        return _problems.Count == 0;
    }

    /// <summary>Возвращает всё, что было при открытии окна.</summary>
    public void Revert()
    {
        foreach (var page in _pages)
            page.Revert();

        Complaint = null;
        Changes();
    }

    /// <summary>
    /// «Сбросить» в шапке: забывает правки открытой страницы, не трогая остальных.
    /// </summary>
    /// <remarks>
    /// Как Reset в шапке настроек Rider. «Отмена» внизу забывает всё и закрывает окно, а здесь
    /// человек передумал об одной странице и остаётся работать с другими.
    /// </remarks>
    public void Reset()
    {
        _selected?.Page.Revert();
        Changes();
    }

    /// <summary>
    /// Открывает раздел по имени его страницы и запоминает, откуда пришли.
    /// </summary>
    /// <param name="pageId">Имя страницы; неизвестное — оставляет как есть.</param>
    /// <remarks>
    /// Так ведут друг на друга страницы: крошка в шапке, ссылка ветки на ребёнка, плагин на свои
    /// настройки. Спрятанную поиском страницу открыть нельзя, не показав, — поэтому поиск
    /// очищается: человек просил эту страницу, а не отбор, который её прятал.
    /// </remarks>
    public void Open(string pageId) => Go(pageId, remember: true);

    /// <summary>Возвращается на страницу, открытую перед этой.</summary>
    public void Back() => Step(_back, _forward);

    /// <summary>Идёт обратно туда, откуда вернулись «назад».</summary>
    public void Forward() => Step(_forward, _back);

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

        if (SettingsSearch.Normalize(query) is not { } needle)
            return [.. pages.Select(page => new SettingsNode(page, All(page)))];

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

    private static bool Matches(ISettingsPage page, string needle) => SettingsSearch.Matches(page.Terms, needle);

    /// <summary>Говорит подвалом окна.</summary>
    /// <param name="complaint">Что сказать.</param>
    /// <remarks>
    /// Подвал говорит о том, что не вышло: правка страницы, не легшая на диск, или перезапуск, не
    /// состоявшийся из этого окна.
    /// </remarks>
    public void Say(string complaint) => Complaint = complaint;

    /// <summary>Открывает раздел по имени его страницы.</summary>
    /// <param name="pageId">Имя страницы; неизвестное — оставляет как есть.</param>
    /// <remarks>
    /// Так в менеджер плагинов ведёт строка «Плагины» в полосе Welcome: окно
    /// одно, а вход в него не один, и каждый вправе назвать свой раздел. Истории такой выбор
    /// не пишет: окно на нём открылось, а не пришло сюда с другой страницы.
    /// </remarks>
    public void Select(string pageId)
    {
        if (Find(pageId) is { } found)
            Show(found, remember: false);
    }

    /// <summary>Пересобирает дерево и удерживает выбранное, если оно уцелело.</summary>
    private void Refresh()
    {
        var wanted = _selected?.Page.Id;

        Nodes.Clear();

        foreach (var node in Filter(_pages, _search))
            Nodes.Add(node);

        Notify(nameof(IsEmpty));
        Narrow();

        Show(
            Flat(Nodes).FirstOrDefault(node => node.Page.Id == wanted)
                ?? Flat(Nodes).FirstOrDefault(node => node.Children.Count == 0),
            remember: false);

        // Узлы новые, а выбранная страница могла остаться прежней — путь в шапке строится по
        // узлам, и старые узлы в нём держать нельзя.
        Notify(nameof(Path));
    }

    /// <summary>
    /// Сужает строки страниц по поиску — тем же правилом, что дерево, уровнем ниже.
    /// </summary>
    /// <remarks>
    /// Совпала подпись страницы или её предка — страница показывается вся: её и искали. Уцелела
    /// ради своих строк — показывает только совпавшие, как Project Settings у Unity: найденная
    /// настройка не тонет среди соседей.
    /// </remarks>
    private void Narrow()
    {
        var needle = SettingsSearch.Normalize(_search);

        foreach (var page in _pages)
            Down(page, needle, whole: needle is null);

        static void Down(ISettingsPage page, string? needle, bool whole)
        {
            var all = whole || SettingsSearch.Matches(page.Title, needle!);

            page.Narrow(all ? null : needle);

            foreach (var child in page.Children)
                Down(child, needle, all);
        }
    }

    /// <summary>Ставит узел справа; запоминает прежний, если так просили.</summary>
    private void Show(SettingsNode? node, bool remember)
    {
        if (ReferenceEquals(_selected, node))
            return;

        // В историю идёт только уход с одной страницы на другую. Узел, пропавший из дерева на миг
        // пересборки, — не уход: дерево само отдаёт пустой выбор, пока его узлы меняются.
        if (remember && _selected is { } left && node is not null
            && !string.Equals(left.Page.Id, node.Page.Id, StringComparison.Ordinal))
        {
            _back.Add(left.Page.Id);
            _forward.Clear();
        }

        _selected = node;

        Notify(nameof(Selected));
        Notify(nameof(Page));
        Notify(nameof(Path));
        Notify(nameof(CanReset));
        Notify(nameof(CanGoBack));
        Notify(nameof(CanGoForward));
    }

    /// <summary>Шаг по истории: из одной стопки на страницу, а покинутую — в другую.</summary>
    private void Step(List<string> from, List<string> to)
    {
        if (from.Count == 0)
            return;

        var target = from[^1];

        from.RemoveAt(from.Count - 1);

        if (_selected is { } here)
            to.Add(here.Page.Id);

        Go(target, remember: false);

        Notify(nameof(CanGoBack));
        Notify(nameof(CanGoForward));
    }

    /// <summary>Открывает страницу по имени; спрятанную поиском — очистив его.</summary>
    private void Go(string pageId, bool remember)
    {
        if (Find(pageId) is null && _search.Length > 0)
        {
            _search = string.Empty;
            Notify(nameof(Search));
            Refresh();
        }

        if (Find(pageId) is { } found)
            Show(found, remember);
    }

    private SettingsNode? Find(string pageId) =>
        Flat(Nodes).FirstOrDefault(node => string.Equals(node.Page.Id, pageId, StringComparison.Ordinal));

    /// <summary>Правка на какой-то странице: узлы, шапка и подвал спрашивают заново.</summary>
    private void OnPageChanged(object? sender, EventArgs e) => Changes();

    private void Changes()
    {
        foreach (var node in Flat(Nodes))
            node.Touch();

        Notify(nameof(HasChanges));
        Notify(nameof(CanReset));
    }

    private static IReadOnlyList<SettingsNode>? PathTo(IEnumerable<SettingsNode> level, SettingsNode target)
    {
        foreach (var node in level)
        {
            if (ReferenceEquals(node, target))
                return [node];

            if (PathTo(node.Children, target) is { } below)
                return [node, .. below];
        }

        return null;
    }

    private static IEnumerable<SettingsNode> Flat(IEnumerable<SettingsNode> nodes) =>
        nodes.SelectMany(node => Flat(node.Children).Prepend(node));

    private void Notify([CallerMemberName] string? property = null) => PropertyChanged.Raise(this, property);
}
