using System.Diagnostics;
using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Settings;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using ArxisStudio.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace ArxisStudio.Welcome;

/// <summary>
/// Экран Welcome: вход в студию, обучение, плагины и настройки.
/// </summary>
/// <remarks>
/// Проектов экран не открывает: этой работы у студии пока нет вовсе, она
/// приедет отдельным модулем. Вход в каркас закрывает это окно и сообщает о
/// себе через <see cref="StudioRequested"/> — что делать дальше, окно не знает.
/// </remarks>
public partial class WelcomeWindow : AxWindow
{
    private readonly WelcomeViewModel _model;
    private readonly ISettingsStore _settings;
    private readonly StudioPlugins _extensions;

    /// <summary>Создаёт экран со своими сервисами.</summary>
    /// <param name="settings">Настройки студии.</param>
    /// <param name="recent">Список недавних проектов.</param>
    /// <param name="plugins">Каталог плагинов.</param>
    /// <param name="extensions">Расширения студии: у них общее хранилище настроек.</param>
    /// <param name="log">Журнал студии; null — молча.</param>
    public WelcomeWindow(
        ISettingsStore settings,
        RecentProjects recent,
        PluginCatalog plugins,
        StudioPlugins extensions,
        IStudioLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        _settings = settings;
        _extensions = extensions;
        _model = new WelcomeViewModel(recent, plugins, log);
        DataContext = _model;

        InitializeComponent();
    }

    /// <summary>Пользователь просит открыть студию.</summary>
    public event EventHandler? StudioRequested;

    private void OnProjectsClick(object? sender, RoutedEventArgs e) => Select(WelcomeSection.Projects);

    private void OnLearnClick(object? sender, RoutedEventArgs e) => Select(WelcomeSection.Learn);

    /// <summary>
    /// Открывает настройки — то же окно, что и из студии.
    /// </summary>
    /// <remarks>
    /// Языковые пакеты перечитываются перед показом: пакет могли поставить
    /// менеджером минуту назад, и список языков собирается на ходу, а не
    /// знается наперёд.
    /// </remarks>
    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        _model.ApplyLanguagePacks();
        _model.IsSettingsOpen = true;

        try
        {
            await SettingsWindow.ShowAsync(this, _settings, _extensions, _extensions.Declaring(), _model.Plugins);
        }
        finally
        {
            _model.IsSettingsOpen = false;
        }
    }

    /// <summary>
    /// Открывает менеджер плагинов — ту же страницу того же окна настроек.
    /// </summary>
    /// <remarks>
    /// Разделом плагины быть перестали: менеджер живёт страницей окна, одного
    /// на студию и на Welcome, и держать вторую его вёрстку здесь значило бы
    /// чинить каждую находку дважды.
    /// </remarks>
    private async void OnPluginsClick(object? sender, RoutedEventArgs e)
    {
        _model.ApplyLanguagePacks();
        _model.IsPluginsOpen = true;

        try
        {
            await SettingsWindow.ShowAsync(
                this, _settings, _extensions, _extensions.Declaring(), _model.Plugins, "studio.plugins");
        }
        finally
        {
            _model.IsPluginsOpen = false;
        }
    }

    private void Select(WelcomeSection section) => _model.Section = section;

    private void OnStudioPressed(object? sender, PointerPressedEventArgs e) =>
        StudioRequested?.Invoke(this, EventArgs.Empty);

    private void OnLinkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string url })
            StudioOpen.InShell(url);
    }
}
