using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using ArxisStudio.Settings;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Settings;
using ArxisStudio.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Клавиатура в окне настроек: где она стоит, когда окно открылось, и что делает Esc.
/// </summary>
/// <remarks>
/// Запись 148 научила Esc диалог, а окно настроек построено на <c>AxWindow</c>, и клавиша до
/// него не доходила. Живая проверка нашла и причину, по которой этого не заметили: фокуса в
/// открытом окне не было ни у кого, и нажатию некуда было идти вовсе.
/// <para>
/// Окно модальное, поэтому каждое ожидание закрытия — со сторожем: не отозвавшееся на клавишу
/// повесило бы весь прогон вместо того, чтобы честно упасть.
/// </para>
/// <para>
/// Очередь общая с остальными: страница оформления читает словари, а <c>Localizer</c> один на
/// процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class SettingsKeyboardTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly string _home = Path.Combine(Path.GetTempPath(), $"arxis-settings-keys-{Guid.NewGuid():N}");
    private readonly StudioLog _log = new();
    private readonly PluginGuard _guard = new();
    private readonly StudioTaskRegistry _tasks = new();
    private readonly PluginContributionRegistry _contributions = new();

    public SettingsKeyboardTests() => Directory.CreateDirectory(Path.Combine(_home, "plugins"));

    public void Dispose()
    {
        if (Directory.Exists(_home))
            Directory.Delete(_home, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Настройки открываются с кареткой в поиске.
    /// </summary>
    /// <remarks>
    /// Так открываются настройки Rider: окно зовут, чтобы найти настройку. Прежде фокуса в
    /// открытом окне не было ни у кого — первое нажатие уходило в пустоту, а инструменты
    /// разработчика отказывались послать клавишу вовсе.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_settings_open_with_the_caret_in_the_search_field()
    {
        var (owner, settings, shown) = Open();

        var focused = settings.FocusManager?.GetFocusedElement() as Visual;

        Assert.True(
            focused is not null && (focused == settings.SearchBox || settings.SearchBox.IsVisualAncestorOf(focused)),
            $"фокус у {focused?.GetType().Name ?? "никого"}, а не в поиске");

        Escape(settings);

        Assert.Same(shown, await Task.WhenAny(shown, Task.Delay(Patience)));

        owner.Close();
    }

    /// <summary>Нечего терять — Esc закрывает настройки сразу.</summary>
    [AvaloniaFact]
    public async Task Escape_closes_the_settings_when_nothing_has_changed()
    {
        var (owner, settings, shown) = Open();

        Escape(settings);

        Assert.Same(shown, await Task.WhenAny(shown, Task.Delay(Patience)));

        owner.Close();
    }

    /// <summary>
    /// Несохранённое Esc молча не выбрасывает: спрашивает, и «нет» оставляет правку на месте.
    /// </summary>
    /// <remarks>
    /// Esc жмут и затем, чтобы закрыть подсказку или список, и второй нажатый подряд не должен
    /// стоить человеку набранного. Поэтому клавиша идёт дорогой крестика, а не «Отмены».
    /// </remarks>
    [AvaloniaFact]
    public async Task Escape_asks_before_it_drops_unsaved_changes()
    {
        var (owner, settings, shown) = Open();
        var row = Rows(settings).Single();

        row.Text = "20";
        Escape(settings);

        var question = Assert.Single(settings.OwnedWindows.OfType<AxDialog>());

        Assert.False(shown.IsCompleted, "Esc выбросил несохранённую правку, не спросив");

        // Esc на вопросе — это «нет»: окно остаётся, правка в нём.
        Escape(question);

        Assert.False(shown.IsCompleted, "ответ «нет» закрыл настройки");
        Assert.Equal("20", row.Text);

        settings.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Same(shown, await Task.WhenAny(shown, Task.Delay(Patience)));

        owner.Close();
    }

    /// <summary>Esc с модификатором окно не закрывает: это уже другое сочетание.</summary>
    [AvaloniaFact]
    public async Task Escape_with_a_modifier_is_not_escape()
    {
        var (owner, settings, shown) = Open();

        settings.KeyPress(Key.Escape, RawInputModifiers.Shift, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.False(shown.IsCompleted, "Shift+Esc закрыл настройки");

        Escape(settings);

        Assert.Same(shown, await Task.WhenAny(shown, Task.Delay(Patience)));

        owner.Close();
    }

    private (Window Owner, SettingsWindow Settings, Task Shown) Open()
    {
        var owner = new Window { Width = 400, Height = 300 };

        owner.Show();
        Dispatcher.UIThread.RunJobs();

        var catalog = new PluginCatalog(Path.Combine(_home, "plugins"));
        var shown = SettingsWindow.ShowAsync(
            owner,
            new JsonSettingsStore(Path.Combine(_home, "settings.json")),
            Extensions(catalog),
            [Module()],
            catalog);

        Dispatcher.UIThread.RunJobs();

        return (owner, Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>()), shown);
    }

    private static void Escape(Window window)
    {
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();
    }

    private static IReadOnlyList<PluginSettingRow> Rows(SettingsWindow settings) =>
        ((SettingsViewModel)settings.DataContext!).Nodes
            .SelectMany(node => node.Children)
            .Select(node => node.Page)
            .OfType<ExtensionPage>()
            .SelectMany(page => page.Rows)
            .ToList();

    /// <summary>Служба расширений, собранная, но не поднятая, — со своим файлом настроек.</summary>
    private StudioPlugins Extensions(PluginCatalog catalog)
    {
        var dock = new StudioDock(new DockView());

        return new StudioPlugins(_log, _guard, _tasks, _contributions)
        {
            Commands = new StudioCommands(_guard),
            Dock = dock,
            ToolBar = new StudioToolBar(new ToolBarStrip(), new ToolBarStrip(), new ToolBarStrip()),
            Documents = new StudioDocuments(dock, _contributions.EditorFor, new Silence()),
            Services = new Dictionary<Type, object>(),
            Catalog = catalog.Scan,
            Assemblies = [],
            Settings = new PluginSettingsStore(userFile: Path.Combine(_home, "plugin-settings.json")),
        };
    }

    /// <summary>Модуль, объявивший одну настройку: правка в ней и есть несохранённое.</summary>
    private static InstalledPlugin Module() => new(
        AppContext.BaseDirectory,
        new PluginManifest
        {
            Id = "arxis.terminal",
            Name = "Терминал",
            Contributions = new PluginContributions
            {
                Settings = { new PluginSetting("terminal.fontSize", "number", "user", "Кегль", 13) },
            },
        },
        Error: null,
        IsEnabled: true,
        IsBuiltIn: true);

    /// <summary>Строка состояния, которая молчит.</summary>
    private sealed class Silence : IStudioStatus
    {
        public void Show(string message)
        {
        }
    }
}
