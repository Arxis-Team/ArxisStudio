using ArxisStudio.Docking;
using ArxisStudio.Services;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
    private readonly StudioPluginsHarness _studio = new();

    public void Dispose()
    {
        _studio.Dispose();
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
    /// Цель панели переживает запомненного хранителя: ушёл он — каретка идёт к цели, а не к первому
    /// встречному.
    /// </summary>
    /// <remarks>
    /// Раскладка запоминает, где стояла каретка, всякий раз, как та уходит из панели. Цель прежде
    /// клалась первым хранителем и стиралась этим же запоминанием, а запомненный потом умирал —
    /// закрытый сеанс терминала, перестроенная строка, — и каретка шла к первому, кто может.
    /// </remarks>
    [AvaloniaFact]
    public void The_target_outlives_the_one_remembered_after_it()
    {
        var (dock, _) = Raised();

        var panel = Named(dock, "arxis.aim:fickle");
        var places = Places(panel);

        Assert.Equal(3, places.Count);
        Assert.True(dock.Focus("arxis.aim:fickle"));
        Assert.True(places[2].IsFocused, "названная панелью цель каретки не получила");

        places[1].Focus();
        DockFocus.Remember(panel);
        ((Panel)places[1].GetVisualParent()!).Children.Remove(places[1]);

        Assert.True(dock.Focus("arxis.aim:panel"), "каретку из панели не увести");

        Assert.True(dock.Focus("arxis.aim:fickle"), "внутри панели не нашлось, кому отдать каретку");
        Assert.True(places[2].IsFocused, "запомненный ушёл, и каретка досталась первому встречному, а не цели");
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
    private (StudioDock Dock, StudioPlugins Plugins) Raised() =>
        (_studio.Dock, _studio.Raise("Probe.Aim", Source, Manifest));

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

        // Три места: первое, проходное — его потом уберут, — и названное.
        [ToolWindow("fickle")]
        public sealed class FicklePanel : ToolWindow
        {
            private Control? _aim;

            public override Control? FocusTarget => _aim;

            protected override Control Build()
            {
                _aim = new Border { Focusable = true, Height = 20 };

                return new StackPanel
                {
                    Children =
                    {
                        new Border { Focusable = true, Height = 20 },
                        new Border { Focusable = true, Height = 20 },
                        _aim,
                    },
                };
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

    private const string Manifest = """
        {
          "id": "arxis.aim",
          "name": "Прицел",
          "version": "1.0.0",
          "contributions": {
            "toolWindows": [
              { "id": "panel", "title": "Прицел", "placement": { "side": "left" } },
              { "id": "stray", "title": "Мимо", "placement": { "side": "right" } },
              { "id": "fickle", "title": "Проход", "placement": { "side": "bottom" } }
            ]
          },
          "activation": [ "onStartup" ]
        }
        """;
}
