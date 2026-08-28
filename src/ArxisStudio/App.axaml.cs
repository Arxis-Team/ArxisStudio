using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using ArxisStudio.Welcome;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ArxisStudio;

/// <summary>
/// Точка сборки студии: создаёт сервисы, применяет сохранённые настройки и
/// показывает экран Welcome. Главное окно открывается, когда выбран проект.
/// </summary>
public class App : Application
{
    private ISettingsStore _settings = null!;
    private RecentProjects _recent = null!;
    private PluginCatalog _plugins = null!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        StudioDevTools.Attach(this);
    }

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        StudioPaths.EnsureUserData();

        _settings = new JsonSettingsStore();
        _recent = new RecentProjects();
        _plugins = new PluginCatalog();

        ApplySettings();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose;
            desktop.MainWindow = CreateWelcome();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private WelcomeWindow CreateWelcome()
    {
        var welcome = new WelcomeWindow(_settings, _recent, _plugins, _log);
        welcome.ProjectRequested += (_, path) =>
        {
            var studio = new MainWindow(_settings, path);
            studio.Show();
            welcome.Close();
        };

        return welcome;
    }

    // Журнал нужен ещё до окна студии: пакеты языков разбираются при
    // запуске, и сказать о занятом коде или потерянном словаре больше
    // некуда.
    private readonly StudioLog _log = new(Console.Out);

    private void ApplySettings()
    {
        var settings = _settings.Current;

        // Языки плагинов ставятся раньше выбора: выбранный язык вполне
        // может быть тем, который принёс пакет, — не поставив их, студия
        // отказала бы ему как несуществующему.
        LanguagePacks.Apply(_plugins, _log);

        Localizer.Instance.SetLanguage(settings.Language);
        StudioTheming.Apply(settings.Theme);
    }
}
