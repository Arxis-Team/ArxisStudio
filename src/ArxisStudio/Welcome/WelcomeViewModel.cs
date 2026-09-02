using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;

namespace ArxisStudio.Welcome;

/// <summary>Раздел экрана Welcome.</summary>
public enum WelcomeSection
{
    /// <summary>Недавние проекты и создание нового.</summary>
    Projects,

    /// <summary>Документация и материалы.</summary>
    Learn,

    /// <summary>Менеджер плагинов.</summary>
    Plugins,

    /// <summary>Настройки студии.</summary>
    Settings,
}

/// <summary>
/// Состояние экрана Welcome: выбранный раздел и список плагинов. Данные
/// читаются с диска, поэтому обновляются явными вызовами — экран не обязан
/// знать, когда пользователь поставил новый плагин.
/// </summary>
public sealed class WelcomeViewModel : INotifyPropertyChanged
{
    private WelcomeSection _section = WelcomeSection.Projects;
    private readonly IStudioLog? _log;

    private string? _status;

    /// <summary>Создаёт модель экрана.</summary>
    /// <param name="settings">Хранилище настроек студии.</param>
    /// <param name="recent">Список недавних проектов.</param>
    /// <param name="plugins">Каталог плагинов.</param>
    /// <param name="log">Журнал студии; null — молча.</param>
    public WelcomeViewModel(
        ISettingsStore settings,
        RecentProjects recent,
        PluginCatalog plugins,
        IStudioLog? log = null)
    {
        SettingsStore = settings;
        Recent = recent;
        Plugins = plugins;
        _log = log;

        RefreshPlugins();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Настройки студии.</summary>
    public ISettingsStore SettingsStore { get; }

    /// <summary>
    /// Недавние проекты.
    /// </summary>
    /// <remarks>
    /// Экран их не показывает: работа с проектами приедет отдельным модулем, а
    /// до тех пор в «Недавних» стоит заглушка. Сам список и его файл остаются
    /// моделью, к которой модуль вернётся, — иначе формат пришлось бы
    /// придумывать заново.
    /// </remarks>
    public RecentProjects Recent { get; }

    /// <summary>Каталог плагинов.</summary>
    public PluginCatalog Plugins { get; }

    /// <summary>Строки интерфейса.</summary>
    public Localizer Loc => Localizer.Instance;

    /// <summary>Установленные плагины.</summary>
    public ObservableCollection<PluginCard> InstalledPlugins { get; } = [];

    /// <summary>
    /// Настройки, объявленные установленными плагинами.
    /// </summary>
    /// <remarks>
    /// Список строится по манифестам: студия читает их, не загружая сборок, и
    /// настройки видны даже у плагина, который в этом сеансе не поднимался.
    /// </remarks>
    public ObservableCollection<PluginSettingRow> PluginSettings { get; } = [];

    /// <summary>Ни один плагин настроек не объявил.</summary>
    public bool HasNoPluginSettings => PluginSettings.Count == 0;

    /// <summary>Текущий раздел.</summary>
    public WelcomeSection Section
    {
        get => _section;
        set
        {
            if (_section == value)
                return;

            _section = value;
            Notify();
            Notify(nameof(IsProjects));
            Notify(nameof(IsLearn));
            Notify(nameof(IsPlugins));
            Notify(nameof(IsSettings));
        }
    }

    /// <summary>Открыт раздел проектов.</summary>
    public bool IsProjects => Section == WelcomeSection.Projects;

    /// <summary>Открыт раздел обучения.</summary>
    public bool IsLearn => Section == WelcomeSection.Learn;

    /// <summary>Открыт раздел плагинов.</summary>
    public bool IsPlugins => Section == WelcomeSection.Plugins;

    /// <summary>Открыт раздел настроек.</summary>
    public bool IsSettings => Section == WelcomeSection.Settings;

    /// <summary>Плагинов не установлено.</summary>
    public bool HasNoPlugins => InstalledPlugins.Count == 0;

    /// <summary>Последнее сообщение операции: ошибка установки, результат создания.</summary>
    public string? Status
    {
        get => _status;
        set
        {
            if (_status == value)
                return;

            _status = value;
            Notify();
            Notify(nameof(HasStatus));
        }
    }

    /// <summary>Есть ли сообщение для показа.</summary>
    public bool HasStatus => !string.IsNullOrEmpty(Status);

    /// <summary>
    /// Пересобирает языки, принесённые плагинами.
    /// </summary>
    /// <remarks>
    /// Языковой пакет — плагин, и всё, что делает с плагинами менеджер, —
    /// установка, включение, выключение, удаление — меняет список языков
    /// студии. Держать его в стороне значило бы оставлять в настройках
    /// язык, которого уже нет.
    /// </remarks>
    public void ApplyLanguagePacks() => LanguagePacks.Apply(Plugins, _log);

    /// <summary>Перечитывает каталог плагинов.</summary>
    public void RefreshPlugins()
    {
        ApplyLanguagePacks();

        InstalledPlugins.Clear();
        PluginSettings.Clear();

        var store = new PluginSettingsStore();

        var installed = Plugins.Scan();

        // Цели зависимостей ищутся среди соседей и встроенных модулей: модуль
        // — годная цель, и карточка обязана считать его присутствующим.
        var all = installed.Concat(StudioModules.Describe()).ToList();

        foreach (var plugin in installed)
        {
            InstalledPlugins.Add(new PluginCard(plugin, PluginGraph.Describe(plugin, all)));

            foreach (var declared in plugin.Manifest?.Contributions.Settings ?? [])
                PluginSettings.Add(new PluginSettingRow(plugin.Id, plugin.DisplayName, declared, store, plugin.Strings));
        }

        foreach (var plugin in installed.Where(candidate => candidate.IconPath is not null))
        {
            if (PluginIcons.Instance.Of(plugin.IconPath) is null)
            {
                _log?.Write(
                    StudioLogLevel.Warning,
                    "Plugins",
                    $"{plugin.DisplayName}: значок {plugin.Manifest?.Icon} не прочитался — " +
                    "не картинка или больше мегабайта");
            }
        }

        Notify(nameof(HasNoPlugins));
        Notify(nameof(HasNoPluginSettings));
    }

    /// <summary>
    /// Кто из включённых обязательно зависит от плагина — прямо или через
    /// других.
    /// </summary>
    /// <param name="plugin">Кого собираются выключить или удалить.</param>
    /// <remarks>
    /// Только обязательные: выключение соседа необязательную связь не ломает,
    /// и пугать человека этими именами значило бы врать.
    /// </remarks>
    public IReadOnlyList<InstalledPlugin> MandatoryDependentsOf(InstalledPlugin plugin) =>
        PluginGraph.Dependents(
            plugin.Id,
            Plugins.Scan().Where(candidate => candidate.IsEnabled).ToList(),
            includeOptional: false);

    private void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
