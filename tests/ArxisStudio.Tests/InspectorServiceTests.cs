using System.Runtime.CompilerServices;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Служба показа инспектора: стопка целей, замок, закрытые цели и цели выгруженного плагина (запись 362).
/// </summary>
/// <remarks>Выгрузка контекста плагина опирается на сборщик мусора — общий на процесс.</remarks>
[Collection(StudioStateCollection.Name)]
public class InspectorServiceTests
{
    /// <summary>
    /// Показан последний поставленный; его ручка, отпущенная, возвращает того, кто был до него, — как плагин,
    /// закончив, отдаёт инспектор дизайнеру.
    /// </summary>
    [Fact]
    public void The_last_shown_target_is_current_and_releasing_it_returns_the_one_before()
    {
        var service = new StudioInspector(contributions: null);
        var designer = new FakeTarget(typeof(Button));
        var plugin = new FakeTarget(typeof(Border));
        var changes = 0;

        service.CurrentChanged += (_, _) => changes++;

        using var first = service.Show(designer);
        var second = service.Show(plugin);

        Assert.Same(plugin, service.Current);
        Assert.Equal(2, changes);

        second.Dispose();

        Assert.Same(designer, service.Current);
        Assert.Equal(3, changes);
    }

    /// <summary>Отпущенная ручка того, кто под верхним, показанного не трогает.</summary>
    [Fact]
    public void Releasing_a_target_below_the_top_changes_nothing_shown()
    {
        var service = new StudioInspector(contributions: null);
        var below = service.Show(new FakeTarget(typeof(Button)));
        var top = new FakeTarget(typeof(Border));

        using var shown = service.Show(top);

        var changes = 0;

        service.CurrentChanged += (_, _) => changes++;
        below.Dispose();

        Assert.Same(top, service.Current);
        Assert.Equal(0, changes);
        Assert.Equal(1, service.Count);
    }

    /// <summary>
    /// Замок держит показанную цель: новые встают в стопку, а снятый замок показывает последнюю живую.
    /// </summary>
    [Fact]
    public void A_lock_keeps_the_shown_target_and_unlocking_shows_the_last_live_one()
    {
        var service = new StudioInspector(contributions: null);
        var locked = new FakeTarget(typeof(Button));
        var later = new FakeTarget(typeof(Border));

        using var first = service.Show(locked);

        service.Lock(true);

        using var second = service.Show(later);

        Assert.True(service.IsLocked);
        Assert.Same(locked, service.Current);

        service.Lock(false);

        Assert.False(service.IsLocked);
        Assert.Same(later, service.Current);
    }

    /// <summary>Закрытая цель уходит сама, а закрытую показать нельзя.</summary>
    [Fact]
    public void A_closed_target_leaves_by_itself_and_a_closed_one_is_never_shown()
    {
        var service = new StudioInspector(contributions: null);
        var designer = new FakeTarget(typeof(Button));
        var closing = new FakeTarget(typeof(Border));

        using var first = service.Show(designer);
        using var second = service.Show(closing);

        closing.Shut();

        Assert.Same(designer, service.Current);
        Assert.Equal(1, service.Count);

        using var third = service.Show(closing);

        Assert.Same(designer, service.Current);
        Assert.Equal(1, service.Count);
    }

    /// <summary>
    /// Закреплённую цель закрыли — показанной нет, пока замок не снимут: молча подменить закреплённое другим
    /// значило бы обмануть того, кто его закрепил.
    /// </summary>
    [Fact]
    public void When_the_locked_target_closes_nothing_is_shown_until_the_lock_is_lifted()
    {
        var service = new StudioInspector(contributions: null);
        var waiting = new FakeTarget(typeof(Button));
        var locked = new FakeTarget(typeof(Border));

        using var first = service.Show(waiting);
        using var second = service.Show(locked);

        service.Lock(true);
        locked.Shut();

        Assert.Null(service.Current);
        Assert.True(service.IsLocked);

        service.Lock(false);

        Assert.Same(waiting, service.Current);
    }

    /// <summary>
    /// Цель выгруженного плагина уходит из стопки, даже если плагин не отпустил её ручку, и не держит его контекст:
    /// показан снова тот, кто был до неё.
    /// </summary>
    [AvaloniaFact]
    public void A_target_of_an_unloaded_plugin_leaves_and_does_not_hold_its_context()
    {
        using var studio = new InspectorStudio();
        var designer = new FakeTarget(typeof(Button));

        using var shown = studio.Service.Show(designer);

        var context = ShowFromPluginAndDrop(studio);

        Assert.Same(designer, studio.Service.Current);
        Assert.Equal(1, studio.Service.Count);

        for (var attempt = 0; attempt < 10 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(context.IsAlive, "цель выгруженного плагина держит его контекст загрузки");
    }

    /// <summary>
    /// Поднимает плагин, который показывает свою цель и ручку не отпускает, и снимает его.
    /// </summary>
    /// <remarks>
    /// Не встраиваемый: запись плагина, оставшаяся в кадре вызывающего, держала бы контекст живым, и проверка
    /// показала бы только это.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ShowFromPluginAndDrop(InspectorStudio studio)
    {
        TestAssembly.EmitPlugin(studio.Plugins, "probe.shower", "Probe.Shower", source: """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using ArxisStudio.Inspector;
            using ArxisStudio.Sdk;

            namespace Probe.Shower;

            public sealed class Entry : StudioPlugin
            {
                public override void Activate(IStudioContext context) => context.Inspector()!.Show(new OwnTarget());
            }

            public sealed class OwnTarget : InspectorTarget
            {
                public override InspectorHeader Header => new("Своё");

                public override InspectorTypeInfo Type => new("Probe.Own");

                public override IReadOnlyList<InspectorProperty> Properties => [];

                public override InspectorValue ValueOf(string id) => InspectorValue.Empty;

                public override Task<bool> WriteAsync(IReadOnlyList<InspectorWrite> writes, string? label = null) =>
                    Task.FromResult(false);
            }
            """);

        studio.Raise();

        Assert.Equal("Probe.Own", studio.Service.Current?.Type.FullName);

        var context = new WeakReference(studio.Host.Loaded.Single(plugin => plugin.Installed.Id == "probe.shower").Context);

        Assert.True(studio.Host.Drop("probe.shower"));

        return context;
    }
}
