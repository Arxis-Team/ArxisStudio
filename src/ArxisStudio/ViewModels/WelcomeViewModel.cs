using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.ViewModels;

/// <summary>Раздел экрана Welcome.</summary>
public enum WelcomeSection
{
    /// <summary>Недавние проекты и создание нового.</summary>
    Projects,

    /// <summary>Документация и материалы.</summary>
    Learn,
}

/// <summary>
/// Состояние экрана Welcome: выбранный раздел и две строки полосы, ведущие в
/// окно настроек.
/// </summary>
/// <remarks>
/// Разделов осталось два. Настройки и менеджер плагинов переехали страницами
/// окна настроек — одного на студию и на Welcome: экран закрывается навсегда,
/// стоит войти в студию, и всё, что жило только здесь, из работающей студии
/// становилось недостижимым.
/// </remarks>
public sealed class WelcomeViewModel : INotifyPropertyChanged
{
    private readonly IStudioLog? _log;

    private WelcomeSection _section = WelcomeSection.Projects;
    private bool _settingsOpen;
    private bool _pluginsOpen;
    private string _filter = string.Empty;
    private string? _status;

    /// <summary>Создаёт модель экрана.</summary>
    /// <param name="recent">Список недавних проектов.</param>
    /// <param name="plugins">Каталог плагинов.</param>
    /// <param name="log">Журнал студии; null — молча.</param>
    /// <remarks>
    /// Языки, принесённые плагинами, подхватываются сразу: выбирать язык
    /// человек пойдёт в настройки, а список собирается на ходу — из того, что
    /// стоит в папке плагинов.
    /// </remarks>
    public WelcomeViewModel(
        RecentProjects recent,
        PluginCatalog plugins,
        IStudioLog? log = null)
    {
        Recent = recent;
        Plugins = plugins;
        _log = log;

        ApplyLanguagePacks();
        RefreshRecent();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Недавние проекты — список, каким он лежит в файле.</summary>
    /// <remarks>
    /// Показывается не он, а <see cref="RecentProjects"/>: экран отбирает из него поиском. Сам
    /// список наблюдаемым не сделан намеренно — его второй писатель зовёт
    /// <see cref="Services.RecentProjects.Touch"/> из работающей студии, когда это окно закрыто, и
    /// читателя у уведомления там не будет никогда.
    /// </remarks>
    public RecentProjects Recent { get; }

    /// <summary>Недавние проекты, прошедшие поиск, — то, что видно на экране.</summary>
    public ObservableCollection<RecentProject> RecentProjects { get; } = [];

    /// <summary>Показывать нечего: либо список пуст, либо поиск ничего не нашёл.</summary>
    public bool HasNoRecent => RecentProjects.Count == 0;

    /// <summary>Что человек набрал в поиске.</summary>
    public string ProjectFilter
    {
        get => _filter;
        set
        {
            if (string.Equals(_filter, value, StringComparison.Ordinal))
                return;

            _filter = value;
            Notify();
            RefreshRecent();
        }
    }

    /// <summary>
    /// Что экран говорит о последней просьбе; null — молчит.
    /// </summary>
    /// <remarks>
    /// Отказ обязан прозвучать здесь, а не в журнале: окно закрывается, как только проект открыт, и
    /// сказать после этого будет не с чего — человек окажется в пустой студии без единого слова.
    /// </remarks>
    public string? Status
    {
        get => _status;
        set
        {
            if (string.Equals(_status, value, StringComparison.Ordinal))
                return;

            _status = value;
            Notify();
            Notify(nameof(HasStatus));
        }
    }

    /// <summary>Экрану есть что сказать.</summary>
    public bool HasStatus => !string.IsNullOrEmpty(Status);

    /// <summary>Каталог плагинов — тот же, что у окна настроек.</summary>
    public PluginCatalog Plugins { get; }

    /// <summary>
    /// Настройки открыты — строка «Настройки» подсвечена, как выбранный раздел.
    /// </summary>
    /// <remarks>
    /// Разделом настройки быть перестали: у них своё окно, одно на студию и на
    /// Welcome. Но строка в полосе осталась там же, где стояла, и человеку
    /// проще, когда она отмечена, пока окно открыто.
    /// </remarks>
    public bool IsSettingsOpen
    {
        get => _settingsOpen;
        set
        {
            if (_settingsOpen == value)
                return;

            _settingsOpen = value;
            Notify();
        }
    }

    /// <summary>Менеджер плагинов открыт — та же отметка на своей строке.</summary>
    public bool IsPluginsOpen
    {
        get => _pluginsOpen;
        set
        {
            if (_pluginsOpen == value)
                return;

            _pluginsOpen = value;
            Notify();
        }
    }

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
        }
    }

    /// <summary>Открыт раздел проектов.</summary>
    public bool IsProjects => Section == WelcomeSection.Projects;

    /// <summary>Открыт раздел обучения.</summary>
    public bool IsLearn => Section == WelcomeSection.Learn;

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

    /// <summary>
    /// Пересобирает показанный список из файла, отбирая поиском.
    /// </summary>
    /// <remarks>
    /// Явно, а не уведомлением от списка: правка за сеанс здесь одна — удаление, — и затевает её
    /// сам экран, так что о ней он и так знает. Зовётся ещё и после отказа открыть: чип «папка не
    /// найдена» должен появиться на строке в тот же миг, когда по ней щёлкнули.
    /// </remarks>
    public void RefreshRecent()
    {
        RecentProjects.Clear();

        foreach (var project in Recent.Items.Where(Matches))
            RecentProjects.Add(project);

        Notify(nameof(HasNoRecent));

        bool Matches(RecentProject project) =>
            ProjectFilter.Length == 0
            || project.Name.Contains(ProjectFilter, StringComparison.CurrentCultureIgnoreCase)
            || project.Path.Contains(ProjectFilter, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Убирает проект из списка — и с экрана, и из файла.</summary>
    /// <param name="project">Строка, которую убирают.</param>
    public void Remove(RecentProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        Recent.Remove(project.Path);
        RefreshRecent();
    }

    /// <summary>
    /// Почему по этому пути открывать нечего; null — открывать можно.
    /// </summary>
    /// <param name="path">Путь, названный строкой недавнего или диалогом.</param>
    /// <remarks>
    /// Спрашивается до того, как окно закроется. Правило расширений берётся у
    /// <see cref="StudioArguments.IsProject"/> — тем же, каким студия судит аргумент запуска:
    /// вторая копия правила разошлась бы с первой на первом же неизвестном расширении. Наличие
    /// файла проверяется здесь, потому что <c>CanonicalPath</c> диска не касается вовсе и путь к
    /// пропавшему файлу для него совершенно законен.
    /// </remarks>
    public string? Complaint(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!StudioArguments.IsProject(path))
            return $"{Localizer.Instance["common.error"]}: {path} — {Localizer.Instance["projects.unsupported"]}";

        return File.Exists(path)
            ? null
            : $"{Localizer.Instance["common.error"]}: {path} — {Localizer.Instance["projects.missing"]}";
    }

    private void Notify([CallerMemberName] string? property = null) => PropertyChanged.Raise(this, property);
}
