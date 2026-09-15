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
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace ArxisStudio.Welcome;

/// <summary>
/// Экран Welcome: недавние проекты, обучение, плагины и настройки.
/// </summary>
/// <remarks>
/// Сам экран проекты не открывает. Он отвечает на один вопрос — есть ли кому открывать и годится
/// ли путь, — и просит об открытии через <see cref="ProjectRequested"/>; загрузку ведёт студия, где
/// её видно задачей в статус-баре. Ждать её здесь значило бы держать на экране окно, которому пора
/// закрыться.
/// <para>
/// Отказ, наоборот, остаётся здесь: окно закрывается, как только проект открыт, и сказанное после
/// этого человеку показать уже негде.
/// </para>
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
        CanOpenProjects = () => extensions.Projects is not null;
        DataContext = _model;

        InitializeComponent();
    }

    /// <summary>
    /// Есть ли кому открывать; в продукте — поднялась ли служба проектов.
    /// </summary>
    /// <remarks>
    /// Шов ради тестов, и узкий нарочно: вопрос здесь один и булев, а собрать в тесте живую службу
    /// значило бы поднять модуль вместе с движком — ради ответа «да».
    /// </remarks>
    internal Func<bool> CanOpenProjects { get; init; }

    /// <summary>
    /// Страница «Клавиши» для окна настроек; <c>null</c> — окно без неё.
    /// </summary>
    /// <remarks>
    /// Сочетания раздаёт окно студии, и спрашивать страницу надо у него: собрано оно раньше Welcome, и
    /// сочетания в нём уже розданы. Прежде настройки из Welcome страницы не показывали вовсе, хотя
    /// открывают их отсюда чаще, чем из студии.
    /// </remarks>
    internal Func<KeysPage>? Keys { get; init; }

    /// <summary>Пользователь просит открыть студию без проекта.</summary>
    public event EventHandler? StudioRequested;

    /// <summary>Пользователь просит открыть проект; в аргументе — путь к нему.</summary>
    public event EventHandler<string>? ProjectRequested;

    /// <summary>Говорит полосой состояния — той же, что отвечает на отказы.</summary>
    /// <param name="message">Что сказать.</param>
    internal void Say(string message) => _model.Status = message;

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
            await SettingsWindow.ShowAsync(this, _settings, _extensions, _extensions.Declaring(), _model.Plugins, keys: Keys?.Invoke());
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
                this, _settings, _extensions, _extensions.Declaring(), _model.Plugins, "studio.plugins", Keys?.Invoke());
        }
        finally
        {
            _model.IsPluginsOpen = false;
        }
    }

    private void Select(WelcomeSection section) => _model.Section = section;

    private void OnStudioClick(object? sender, RoutedEventArgs e) =>
        StudioRequested?.Invoke(this, EventArgs.Empty);

    private void OnLinkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string url })
            StudioOpen.InShell(url);
    }

    /// <summary>
    /// Щелчок по строке недавнего проекта.
    /// </summary>
    /// <remarks>
    /// Кнопка проверяется, и это не придирка: на строке висит контекстное меню, и без проверки
    /// правый щелчок открывал бы проект вместе с меню. Фокус ставится обеими кнопками — по нему
    /// работает Delete, и меню должно раскрываться на той строке, которую видно выбранной.
    /// </remarks>
    private void OnRecentPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: string path } row)
            return;

        row.Focus();

        if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            OpenProject(path);
    }

    /// <summary>Delete убирает строку, на которой стоит фокус.</summary>
    private void OnRecentKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || sender is not Control { Tag: string path })
            return;

        if (_model.RecentProjects.FirstOrDefault(project =>
                string.Equals(project.Path, path, StringComparison.OrdinalIgnoreCase)) is { } found)
        {
            _model.Remove(found);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Кнопка меню на строке раскрывает то же меню, что и правый щелчок.
    /// </summary>
    /// <remarks>
    /// Меню объявлено на строке один раз и показывается у кнопки: второй его список в разметке
    /// значило бы чинить всякую правку дважды и однажды забыть.
    /// </remarks>
    private void OnRecentMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control button && button.FindAncestorOfType<Border>() is { ContextFlyout: { } menu })
            menu.ShowAt(button);
    }

    private void OnRecentOpenClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: RecentProject project })
            OpenProject(project.Path);
    }

    private void OnRecentRevealClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: RecentProject project })
            StudioOpen.InShell(project.Folder);
    }

    private void OnRecentCopyPathClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: RecentProject project })
            _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(project.Path);
    }

    private void OnRecentRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: RecentProject project })
            _model.Remove(project);
    }

    /// <summary>
    /// Выбор проекта файловым диалогом.
    /// </summary>
    /// <remarks>
    /// Второй фильтр — «все файлы» — стоит нарочно: правило решает
    /// <see cref="WelcomeViewModel.Complaint"/>, а фильтр диалога только помогает не искать
    /// глазами. Выбранное мимо правила отвергается со словом, а не молча не открывается.
    /// </remarks>
    private async void OnOpenProjectClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Instance["projects.open"],
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Решение / проект")
                {
                    Patterns = ["*.sln", "*.slnx", "*.csproj", "*.fsproj", "*.vbproj"],
                },
                new FilePickerFileType(Localizer.Instance["common.all"]) { Patterns = ["*"] },
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            OpenProject(path);
    }

    private void OnNewProjectClick(object? sender, RoutedEventArgs e) =>
        _model.Status = Localizer.Instance["projects.new.later"];

    private void OnCloneClick(object? sender, RoutedEventArgs e) =>
        _model.Status = Localizer.Instance["vcs.clone.later"];

    private void OnDismissStatus(object? sender, RoutedEventArgs e) => _model.Status = null;

    /// <summary>
    /// Просит студию открыть проект — или объясняет, почему не просит.
    /// </summary>
    /// <param name="path">Путь к решению или проекту.</param>
    /// <remarks>
    /// <c>Touch</c> здесь не зовётся намеренно: недавние отмечает студия, и только на готовности —
    /// путь, который не прочёлся, в списке не нужен. Позвать его отсюда значило бы вернуть то, от
    /// чего <see cref="CurrentProject"/> отказался, и писать файл дважды на каждое открытие.
    /// </remarks>
    private void OpenProject(string path)
    {
        if (!CanOpenProjects())
        {
            _model.Status = Localizer.Instance["projects.noservice"];
            return;
        }

        if (_model.Complaint(path) is { } complaint)
        {
            _model.Status = complaint;

            // Файла не стало, пока список лежал на экране: чип «папка не найдена» должен
            // появиться на строке тем же щелчком, которым человек об этом узнал.
            _model.RefreshRecent();
            return;
        }

        ProjectRequested?.Invoke(this, path);
    }
}
