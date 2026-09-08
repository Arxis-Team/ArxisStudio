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
        _model = new WelcomeViewModel(settings, recent, plugins, log);
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
            await SettingsWindow.ShowAsync(this, _settings, _extensions, _extensions.Declaring());
        }
        finally
        {
            _model.IsSettingsOpen = false;
        }
    }

    private void OnPluginsClick(object? sender, RoutedEventArgs e)
    {
        _model.RefreshPlugins();
        Select(WelcomeSection.Plugins);
    }

    private void Select(WelcomeSection section) => _model.Section = section;

    private void OnDismissStatus(object? sender, RoutedEventArgs e) => _model.Status = null;

    private void OnStudioPressed(object? sender, PointerPressedEventArgs e) =>
        StudioRequested?.Invoke(this, EventArgs.Empty);

    private async void OnInstallPluginClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localizer.Instance["plugins.install"],
            AllowMultiple = false,
        });

        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } source)
            return;

        Report(_model.Plugins.InstallFromDirectory(source, replace: true));
    }

    /// <summary>
    /// Ставит плагин из архива <c>.axplugin</c>.
    /// </summary>
    /// <remarks>
    /// Архив — то, чем плагин доезжает до чужой машины: та же папка в zip.
    /// Каталог умел ставить его с самого начала, а положить архив было некуда —
    /// в менеджере была одна кнопка, и та про папку.
    /// </remarks>
    private async void OnInstallArchiveClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Instance["plugins.installarchive"],
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ArxisStudio") { Patterns = ["*.axplugin"] },
            ],
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } archive)
            return;

        Report(_model.Plugins.InstallFromArchive(archive, replace: true));
    }

    /// <summary>
    /// Снимает плагин с машины.
    /// </summary>
    /// <remarks>
    /// Для одинокого плагина подтверждения нет намеренно: плагин — это папка,
    /// поставить его заново значит выбрать её снова, и спрашивать «точно ли» о
    /// действии, которое повторяется одним щелчком, — лишний шаг на каждый раз
    /// ради редкой ошибки. Вопрос появляется только тогда, когда страдают
    /// другие: этим плагином пользуются соседи, и молча оставить их сломанными
    /// нельзя.
    /// </remarks>
    private async void OnRemovePluginClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: InstalledPlugin plugin })
            return;

        var dependents = _model.MandatoryDependentsOf(plugin);

        if (dependents.Count > 0)
        {
            var agreed = await StudioAsk.ConfirmAsync(
                this,
                Localizer.Instance["plugins.dependents.title"],
                string.Format(
                    CultureInfo.CurrentCulture,
                    Localizer.Instance["plugins.dependents.remove.message"],
                    plugin.DisplayName,
                    string.Join(", ", dependents.Select(dependent => dependent.DisplayName))),
                Localizer.Instance["plugins.dependents.remove.confirm"],
                danger: true);

            if (!agreed)
                return;

            // Зависимые выключаются, а не удаляются: их папки — чужая
            // работа, и сносить её за компанию студия не вправе. Выключенный
            // плагин человек включит обратно, когда вернёт зависимость.
            foreach (var dependent in dependents)
                _model.Plugins.SetEnabled(dependent.Id, false);
        }

        var error = _model.Plugins.Uninstall(plugin);

        _model.Status = error is null
            ? dependents.Count > 0
                ? $"{plugin.DisplayName} {Localizer.Instance["plugins.removed.suffix"]}. " +
                  string.Format(
                      CultureInfo.CurrentCulture,
                      Localizer.Instance["plugins.disabled.many"],
                      string.Join(", ", dependents.Select(dependent => dependent.DisplayName)))
                : $"{plugin.DisplayName} {Localizer.Instance["plugins.removed.suffix"]}"
            : $"{Localizer.Instance["common.error"]}: {error}";

        _model.RefreshPlugins();
    }

    /// <summary>
    /// Говорит, чем кончилась установка.
    /// </summary>
    /// <remarks>
    /// Установка поверх уже стоящего плагина — обычный способ обновиться, и
    /// сказать об этом надо иначе, чем о первой установке: иначе человек не
    /// поймёт, заменил он свою версию или поставил вторую.
    /// </remarks>
    private void Report((InstalledPlugin? Plugin, string? Error) result)
    {
        var known = _model.InstalledPlugins.Select(card => card.Plugin.Id).ToHashSet(StringComparer.Ordinal);

        _model.Status = result.Plugin is not { } plugin
            ? $"{Localizer.Instance["common.error"]}: {result.Error}"
            : $"{plugin.DisplayName} {plugin.Manifest?.Version} " +
              Localizer.Instance[known.Contains(plugin.Id) ? "plugins.updated.suffix" : "plugins.installed.suffix"];

        _model.RefreshPlugins();
    }

    private void OnOpenPluginFolderClick(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_model.Plugins.Root);
        OpenInShell(_model.Plugins.Root);
    }

    /// <summary>
    /// Включает или выключает плагин.
    /// </summary>
    /// <remarks>
    /// Выключение того, кем пользуются другие, спрашивает: зависимые без
    /// него не поднимутся, и человек должен решить это глазами, а не узнать
    /// при следующем запуске из журнала.
    /// </remarks>
    private async void OnTogglePluginClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: InstalledPlugin plugin })
            return;

        if (plugin.IsEnabled)
        {
            var dependents = _model.MandatoryDependentsOf(plugin);

            if (dependents.Count > 0)
            {
                var agreed = await StudioAsk.ConfirmAsync(
                    this,
                    Localizer.Instance["plugins.dependents.title"],
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Localizer.Instance["plugins.dependents.disable.message"],
                        plugin.DisplayName,
                        string.Join(", ", dependents.Select(dependent => dependent.DisplayName))),
                    Localizer.Instance["plugins.dependents.disable.confirm"],
                    danger: false);

                if (!agreed)
                    return;

                foreach (var dependent in dependents)
                    _model.Plugins.SetEnabled(dependent.Id, false);

                _model.Status = string.Format(
                    CultureInfo.CurrentCulture,
                    Localizer.Instance["plugins.disabled.many"],
                    string.Join(", ", dependents.Select(dependent => dependent.DisplayName).Append(plugin.DisplayName)));
            }
        }

        _model.Plugins.SetEnabled(plugin.Id, !plugin.IsEnabled);
        _model.RefreshPlugins();
    }

    private void OnLinkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string url })
            OpenInShell(url);
    }

    private static void OpenInShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Открыть ссылку или папку — не то, ради чего стоит падать.
        }
    }
}
