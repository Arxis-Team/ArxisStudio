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
/// перезагружается. Не отпустился — окно говорит про перезапуск, а не молчит.
/// </para>
/// <para>
/// Встроенных модулей здесь нет и быть не должно: их не ставят и не удаляют,
/// они приезжают со студией. Настройки же объявляют и те и другие — они на
/// соседней ветке «Расширения».
/// </para>
/// </remarks>
public sealed class PluginsPage : ISettingsPage, INotifyPropertyChanged
{
    private readonly PluginCatalog _catalog;
    private readonly StudioPlugins _extensions;
    private readonly IPluginDialogs _dialogs;

    private string? _status;

    /// <summary>Собирает страницу поверх каталога и живых расширений.</summary>
    /// <param name="catalog">Каталог плагинов на диске.</param>
    /// <param name="extensions">Расширения студии: живой хост.</param>
    /// <param name="dialogs">Кто спрашивает человека.</param>
    public PluginsPage(PluginCatalog catalog, StudioPlugins extensions, IPluginDialogs dialogs)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(dialogs);

        _catalog = catalog;
        _extensions = extensions;
        _dialogs = dialogs;

        Refresh();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Установленные плагины.</summary>
    public ObservableCollection<PluginCard> Cards { get; } = [];

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
    public IEnumerable<string> Terms =>
        Cards
            .SelectMany(card => card.Plugin.Tags
                .Append(card.Plugin.DisplayName)
                .Append(card.Plugin.Id)
                .Append(card.Plugin.Manifest?.Publisher))
            .Append(Title)
            .OfType<string>();

    /// <inheritdoc/>
    public bool HasChanges => Cards.Any(card => card.IsChanged);

    /// <summary>Плагинов не установлено.</summary>
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
    public async Task CommitAsync(ICollection<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        var changed = Cards.Where(card => card.IsChanged).ToList();

        if (changed.Count == 0)
            return;

        foreach (var card in changed)
            _catalog.SetEnabled(card.Plugin.Id, card.IsOn);

        var complaint = await _extensions.ApplyAsync(
            changed.Where(card => !card.IsOn).Select(card => card.Plugin.Id).ToList(),
            changed.Where(card => card.IsOn).Select(card => card.Plugin.Id).ToList());

        if (complaint is not null)
            problems.Add(complaint);

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
            _catalog.SetEnabled(dependent.Plugin.Id, false);

        var error = _catalog.Uninstall(card.Plugin);

        if (error is not null)
        {
            Status = $"{Localizer.Instance["common.error"]}: {error}";
            return;
        }

        var gone = dependents.Select(dependent => dependent.Plugin.Id).Append(card.Plugin.Id).ToList();

        await _extensions.ApplyAsync(gone, []);

        Status = dependents.Count > 0
            ? $"{card.Plugin.DisplayName} {Localizer.Instance["plugins.removed.suffix"]}. " +
              string.Format(
                  CultureInfo.CurrentCulture,
                  Localizer.Instance["plugins.disabled.many"],
                  string.Join(", ", dependents.Select(dependent => dependent.Plugin.DisplayName)))
            : $"{card.Plugin.DisplayName} {Localizer.Instance["plugins.removed.suffix"]}";

        Refresh();
    }

    /// <summary>Перечитывает каталог: список собирается заново.</summary>
    /// <remarks>
    /// Языковые пакеты перечитываются вместе со списком: пакет — тоже плагин, и
    /// его установка, выключение или удаление меняют список языков студии.
    /// </remarks>
    public void Refresh()
    {
        var installed = _catalog.Scan();
        var all = installed.Concat(_extensions.Modules).ToList();

        Cards.Clear();

        foreach (var plugin in installed)
            Cards.Add(new PluginCard(plugin, PluginGraph.Describe(plugin, all)));

        Notify(nameof(Cards));
        Notify(nameof(IsEmpty));
        Notify(nameof(HasChanges));
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

        Status = complaint is not null
            ? complaint
            : $"{plugin.DisplayName} {plugin.Manifest?.Version} " +
              Localizer.Instance[known ? "plugins.updated.suffix" : "plugins.installed.suffix"];

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
