using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ArxisStudio.Extensibility;
using ArxisStudio.Icons;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using ArxisStudio.ViewModels;
using Avalonia.Media;

namespace ArxisStudio.Settings;

/// <summary>
/// Чем страница плагинов просит окно: выбрать папку, выбрать архив, задать
/// вопрос, показать каталог в проводнике.
/// </summary>
/// <remarks>
/// Диалоги — дело окна, а не страницы: у страницы нет ни владельца для
/// модального вопроса, ни доступа к хранилищу файлов платформы. Интерфейсом, а
/// не четырьмя делегатами, — вместе они одно: «спроси человека», и подменяются
/// в тесте тоже вместе.
/// </remarks>
public interface IPluginDialogs
{
    /// <summary>Просит выбрать папку плагина; null — передумали.</summary>
    /// <param name="title">Заголовок окна выбора.</param>
    Task<string?> AskFolderAsync(string title);

    /// <summary>Просит выбрать архив <c>.axplugin</c>; null — передумали.</summary>
    /// <param name="title">Заголовок окна выбора.</param>
    Task<string?> AskArchiveAsync(string title);

    /// <summary>Задаёт вопрос с двумя ответами; <c>true</c> — согласились.</summary>
    /// <param name="title">Заголовок вопроса.</param>
    /// <param name="message">Сам вопрос.</param>
    /// <param name="confirm">Подпись согласия.</param>
    /// <param name="danger">Красить ли согласие как опасное.</param>
    Task<bool> ConfirmAsync(string title, string message, string confirm, bool danger);

    /// <summary>Показывает путь средствами системы.</summary>
    /// <param name="path">Что показать.</param>
    void Reveal(string path);
}

/// <summary>
/// Страница «Плагины»: менеджер расширений, приехавших со стороны.
/// </summary>
/// <remarks>
/// Переехала из экрана Welcome целиком — вместе с карточкой, вопросами о
/// зависимых и установкой из папки и архива. Причина та же, что была у
/// настроек: Welcome закрывается навсегда, стоит войти в студию, и до
/// менеджера из работающей студии было не добраться.
/// <para>
/// Действия разделены честно, а не поровну. Включение и выключение копятся до
/// «Сохранить»: галочку можно поставить и передумать. Установка и удаление
/// случаются сразу — они файловые, и отменять «Отменой» было бы обещанием,
/// которого страница не сдержит.
/// </para>
/// <para>
/// Оба вида доходят до <b>работающей</b> студии, а не только до диска:
/// выключенный опускается, включённый поднимается, поставленный поверх
/// перезагружается. Не отпустился — плагин ждёт перезапуска студии, и окно его предлагает, а не
/// молчит.
/// </para>
/// <para>
/// Устроена, как Plugins → Installed у Rider: слева список с группами — внешние, языковые
/// пакеты, встроенные — и флажком в строке, справа подробности выбранного. Встроенные модули
/// стоят здесь только для справки: их не ставят, не выключают и не удаляют, они приезжают со
/// студией, — но видно их версию, что они добавляют и дорогу к их настройкам. Карточек у них
/// прежде не было вовсе (запись 105); группу завёл человек, когда менеджер стал справочником, а
/// не только пультом.
/// </para>
/// </remarks>
public sealed class PluginsPage : ISettingsPage, INotifyPropertyChanged
{
    private readonly PluginCatalog _catalog;
    private readonly StudioPlugins _extensions;
    private readonly IPluginDialogs _dialogs;
    private readonly IReadOnlyList<InstalledPlugin> _builtIn;

    /// <summary>Какие группы человек свернул или раскрыл сам — это переживает пересборку списка.</summary>
    private readonly Dictionary<string, bool> _folded = new(StringComparer.Ordinal);

    /// <summary>
    /// Чьи галочки записаны перед перезапуском и не применены: не состоится он — их применяют вживую.
    /// </summary>
    private readonly HashSet<string> _unapplied = new(StringComparer.Ordinal);

    private string? _status;
    private string? _query;
    private PluginCard? _selected;
    private bool _arranging;

    /// <summary>Собирает страницу поверх каталога и живых расширений.</summary>
    /// <param name="catalog">Каталог плагинов на диске.</param>
    /// <param name="extensions">Расширения студии: живой хост.</param>
    /// <param name="dialogs">Кто спрашивает человека.</param>
    /// <param name="builtIn">
    /// Встроенные модули; null — те, что подняла служба расширений. Окно передаёт тех же, что
    /// объявляют настройки, — из Welcome служба ещё может не знать своих модулей.
    /// </param>
    public PluginsPage(
        PluginCatalog catalog,
        StudioPlugins extensions,
        IPluginDialogs dialogs,
        IReadOnlyList<InstalledPlugin>? builtIn = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(dialogs);

        _catalog = catalog;
        _extensions = extensions;
        _dialogs = dialogs;
        _builtIn = builtIn ?? extensions.Modules;

        Refresh();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <summary>Установленные плагины — те, что ставят, выключают и удаляют.</summary>
    public ObservableCollection<PluginCard> Cards { get; } = [];

    /// <summary>Встроенные модули — для справки.</summary>
    public IReadOnlyList<PluginCard> BuiltIn { get; private set; } = [];

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

    /// <inheritdoc/>
    public string Id => "studio.plugins";

    /// <inheritdoc/>
    public string Title => Localizer.Instance["plugins.title"];

    /// <inheritdoc/>
    public Geometry? Icon => AxIcons.Plugin;

    /// <inheritdoc/>
    public IReadOnlyList<ISettingsPage> Children => [];

    /// <summary>
    /// По чему страницу находит поиск: подпись раздела и сами плагины.
    /// </summary>
    /// <remarks>
    /// Имя, издатель и идентификатор: последним человек ищет из документации
    /// соседа, где плагин назван так же, как в манифесте.
    /// <para>
    /// Метки — сами теги, а не их подписи: тег один на все языки, и найденное
    /// по нему не должно меняться вместе с языком интерфейса. Поэтому «tools»
    /// приводит к терминалу и в русской студии — там же, где документация
    /// автора и его манифест.
    /// </para>
    /// </remarks>
    public IEnumerable<string> Terms => Cards.Concat(BuiltIn).SelectMany(card => card.Terms).Append(Title);

    /// <inheritdoc/>
    public bool HasChanges => Cards.Any(card => card.IsChanged);

    /// <summary>Внешних плагинов не установлено.</summary>
    public bool IsEmpty => Cards.Count == 0;

    /// <summary>
    /// Чем кончилось последнее действие; null — сказать нечего.
    /// </summary>
    /// <remarks>
    /// Своя строка, а не жалоба в подвале окна: подвал говорит о том, что не
    /// записалось, и красен собой, а «плагин установлен» — хорошая новость.
    /// </remarks>
    public string? Status
    {
        get => _status;
        private set
        {
            _status = value;
            Notify();
            Notify(nameof(HasStatus));
        }
    }

    /// <summary>Есть что сказать о последнем действии.</summary>
    public bool HasStatus => !string.IsNullOrEmpty(_status);

    /// <inheritdoc/>
    public Task CommitAsync(ICollection<string> problems) => CommitAsync(problems, live: true);

    /// <inheritdoc/>
    /// <remarks>
    /// Перед перезапуском (<paramref name="live"/> = <c>false</c>) галочки только записываются:
    /// опускать и поднимать плагины в процессе, который через миг закроется, — это ожидание
    /// выгрузки и десяток проходов сборщика мусора ради состояния, которое новая копия и так
    /// прочтёт с диска. Записанное помнится до <see cref="CatchUpAsync"/>: перезапуск может и не
    /// состояться.
    /// </remarks>
    public async Task CommitAsync(ICollection<string> problems, bool live)
    {
        ArgumentNullException.ThrowIfNull(problems);

        var changed = Cards.Where(card => card.IsChanged).ToList();

        if (changed.Count == 0)
            return;

        // Применяется только записанное: галочка, не дошедшая до диска, после перезапуска
        // вернулась бы прежней, а студия до него жила бы по новой — два состояния сразу.
        var saved = new List<PluginCard>();

        foreach (var card in changed)
        {
            if (_catalog.SetEnabled(card.Plugin.Id, card.IsOn) is { } refusal)
                problems.Add(refusal);
            else
                saved.Add(card);
        }

        if (!live)
        {
            _unapplied.UnionWith(saved.Select(card => card.Plugin.Id));
            Refresh();
            return;
        }

        var complaint = await _extensions.ApplyAsync(
            saved.Where(card => !card.IsOn).Select(card => card.Plugin.Id).ToList(),
            saved.Where(card => card.IsOn).Select(card => card.Plugin.Id).ToList());

        if (complaint is not null)
            problems.Add(complaint);

        Refresh();
    }

    /// <summary>
    /// Применяет вживую галочки, записанные перед перезапуском, который не состоялся.
    /// </summary>
    /// <remarks>
    /// Без этого студия после отказа жила бы по-старому, а диск помнил по-новому: выключенный плагин
    /// работал бы с панелями и документами, а его строка стояла бы выключенной. Что применять,
    /// решает диск, а не память о галочке: между записью и отказом её могли записать ещё раз.
    /// </remarks>
    public async Task CatchUpAsync()
    {
        if (_unapplied.Count == 0)
            return;

        var touched = _catalog.Scan().Where(plugin => _unapplied.Contains(plugin.Id)).ToList();

        _unapplied.Clear();

        if (await _extensions.ApplyAsync(
                [.. touched.Where(plugin => !plugin.IsEnabled).Select(plugin => plugin.Id)],
                [.. touched.Where(plugin => plugin.IsEnabled).Select(plugin => plugin.Id)]) is { } complaint)
        {
            Status = complaint;
        }

        Refresh();
    }

    /// <inheritdoc/>
    public void Revert()
    {
        foreach (var card in Cards)
            card.Revert();

        Status = null;
        Notify(nameof(HasChanges));
    }

    /// <summary>
    /// Ставит плагин из папки — сразу и с подъёмом в работающей студии.
    /// </summary>
    /// <remarks>
    /// Установка поверх уже стоящего — обычный способ обновиться, и говорить о
    /// ней надо иначе, чем о первой: иначе человек не поймёт, заменил он свою
    /// версию или поставил вторую.
    /// </remarks>
    public async Task InstallFromFolderAsync()
    {
        if (await _dialogs.AskFolderAsync(Localizer.Instance["plugins.install"]) is not { } source)
            return;

        await AcceptAsync(_catalog.InstallFromDirectory(source, replace: true));
    }

    /// <summary>Ставит плагин из архива <c>.axplugin</c> — той же дорогой.</summary>
    public async Task InstallFromArchiveAsync()
    {
        if (await _dialogs.AskArchiveAsync(Localizer.Instance["plugins.installarchive"]) is not { } archive)
            return;

        await AcceptAsync(_catalog.InstallFromArchive(archive, replace: true));
    }

    /// <summary>Показывает папку плагинов средствами системы.</summary>
    public void OpenFolder()
    {
        Directory.CreateDirectory(_catalog.Root);
        _dialogs.Reveal(_catalog.Root);
    }

    /// <summary>Показывает папку одного плагина средствами системы.</summary>
    /// <param name="card">Чью папку.</param>
    public void RevealFolder(PluginCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        _dialogs.Reveal(card.Folder);
    }

    /// <summary>
    /// «Вернуть» в подробностях: забывает непринятую галочку одного плагина.
    /// </summary>
    /// <param name="card">Чью галочку.</param>
    /// <remarks>
    /// Зависимых, выключенных вместе с ним по вопросу, не трогает: их человек выключил своим
    /// согласием, и вернуть их — отдельное решение, которое он примет их же строкой.
    /// </remarks>
    public void Undo(PluginCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        card.Revert();
        Notify(nameof(HasChanges));
    }

    /// <summary>Баннер итога действия закрыли — сказанное больше не нужно.</summary>
    public void Dismiss() => Status = null;

    /// <summary>
    /// Ставит или снимает галочку — со спросом, если страдают соседи.
    /// </summary>
    /// <param name="card">Чью галочку трогают.</param>
    /// <remarks>
    /// Выключение того, кем пользуются другие, спрашивает: зависимые без него
    /// не поднимутся, и человек должен решить это глазами, а не узнать при
    /// следующем запуске из журнала. Согласился — гаснут и они, тут же и в том
    /// же окне: до «Сохранить» это всё ещё передумываемо.
    /// </remarks>
    public async Task ToggleAsync(PluginCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        // Модуль и сломанный не выключаются: у них и флажка нет, но Пробел в строке и чужой вызов
        // доходят сюда и без него.
        if (!card.CanToggle)
            return;

        if (!card.IsOn)
        {
            card.IsOn = true;
            Notify(nameof(HasChanges));
            return;
        }

        var dependents = Dependents(card.Plugin, onlyMandatory: true);

        if (dependents.Count > 0)
        {
            var agreed = await _dialogs.ConfirmAsync(
                Localizer.Instance["plugins.dependents.title"],
                string.Format(
                    CultureInfo.CurrentCulture,
                    Localizer.Instance["plugins.dependents.disable.message"],
                    card.Plugin.DisplayName,
                    string.Join(", ", dependents.Select(dependent => dependent.Plugin.DisplayName))),
                Localizer.Instance["plugins.dependents.disable.confirm"],
                danger: false);

            if (!agreed)
                return;

            foreach (var dependent in dependents)
                dependent.IsOn = false;
        }

        card.IsOn = false;
        Notify(nameof(HasChanges));
    }

    /// <summary>
    /// Снимает плагин с машины — сразу, вместе с его контекстом в студии.
    /// </summary>
    /// <param name="card">Кого снимают.</param>
    /// <remarks>
    /// Спрашивает всегда: удаление сносит папку и «Отменой» не отменяется — это
    /// единственное необратимое действие всего окна. Прежде вопрос задавался
    /// только за соседей, но тогда «Удалить» стояло отдельной кнопкой у самого
    /// края карточки; теперь оно спрятано под стрелку, и вопрос там уместен —
    /// два шага у необратимого, ноль лишних у частого.
    /// <para>
    /// Зависимые выключаются, а не удаляются: их папки — чужая работа, и
    /// сносить её за компанию студия не вправе. Выключаются они на диске
    /// сразу, а не галочкой: удаление уже случилось, и оставлять файлы в
    /// состоянии «зависимость снята, а зависимый включён» нельзя.
    /// </para>
    /// </remarks>
    public async Task RemoveAsync(PluginCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        // Модуль уходит только вместе со студией: Delete в его строке не делает ничего.
        if (!card.CanRemove)
            return;

        var dependents = Dependents(card.Plugin, onlyMandatory: true);

        var agreed = dependents.Count == 0
            ? await _dialogs.ConfirmAsync(
                Localizer.Instance["plugins.remove.title"],
                string.Format(
                    CultureInfo.CurrentCulture,
                    Localizer.Instance["plugins.remove.message"],
                    card.Plugin.DisplayName),
                Localizer.Instance["plugins.remove"],
                danger: true)
            : await _dialogs.ConfirmAsync(
                Localizer.Instance["plugins.dependents.title"],
                string.Format(
                    CultureInfo.CurrentCulture,
                    Localizer.Instance["plugins.dependents.remove.message"],
                    card.Plugin.DisplayName,
                    string.Join(", ", dependents.Select(dependent => dependent.Plugin.DisplayName))),
                Localizer.Instance["plugins.dependents.remove.confirm"],
                danger: true);

        if (!agreed)
            return;

        foreach (var dependent in dependents)
        {
            if (_catalog.SetEnabled(dependent.Plugin.Id, false) is not { } refusal)
                continue;

            // Зависимый не выключился — снимать того, на ком он стоит, нельзя: при следующем
            // запуске он поднялся бы без соседа, о чём человек как раз и не соглашался.
            Status = $"{Localizer.Instance["common.error"]}: {refusal}";
            return;
        }

        var error = _catalog.Uninstall(card.Plugin);

        if (error is not null)
        {
            Status = $"{Localizer.Instance["common.error"]}: {error}";
            return;
        }

        var gone = dependents.Select(dependent => dependent.Plugin.Id).Append(card.Plugin.Id).ToList();

        await _extensions.ApplyAsync(gone, []);

        var removed = dependents.Count > 0
            ? $"{card.Plugin.DisplayName} {Localizer.Instance["plugins.removed.suffix"]}. " +
              string.Format(
                  CultureInfo.CurrentCulture,
                  Localizer.Instance["plugins.disabled.many"],
                  string.Join(", ", dependents.Select(dependent => dependent.Plugin.DisplayName)))
            : $"{card.Plugin.DisplayName} {Localizer.Instance["plugins.removed.suffix"]}";

        // Удалённого в списке больше нет, и что удаление довершит перезапуск, сказать можно только
        // здесь.
        Status = Restarting(removed, card.Plugin.Id);

        Refresh();
    }

    /// <summary>
    /// Итог действия — и что довершит его перезапуск, если плагин его ждёт.
    /// </summary>
    /// <param name="done">Итог словами.</param>
    /// <param name="pluginId">О ком он.</param>
    /// <remarks>Без причины: её читают в журнале те, кому она что-то говорит.</remarks>
    private string Restarting(string done, string pluginId) =>
        _extensions.AwaitingRestart.ContainsKey(pluginId)
            ? $"{done}. {Localizer.Instance["restart.required"]}"
            : done;

    /// <summary>Перечитывает каталог: список собирается заново.</summary>
    /// <remarks>
    /// Языковые пакеты перечитываются вместе со списком: пакет — тоже плагин, и
    /// его установка, выключение или удаление меняют список языков студии.
    /// </remarks>
    public void Refresh()
    {
        var installed = _catalog.Scan();
        var all = installed.Concat(_builtIn).ToList();
        var wanted = _selected?.Plugin.Id;

        Cards.Clear();

        // Причина отказа — только у включённого: выключенный не поднимался и не должен был, а
        // запись о его прежнем отказе живёт в службе до следующей попытки.
        foreach (var plugin in installed)
        {
            var riseError = plugin.IsEnabled && _extensions.Unrisen.TryGetValue(plugin.Id, out var why) ? why : null;
            var card = new PluginCard(
                plugin,
                PluginGraph.Describe(plugin, all),
                riseError,
                _extensions.AwaitingRestart.ContainsKey(plugin.Id));

            card.PropertyChanged += OnCardChanged;
            Cards.Add(card);
        }

        BuiltIn = [.. _builtIn.Select(module => new PluginCard(module, PluginGraph.Describe(module, all)))];

        foreach (var card in Cards.Concat(BuiltIn))
        {
            // Список собирается заново после установки и удаления, а поиск в окне остаётся тем
            // же: новая строка встаёт под тот отбор, что был у прежних.
            card.Narrow(_query);
            card.Dependents = NeededBy(card.Plugin, all);
        }

        Group();

        // Выбор переживает пересборку: плагин, который смотрели, остаётся выбранным и после
        // установки соседа, и после «Сохранить».
        Choose(Cards.Concat(BuiltIn).FirstOrDefault(card => string.Equals(card.Plugin.Id, wanted, StringComparison.Ordinal)));
        Arrange();

        Notify(nameof(Cards));
        Notify(nameof(BuiltIn));
        Notify(nameof(IsEmpty));
        Notify(nameof(HasChanges));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Строки страницы — плагины, и отбирает их то же, по чему поиск находит страницу: имя,
    /// идентификатор, издатель и метки. Пока идёт отбор, группы раскрыты и не сворачиваются.
    /// </remarks>
    public void Narrow(string? query)
    {
        _query = string.IsNullOrWhiteSpace(query) ? null : query.Trim();

        foreach (var card in Cards.Concat(BuiltIn))
            card.Narrow(_query);

        foreach (var group in Groups)
            group.CanFold = _query is null;

        Arrange();
    }

    /// <summary>Кто из установленных сам объявил зависимость на этот плагин.</summary>
    /// <remarks>
    /// Прямые, а не вся волна: «нужен плагинам» отвечает, кто назвал его в своём манифесте. Кто
    /// стоит за ними, видно в их собственных подробностях. Необязательная связь так и подписана.
    /// </remarks>
    private static IReadOnlyList<string> NeededBy(InstalledPlugin target, IReadOnlyList<InstalledPlugin> all) =>
    [
        .. all
            .Select(plugin => (plugin, dependency: (plugin.Manifest?.Dependencies ?? [])
                .FirstOrDefault(dependency => string.Equals(dependency.Id, target.Id, StringComparison.OrdinalIgnoreCase))))
            .Where(pair => pair.dependency is not null)
            .Select(pair => pair.dependency!.Optional
                ? $"{pair.plugin.DisplayName} — {Localizer.Instance["plugins.dep.optional"]}"
                : pair.plugin.DisplayName),
    ];

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

        var external = Cards.Where(card => !card.IsLanguagePack).ToList();
        var languages = Cards.Where(card => card.IsLanguagePack).ToList();
        var only = external.Count == 0 && languages.Count == 0;

        Groups =
        [
            .. new[]
            {
                Make("external", "plugins.group.external", external, counts: true, open: true),
                Make("languages", "plugins.group.languages", languages, counts: true, open: true),
                Make("builtin", "plugins.group.builtin", BuiltIn, counts: false, open: only),
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

    /// <summary>
    /// Выбирает плагин по идентификатору; незнакомый ничего не меняет.
    /// </summary>
    /// <param name="pluginId">Кого выбрать.</param>
    /// <remarks>Так окно настроек возвращает выбор, с которым студию перезапустили.</remarks>
    public void Pick(string? pluginId)
    {
        if (Cards.Concat(BuiltIn).FirstOrDefault(card => string.Equals(card.Plugin.Id, pluginId, StringComparison.Ordinal)) is { } found)
            Choose(found);
    }

    /// <summary>Какие группы человек свернул или раскрыл сам: ключ группы и раскрыта ли она.</summary>
    public IReadOnlyDictionary<string, bool> Folded => new Dictionary<string, bool>(_folded, StringComparer.Ordinal);

    /// <summary>Возвращает группам то, как их свернул и раскрыл человек.</summary>
    /// <param name="folded">Ключ группы и раскрыта ли она.</param>
    public void Fold(IReadOnlyDictionary<string, bool> folded)
    {
        ArgumentNullException.ThrowIfNull(folded);

        foreach (var (key, open) in folded)
            _folded[key] = open;

        Group();
        Arrange();
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

    /// <summary>Галочка карточки сменилась — несохранённое страницы тоже.</summary>
    private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginCard.IsOn))
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Говорит, чем кончилась установка, и доводит её до студии.</summary>
    private async Task AcceptAsync((InstalledPlugin? Plugin, string? Error) result)
    {
        if (result.Plugin is not { } plugin)
        {
            Status = $"{Localizer.Instance["common.error"]}: {result.Error}";
            return;
        }

        var known = Cards.Any(card => string.Equals(card.Plugin.Id, plugin.Id, StringComparison.Ordinal));

        // Один вызов на обе дороги: прежней копии, если она была поднята,
        // нужен спуск, а свежей — подъём. Ставится плагин впервые — спускать
        // нечего, и список опускаемых остаётся пустым сам собой.
        var complaint = await _extensions.ApplyAsync([plugin.Id], [plugin.Id]);

        // Обновлённый встал свежей копией, а прежняя могла остаться в памяти: о перезапуске
        // говорится вместе с итогом, а не вместо него.
        Status = complaint ?? Restarting(
            $"{plugin.DisplayName} {plugin.Manifest?.Version} " +
            Localizer.Instance[known ? "plugins.updated.suffix" : "plugins.installed.suffix"],
            plugin.Id);

        Refresh();
    }

    /// <summary>Кто из включённых обязательно зависит от плагина.</summary>
    /// <remarks>
    /// Считается по галочкам, а не по диску: человек мог выключить соседа
    /// минуту назад в этом же окне, и спрашивать о нём было бы враньём.
    /// </remarks>
    private IReadOnlyList<PluginCard> Dependents(InstalledPlugin plugin, bool onlyMandatory)
    {
        var standing = Cards.Where(card => card.IsOn).Select(card => card.Plugin).ToList();

        return PluginGraph.Dependents(plugin.Id, standing, includeOptional: !onlyMandatory)
            .Select(dependent => Cards.First(card => card.Plugin.Id == dependent.Id))
            .ToList();
    }

    private void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
