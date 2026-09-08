using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
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
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

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

    /// <summary>Каталог плагинов — тот же, что у окна настроек.</summary>
    public PluginCatalog Plugins { get; }

    /// <summary>Строки интерфейса.</summary>
    public Localizer Loc => Localizer.Instance;

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

    private void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
