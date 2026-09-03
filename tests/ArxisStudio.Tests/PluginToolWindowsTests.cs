using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Служба панелей: плагин достаёт свою панель на видное место.
/// </summary>
/// <remarks>
/// Очередь общая с остальными: заголовки панелей привязываются к словарям, а
/// <c>Localizer</c> один на процесс.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginToolWindowsTests
{
    /// <summary>
    /// Показывается своя панель по короткому имени; чужая и несуществующая — нет.
    /// </summary>
    /// <remarks>
    /// Имя плагина подставляет служба, а не плагин: манифест обещает
    /// уникальность только внутри плагина, и короткое имя — всё, что он знает.
    /// </remarks>
    [AvaloniaFact]
    public void Show_reaches_only_the_plugins_own_panel()
    {
        var (dock, chosen) = Dock();
        var service = new PluginToolWindows(dock, "arxis.probe");

        service.Show("panel");
        service.Show("нет.такой");

        Assert.Equal(["arxis.probe:panel"], chosen);
    }

    /// <summary>
    /// Из фонового потока показ доходит до дока, а не падает по дороге.
    /// </summary>
    /// <remarks>
    /// Плагин зовёт службу откуда угодно — из фоновой задачи в том числе, — а
    /// док живёт на потоке интерфейса и обращение с чужого потока встретил бы
    /// исключением.
    /// </remarks>
    [AvaloniaFact]
    public void Show_from_a_background_thread_reaches_the_dock()
    {
        var (dock, chosen) = Dock();
        var service = new PluginToolWindows(dock, "arxis.probe");

        // Исключение с фонового потока приедет сюда: ждём именно его отсутствия.
        Task.Run(() => service.Show("panel")).GetAwaiter().GetResult();

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["arxis.probe:panel"], chosen);
    }

    /// <summary>Фабрика выдаёт службу только студии с доком — и уже именной.</summary>
    [AvaloniaFact]
    public void The_factory_hands_the_service_only_when_it_has_a_dock()
    {
        var (dock, chosen) = Dock();

        var plugin = new InstalledPlugin(
            Path.GetTempPath(),
            new PluginManifest { Id = "arxis.probe", Name = "Проба" },
            null,
            IsEnabled: true);

        var with = new StudioContextFactory(new StudioLog(), new StudioCommands(), null, dock: dock).Create(plugin);
        var without = new StudioContextFactory(new StudioLog(), new StudioCommands(), null).Create(plugin);

        Assert.Null(without.GetService<IStudioToolWindows>());

        var service = with.GetService<IStudioToolWindows>();

        Assert.NotNull(service);

        service!.Show("panel");

        Assert.Equal(["arxis.probe:panel"], chosen);
    }

    /// <summary>Док с двумя панелями разных хозяев в показанном окне и список того, что он выбрал.</summary>
    private static (StudioDock Dock, List<string> Chosen) Dock()
    {
        var view = new DockView();
        var dock = new StudioDock(view);
        var window = new Window { Width = 800, Height = 600, Content = view };

        window.Show();

        dock.Add("arxis.probe", "arxis.probe:panel", new PluginPlacement { Side = "bottom" }, "Проба", PluginStrings.Studio, new Border());
        dock.Add("arxis.other", "arxis.other:panel", new PluginPlacement { Side = "bottom" }, "Чужая", PluginStrings.Studio, new Border());

        var chosen = new List<string>();

        dock.Chosen += (_, id) => chosen.Add(id);

        return (dock, chosen);
    }
}
