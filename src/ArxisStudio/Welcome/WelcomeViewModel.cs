using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;

namespace ArxisStudio.Welcome;

/// <summary>Раздел экрана Welcome.</summary>
public enum WelcomeSection
{
    /// <summary>Недавние проекты и создание нового.</summary>
    Projects,

    /// <summary>Установленные шаблоны dotnet new.</summary>
    Templates,

    /// <summary>Документация и материалы.</summary>
    Learn,

    /// <summary>Менеджер плагинов.</summary>
    Plugins,

    /// <summary>Настройки студии.</summary>
    Settings,
}

/// <summary>
/// Состояние экрана Welcome: выбранный раздел, списки проектов, шаблонов и
/// плагинов. Данные читаются с диска, поэтому обновляются явными вызовами —
/// экран не обязан знать, когда пользователь поставил новый шаблон.
/// </summary>
public sealed class WelcomeViewModel : INotifyPropertyChanged
{
    private readonly TemplateCatalog _templates = new();
    private WelcomeSection _section = WelcomeSection.Projects;
    private bool _isLoadingTemplates;
    private string _projectFilter = string.Empty;
    private string _templateFilter = string.Empty;
    private string? _status;

    /// <summary>Создаёт модель экрана.</summary>
    /// <param name="settings">Хранилище настроек студии.</param>
    /// <param name="recent">Список недавних проектов.</param>
    /// <param name="plugins">Каталог плагинов.</param>
    public WelcomeViewModel(ISettingsStore settings, RecentProjects recent, PluginCatalog plugins)
    {
        SettingsStore = settings;
        Recent = recent;
        Plugins = plugins;

        RefreshRecent();
        RefreshPlugins();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Настройки студии.</summary>
    public ISettingsStore SettingsStore { get; }

    /// <summary>Недавние проекты.</summary>
    public RecentProjects Recent { get; }

    /// <summary>Каталог плагинов.</summary>
    public PluginCatalog Plugins { get; }

    /// <summary>Строки интерфейса.</summary>
    public Localizer Loc => Localizer.Instance;

    /// <summary>Недавние проекты, отфильтрованные строкой поиска.</summary>
    public ObservableCollection<RecentProject> RecentProjects { get; } = [];

    /// <summary>Установленные шаблоны, отфильтрованные строкой поиска.</summary>
    public ObservableCollection<ProjectTemplate> Templates { get; } = [];

    /// <summary>Установленные плагины.</summary>
    public ObservableCollection<InstalledPlugin> InstalledPlugins { get; } = [];

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
            Notify(nameof(IsTemplates));
            Notify(nameof(IsLearn));
            Notify(nameof(IsPlugins));
            Notify(nameof(IsSettings));
        }
    }

    /// <summary>Открыт раздел проектов.</summary>
    public bool IsProjects => Section == WelcomeSection.Projects;

    /// <summary>Открыт раздел шаблонов.</summary>
    public bool IsTemplates => Section == WelcomeSection.Templates;

    /// <summary>Открыт раздел обучения.</summary>
    public bool IsLearn => Section == WelcomeSection.Learn;

    /// <summary>Открыт раздел плагинов.</summary>
    public bool IsPlugins => Section == WelcomeSection.Plugins;

    /// <summary>Открыт раздел настроек.</summary>
    public bool IsSettings => Section == WelcomeSection.Settings;

    /// <summary>Идёт чтение списка шаблонов.</summary>
    public bool IsLoadingTemplates
    {
        get => _isLoadingTemplates;
        private set
        {
            if (_isLoadingTemplates == value)
                return;

            _isLoadingTemplates = value;
            Notify();
            Notify(nameof(HasNoTemplates));
        }
    }

    /// <summary>Шаблонов нет, и чтение уже закончилось.</summary>
    public bool HasNoTemplates => !IsLoadingTemplates && Templates.Count == 0;

    /// <summary>Список недавних пуст.</summary>
    public bool HasNoRecent => RecentProjects.Count == 0;

    /// <summary>Плагинов не установлено.</summary>
    public bool HasNoPlugins => InstalledPlugins.Count == 0;

    /// <summary>Строка поиска по проектам.</summary>
    public string ProjectFilter
    {
        get => _projectFilter;
        set
        {
            if (_projectFilter == value)
                return;

            _projectFilter = value;
            Notify();
            RefreshRecent();
        }
    }

    /// <summary>Строка поиска по шаблонам.</summary>
    public string TemplateFilter
    {
        get => _templateFilter;
        set
        {
            if (_templateFilter == value)
                return;

            _templateFilter = value;
            Notify();
            ApplyTemplateFilter();
        }
    }

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

    /// <summary>Перечитывает список недавних проектов с учётом фильтра.</summary>
    public void RefreshRecent()
    {
        var filtered = Recent.Items.Where(Matches).ToList();

        RecentProjects.Clear();
        foreach (var project in filtered)
            RecentProjects.Add(project);

        Notify(nameof(HasNoRecent));

        bool Matches(RecentProject project) =>
            ProjectFilter.Length == 0 ||
            project.Name.Contains(ProjectFilter, StringComparison.CurrentCultureIgnoreCase) ||
            project.Path.Contains(ProjectFilter, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Перечитывает каталог плагинов.</summary>
    public void RefreshPlugins()
    {
        InstalledPlugins.Clear();
        foreach (var plugin in Plugins.Scan())
            InstalledPlugins.Add(plugin);

        Notify(nameof(HasNoPlugins));
    }

    /// <summary>Читает установленные шаблоны dotnet new.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task LoadTemplatesAsync(CancellationToken cancellationToken = default)
    {
        IsLoadingTemplates = true;
        try
        {
            AllTemplates = await _templates.ListAsync(cancellationToken);
            ApplyTemplateFilter();
        }
        finally
        {
            IsLoadingTemplates = false;
        }
    }

    /// <summary>Создаёт проект по шаблону.</summary>
    /// <param name="template">Шаблон.</param>
    /// <param name="name">Имя проекта.</param>
    /// <param name="location">Папка, внутри которой создать проект.</param>
    public Task<(string? EntryPoint, string? Error)> CreateProjectAsync(
        ProjectTemplate template, string name, string location) =>
        _templates.CreateAsync(template, name, location);

    internal IReadOnlyList<ProjectTemplate> AllTemplates { get; private set; } = [];

    private void ApplyTemplateFilter()
    {
        Templates.Clear();

        foreach (var template in AllTemplates.Where(Matches))
            Templates.Add(template);

        Notify(nameof(HasNoTemplates));

        bool Matches(ProjectTemplate template) =>
            TemplateFilter.Length == 0 ||
            template.Name.Contains(TemplateFilter, StringComparison.CurrentCultureIgnoreCase) ||
            template.ShortName.Contains(TemplateFilter, StringComparison.CurrentCultureIgnoreCase) ||
            template.TagLine.Contains(TemplateFilter, StringComparison.CurrentCultureIgnoreCase);
    }

    private void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
