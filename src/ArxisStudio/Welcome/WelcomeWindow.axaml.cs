using System.Diagnostics;
using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace ArxisStudio.Welcome;

/// <summary>
/// Экран Welcome: недавние проекты, шаблоны, обучение, плагины и настройки.
/// Открытие проекта закрывает это окно и передаёт путь дальше через
/// <see cref="ProjectRequested"/> — окно не знает, что со ним будет делать студия.
/// </summary>
public partial class WelcomeWindow : AxWindow
{
    private readonly WelcomeViewModel _model;
    private bool _loadingSettings;

    /// <summary>Создаёт экран со своими сервисами.</summary>
    /// <param name="settings">Настройки студии.</param>
    /// <param name="recent">Список недавних проектов.</param>
    /// <param name="plugins">Каталог плагинов.</param>
    /// <param name="log">Журнал студии; null — молча.</param>
    public WelcomeWindow(
        ISettingsStore settings,
        RecentProjects recent,
        PluginCatalog plugins,
        IStudioLog? log = null)
    {
        _model = new WelcomeViewModel(settings, recent, plugins, log);
        DataContext = _model;

        InitializeComponent();
        LoadSettingsIntoControls();

    }

    /// <summary>Пользователь выбрал проект: путь к <c>.sln</c>, <c>.slnx</c> или <c>.csproj</c>.</summary>
    public event EventHandler<string>? ProjectRequested;

    private void OnProjectsClick(object? sender, RoutedEventArgs e) => Select(WelcomeSection.Projects);

    private void OnLearnClick(object? sender, RoutedEventArgs e) => Select(WelcomeSection.Learn);

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        // Словари лежат файлами, и файл могли положить только что; языковой
        // пакет могли поставить менеджером минуту назад. Список языков
        // собирается при каждом заходе в настройки, а не один раз при
        // запуске — иначе добавленный язык ждал бы перезапуска студии.
        _model.ApplyLanguagePacks();
        ShowLanguages();

        Select(WelcomeSection.Settings);
    }

    /// <summary>
    /// Заполняет список языков тем, что студия сейчас умеет показать.
    /// </summary>
    /// <remarks>
    /// Выбранным отмечается язык, на котором интерфейс говорит на самом деле,
    /// а не записанный в настройках: словарь могли удалить, и тогда студия
    /// осталась на запасном — показать в списке отсутствующий язык значило бы
    /// соврать.
    /// </remarks>
    private void ShowLanguages()
    {
        // Признак сохраняется и возвращается, а не гасится: список языков
        // заполняют и при заходе в настройки, и посреди общей загрузки
        // контролов — сбросив его здесь, мы приняли бы остаток той загрузки
        // за правку человека.
        var loading = _loadingSettings;

        _loadingSettings = true;
        try
        {
            LanguageBox.Items.Clear();

            foreach (var language in Localizer.Instance.Languages)
            {
                LanguageBox.Items.Add(new AxComboBoxItem
                {
                    Content = language.Name,
                    Tag = language.Code,
                });
            }

            LanguageBox.SelectedItem = LanguageBox.Items
                .OfType<AxComboBoxItem>()
                .FirstOrDefault(item => (string?)item.Tag == Localizer.Instance.Language);
        }
        finally
        {
            _loadingSettings = loading;
        }
    }

    private void OnPluginsClick(object? sender, RoutedEventArgs e)
    {
        _model.RefreshPlugins();
        Select(WelcomeSection.Plugins);
    }

    private async void OnTemplatesClick(object? sender, RoutedEventArgs e)
    {
        Select(WelcomeSection.Templates);

        if (_model.AllTemplates.Count == 0)
            await _model.LoadTemplatesAsync();
    }

    private async void OnRefreshTemplatesClick(object? sender, RoutedEventArgs e) =>
        await _model.LoadTemplatesAsync();

    private void Select(WelcomeSection section) => _model.Section = section;

    private void OnDismissStatus(object? sender, RoutedEventArgs e) => _model.Status = null;

    private void OnRecentPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string path })
            OpenProject(path);
    }

    private async void OnOpenProjectClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Instance["projects.open"],
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Solution / project")
                {
                    Patterns = ["*.sln", "*.slnx", "*.csproj"],
                },
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            OpenProject(path);
    }

    private async void OnNewProjectClick(object? sender, RoutedEventArgs e)
    {
        _model.Section = WelcomeSection.Templates;

        if (_model.AllTemplates.Count == 0)
            await _model.LoadTemplatesAsync();
    }

    private void OnCloneClick(object? sender, RoutedEventArgs e) =>
        _model.Status = Localizer.Instance["vcs.clone.later"];

    private async void OnCreateFromTemplate(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: ProjectTemplate template })
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localizer.Instance["newproject.location"],
            AllowMultiple = false,
        });

        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } location)
            return;

        var name = MakeProjectName(template, location);
        _model.Status = Localizer.Instance["newproject.creating"];

        var (entryPoint, error) = await _model.CreateProjectAsync(template, name, location);

        if (entryPoint is null)
        {
            _model.Status = $"{Localizer.Instance["common.error"]}: {error}";
            return;
        }

        OpenProject(entryPoint);
    }

    private static string MakeProjectName(ProjectTemplate template, string location)
    {
        var baseName = template.ShortName.Replace('.', '-');
        var name = baseName;
        var index = 2;

        while (Directory.Exists(Path.Combine(location, name)))
            name = $"{baseName}-{index++}";

        return name;
    }

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
            var agreed = await ConfirmAsync(
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
    /// Спрашивает разрешения на действие, задевающее других.
    /// </summary>
    /// <param name="title">Заголовок вопроса.</param>
    /// <param name="message">Что случится и с кем.</param>
    /// <param name="confirm">Надпись на кнопке согласия.</param>
    /// <param name="danger">Действие необратимо — кнопка предупреждает цветом.</param>
    /// <remarks>
    /// Первый диалог студии. AxDialog задуман модальным окном: решение о
    /// чужих плагинах не то, мимо чего можно щёлкнуть, — Esc и «Отмена»
    /// оставляют всё как было.
    /// </remarks>
    private async Task<bool> ConfirmAsync(string title, string message, string confirm, bool danger)
    {
        var cancel = new AxButton { Content = Localizer.Instance["common.cancel"], MinWidth = 96 };
        var agree = new AxButton { Content = confirm, MinWidth = 96 };
        var alert = new AxIcon { Data = AxIcons.Warning, Width = 20, Height = 20 };

        // Кисть ищется с вариантом темы: без него ресурс не находится, а
        // выставленный null убил бы наследование цвета — значок стал бы
        // невидимым.
        if (this.TryFindResource("AxYelBrush", ActualThemeVariant, out var yellow) &&
            yellow is Avalonia.Media.IBrush brush)
        {
            alert.Foreground = brush;
        }

        if (danger)
            agree.Classes.Add("danger");
        else
            agree.Classes.Add("accent");

        var dialog = new AxDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 420,
            },
            AlertIcon = alert,
            Buttons = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                Children = { cancel, agree },
            },
        };

        cancel.Click += (_, _) => dialog.Close(false);
        agree.Click += (_, _) => dialog.Close(true);

        return await dialog.ShowDialog<bool?>(this) == true;
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
                var agreed = await ConfirmAsync(
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

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings)
            return;

        var theme = ThemeSwitch.SelectedIndex == 1 ? StudioTheme.Light : StudioTheme.Dark;
        _model.SettingsStore.Current.Theme = theme;
        _model.SettingsStore.Save();

        StudioTheming.Apply(theme);
    }

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || LanguageBox.SelectedItem is not ContentControl { Tag: string language })
            return;

        Localizer.Instance.SetLanguage(language);
        _model.SettingsStore.Current.Language = language;
        _model.SettingsStore.Save();

        // Строки студии обновит привязка, а имена и подписи плагинов лежат в их
        // собственных словарях и читаются при обходе каталога — значит, обойти
        // его надо заново, иначе половина экрана осталась бы на прежнем языке.
        _model.RefreshPlugins();
    }

    private void LoadSettingsIntoControls()
    {
        _loadingSettings = true;
        try
        {
            var settings = _model.SettingsStore.Current;

            ThemeSwitch.SelectedIndex = settings.Theme == StudioTheme.Light ? 1 : 0;
            ShowLanguages();
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void OpenProject(string path)
    {
        if (!File.Exists(path))
        {
            _model.Status = $"{Localizer.Instance["common.error"]}: {path} — {Localizer.Instance["projects.missing"]}";
            _model.RefreshRecent();
            return;
        }

        _model.Recent.Touch(path);
        ProjectRequested?.Invoke(this, path);
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
