using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Каретка в панелях расширений.
/// </summary>
/// <remarks>
/// Панель открывают, чтобы ею работать, и место, с которого работа начинается,
/// знает она одна: поле поиска, дерево, кнопка действия. Не назвав его, панель
/// получает каретку на первый контрол, который умеет её взять, — часто на
/// кнопку, которую человек не собирался нажимать.
/// <para>
/// Очередь общая: подписи панелей привязываются к словарям, а <c>Localizer</c>
/// один на процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginFocusTests : IDisposable
{
    private readonly StudioLog _log = new();
    private readonly PluginGuard _guard = new();
    private readonly PluginContributionRegistry _contributions = new();

    private StudioPlugins? _plugins;

    public void Dispose()
    {
        // Хост отпускается первым: пока жив контекст загрузки, его файлы
        // держит процесс.
        _plugins?.Stop();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Названная панелью цель получает каретку вперёд первого встречного.
    /// </summary>
    /// <remarks>
    /// Цель здесь стоит второй нарочно: назови панель первый контрол, проверка
    /// прошла бы и без всякой передачи — первого студия берёт и сама.
    /// </remarks>
    [AvaloniaFact]
    public void The_target_a_panel_names_gets_the_caret_before_the_first_comer()
    {
        var (dock, plugins) = Raised();

        Assert.True(dock.Focus("arxis.aim:panel"), "внутри панели не нашлось, кому отдать каретку");

        var panel = Named(dock, "arxis.aim:panel");
        var places = Places(panel);

        Assert.Equal(2, places.Count);
        Assert.False(places[0].IsFocused, "каретка ушла первому встречному, а не названному");
        Assert.True(places[1].IsFocused, "названная панелью цель каретки не получила");

        _ = plugins;
    }

    /// <summary>
    /// Цель из чужой панели отвергается молча.
    /// </summary>
    /// <remarks>
    /// Панель могла назвать что угодно, и опасен здесь не промах, а точное
    /// попадание: контрол соседней панели. Каретка, уехавшая туда по просьбе
    /// той, которую открыли, — это кража, и человек её не поймёт.
    /// <para>
    /// Цель берётся живая, стоящая в дереве: названный контрол, которого в
    /// дереве нет вовсе, отвергается сам собой — фокус ему не даст Avalonia, — и
    /// проверял бы такой тест не то, что написано в его имени.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void A_target_that_lives_in_another_panel_is_refused()
    {
        var (dock, _) = Raised();

        var neighbour = Places(Named(dock, "arxis.aim:panel"))[1];

        Assert.True(dock.Focus("arxis.aim:stray"), "внутри панели не нашлось, кому отдать каретку");

        Assert.False(neighbour.IsFocused, "каретка уехала в соседнюю панель по просьбе этой");
        Assert.True(Assert.Single(Places(Named(dock, "arxis.aim:stray"))).IsFocused);
    }

    /// <summary>
    /// Служба достаёт только свои панели.
    /// </summary>
    /// <remarks>
    /// Имя плагина подставляет служба, а не плагин: короткое имя — всё, что он
    /// знает, и дотянуться до чужой панели он не может по построению.
    /// </remarks>
    [AvaloniaFact]
    public void The_service_reaches_only_the_plugins_own_panel()
    {
        var (dock, _) = Raised();

        var mine = new PluginFocus(dock, "arxis.aim");
        var stranger = new PluginFocus(dock, "arxis.other");

        Assert.True(mine.Focus("panel"));
        Assert.True(mine.IsFocused("panel"));

        Assert.False(stranger.Focus("panel"), "чужая панель досталась не своему хозяину");
        Assert.False(stranger.IsFocused("panel"));
        Assert.False(mine.Focus("нет.такой"));
    }

    /// <summary>Контрол панели в раскладке.</summary>
    private static Control Named(StudioDock dock, string id) =>
        dock.Items.Find(id)?.Content ?? throw new InvalidOperationException($"панели {id} в раскладке нет");

    /// <summary>Места, куда может встать каретка, по порядку.</summary>
    private static IReadOnlyList<Control> Places(Control panel) =>
        [.. panel.GetVisualDescendants().OfType<Control>().Where(candidate => candidate.Focusable)];

    /// <summary>Поднимает модуль с панелью, которая называет свою цель.</summary>
    private (StudioDock Dock, StudioPlugins Plugins) Raised()
    {
        var view = new DockView();
        var dock = new StudioDock(view);

        new Window { Width = 900, Height = 600, Content = view }.Show();
        dock.Shown();

        var plugins = new StudioPlugins(_log, _guard, new StudioTaskRegistry(), _contributions)
        {
            Assemblies = [TestAssembly.Emit("Probe.Aim", Source, Manifest)],
            Commands = new StudioCommands(),
            Dock = dock,
            ToolBar = new StudioToolBar(new ToolBarStrip(), new ToolBarStrip(), new ToolBarStrip()),
            Documents = new StudioDocuments(dock, _contributions.EditorFor, new Quiet()),
            Services = new Dictionary<Type, object>
            {
                [typeof(PluginContributionRegistry)] = _contributions,
                [typeof(PluginGuard)] = _guard,
            },
            Catalog = () => [],
        };

        _plugins = plugins;

        plugins.LoadModules();
        Dispatcher.UIThread.RunJobs();

        return (dock, plugins);
    }

    /// <summary>Панель с двумя местами для каретки; названо второе.</summary>
    private const string Source = """
        using ArxisStudio.Sdk;
        using Avalonia.Controls;

        namespace Probe;

        public sealed class AimModule : StudioPlugin
        {
            public override void Activate(IStudioContext context)
            {
            }
        }

        // Место встречи двух панелей: так вторая дотягивается до контрола
        // первой — ровно то, что студия обязана отвергнуть.
        public static class Neighbourhood
        {
            public static Control? Aim;
        }

        [ToolWindow("panel")]
        public sealed class AimPanel : ToolWindow
        {
            private Control? _aim;

            public override Control? FocusTarget => _aim;

            protected override Control Build()
            {
                var first = new Border { Focusable = true, Height = 20 };

                _aim = new Border { Focusable = true, Height = 20 };
                Neighbourhood.Aim = _aim;

                return new StackPanel { Children = { first, _aim } };
            }
        }

        [ToolWindow("stray")]
        public sealed class StrayPanel : ToolWindow
        {
            // Цель из чужой панели — живая и стоящая в дереве.
            public override Control? FocusTarget => Neighbourhood.Aim;

            protected override Control Build() =>
                new StackPanel { Children = { new Border { Focusable = true, Height = 20 } } };
        }
        """;

    /// <summary>Статус, которому некому докладывать.</summary>
    private sealed class Quiet : IStudioStatus
    {
        public void Show(string message)
        {
        }
    }

    private const string Manifest = """
        {
          "id": "arxis.aim",
          "name": "Прицел",
          "version": "1.0.0",
          "contributions": {
            "toolWindows": [
              { "id": "panel", "title": "Прицел", "placement": { "side": "left" } },
              { "id": "stray", "title": "Мимо", "placement": { "side": "right" } }
            ]
          },
          "activation": [ "onStartup" ]
        }
        """;
}
