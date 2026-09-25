using System.Reflection;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArxisStudio.Tests;

/// <summary>
/// Служба расширений, собранная так, как её собирает главное окно, — над временным файлом настроек.
/// </summary>
/// <remarks>
/// Девять наборов собирали службу руками, и собирали по-разному: у одних шов сбоев доходил до
/// команд и документов, у других нет, а пятеро не называли файла настроек вовсе — и хранилище по
/// умолчанию читало настоящую папку данных, а плагин, записавший настройку, писал бы в неё. Здесь
/// шов общий, как у окна, а файл настроек свой и стирается вместе с харнессом.
/// <para>
/// Части службы открыты: тест держит в руках реестр команд, раскладку, полосу и то, что сказала
/// строка состояния. Службу собирает <see cref="Build"/>, а останавливает <see cref="Dispose"/> —
/// раньше, чем стирает файл: пока жив контекст загрузки, его файлы держит процесс.
/// </para>
/// </remarks>
internal sealed class StudioPluginsHarness : IDisposable
{
    private readonly List<StudioPlugins> _built = [];

    /// <summary>Собирает части службы.</summary>
    /// <param name="view">Вид раскладки; null — свой, ещё нигде не показанный.</param>
    public StudioPluginsHarness(DockView? view = null)
    {
        View = view ?? new DockView();
        Commands = new StudioCommands(Guard);
        Dock = new StudioDock(View);
        ToolBar = new StudioToolBar(Left, Center, Right) { Invoke = Commands.Invoke };
        Documents = new StudioDocuments(Dock, Contributions.EditorFor, Status, Guard);
    }

    /// <summary>Журнал студии.</summary>
    public StudioLog Log { get; } = new();

    /// <summary>Шов сбоев — общий у службы, команд и документов.</summary>
    public PluginGuard Guard { get; } = new();

    /// <summary>Реестр задач расширений.</summary>
    public StudioTaskRegistry Tasks { get; } = new();

    /// <summary>Реестр вкладов: редакторы, инспекторы.</summary>
    public PluginContributionRegistry Contributions { get; } = new();

    /// <summary>Реестр команд.</summary>
    public StudioCommands Commands { get; }

    /// <summary>Вид раскладки.</summary>
    public DockView View { get; }

    /// <summary>Раскладка, в которую встают панели.</summary>
    public StudioDock Dock { get; }

    /// <summary>Левый отрезок полосы.</summary>
    public ToolBarStrip Left { get; } = new();

    /// <summary>Средний отрезок полосы.</summary>
    public ToolBarStrip Center { get; } = new();

    /// <summary>Правый отрезок полосы.</summary>
    public ToolBarStrip Right { get; } = new();

    /// <summary>Полоса; щелчок по кнопке идёт через реестр команд, как у окна.</summary>
    public StudioToolBar ToolBar { get; }

    /// <summary>Что студия сказала человеку.</summary>
    public StatusProbe Status { get; } = new();

    /// <summary>Документы.</summary>
    public StudioDocuments Documents { get; }

    /// <summary>Файл пользовательских настроек расширений — свой на харнесс.</summary>
    public string SettingsFile { get; } =
        Path.Combine(Path.GetTempPath(), $"arxis-plugin-settings-{Guid.NewGuid():N}.json");

    /// <summary>Служба, собранная последней; null — ещё не собрана.</summary>
    public StudioPlugins? Plugins => _built.LastOrDefault();

    /// <summary>Собирает службу, никого не поднимая.</summary>
    /// <param name="catalog">Где служба берёт установленные плагины; null — их нет.</param>
    /// <param name="modules">Сборки встроенных модулей; null — ни одного.</param>
    /// <param name="shortcuts">Реестр сочетаний; null — сочетания манифестов не раздаются.</param>
    /// <remarks>
    /// Собрать можно и второй раз — так страница плагинов открывается заново над той же папкой, — и
    /// остановлены будут все собранные.
    /// </remarks>
    public StudioPlugins Build(
        Func<IReadOnlyList<InstalledPlugin>>? catalog = null,
        IReadOnlyList<Assembly>? modules = null,
        StudioShortcuts? shortcuts = null)
    {
        var plugins = new StudioPlugins(Log, Guard, Tasks, Contributions)
        {
            Commands = Commands,
            Dock = Dock,
            ToolBar = ToolBar,
            Documents = Documents,
            Shortcuts = shortcuts,
            Services = new Dictionary<Type, object>
            {
                [typeof(PluginContributionRegistry)] = Contributions,
                [typeof(PluginGuard)] = Guard,
            },
            Catalog = catalog ?? (() => []),
            Assemblies = modules ?? [],
            Settings = new PluginSettingsStore(userFile: SettingsFile),
        };

        _built.Add(plugins);

        return plugins;
    }

    /// <summary>Показывает раскладку в окне: панели получают размер, а вкладки — место.</summary>
    /// <param name="width">Ширина окна.</param>
    /// <param name="height">Высота окна.</param>
    public Window Show(double width = 900, double height = 600)
    {
        var window = new Window { Width = width, Height = height, Content = View };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    /// <summary>
    /// Поднимает встроенный модуль из исходника и манифеста в показанной раскладке.
    /// </summary>
    /// <param name="name">Имя сборки модуля.</param>
    /// <param name="source">Код модуля.</param>
    /// <param name="manifest">Его <c>module.json</c>.</param>
    /// <param name="shortcuts">Реестр сочетаний; null — сочетания манифеста не раздаются.</param>
    public StudioPlugins Raise(string name, string source, string manifest, StudioShortcuts? shortcuts = null)
    {
        Show();
        Dock.Shown();

        var plugins = Build(modules: [TestAssembly.EmitModule(name, source, manifest)], shortcuts: shortcuts);

        plugins.LoadModules();
        Dispatcher.UIThread.RunJobs();

        return plugins;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var plugins in _built)
            plugins.Stop();

        try
        {
            File.Delete(SettingsFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
