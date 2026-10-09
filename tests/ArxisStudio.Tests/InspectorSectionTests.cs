using System.Runtime.CompilerServices;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Panel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Разделы плагинов в инспекторе: строки, которые раздел взял себе, уходят из списка, раздел «целиком» заменяет
/// их все, раздел шапки встаёт под именем цели, а выгруженный плагин уносит свои разделы (запись 362).
/// </summary>
[Collection(StudioStateCollection.Name)]
public class InspectorSectionTests
{
    /// <summary>
    /// Раздел встаёт своим заголовком, а свойства, которые он взял, — строками внутри него, с тем же редактором и
    /// значением; в списке их больше нет.
    /// </summary>
    [AvaloniaFact]
    public void A_section_takes_the_rows_it_claims_and_shows_them_itself()
    {
        using var studio = new InspectorStudio();

        Plugin(studio);
        studio.Raise();

        var panel = studio.Panel();
        var target = Button();

        target.Put("Width", new InspectorValue("120") { Source = InspectorValueSource.Local });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var ids = Cells(panel).Select(static cell => cell.Row.Property.Id).ToList();
        var section = Assert.Single(panel.List!.Items.OfType<SectionCell>());
        var rows = section.Host.GetVisualDescendants().OfType<InspectorRow>().Select(static row => row.Property.Id).ToList();

        Assert.DoesNotContain("Width", ids);
        Assert.DoesNotContain("Height", ids);
        Assert.Contains("Opacity", ids);
        Assert.Equal(["Width", "Height"], rows);
        Assert.Equal("Размер", section.Host.Title);
        Assert.Contains(panel.List.Items.OfType<GroupCell>(), group => group.CategoryId == "@Размер");
        Assert.True(target.Asked.ContainsKey("Width"), "строка раздела не спросила значения");
    }

    /// <summary>
    /// При поиске раздел остаётся, только если ему отвечает заголовок или взятое им свойство, — как строка, которой
    /// отвечает подпись (найдено живой проверкой записи 365: раздел доступности стоял в поиске «Row»).
    /// </summary>
    [AvaloniaFact]
    public void A_search_keeps_a_section_only_when_its_title_or_its_claims_match()
    {
        using var studio = new InspectorStudio();

        Plugin(studio);
        studio.Raise();

        var panel = studio.Panel();

        using var shown = studio.Service.Show(Button());

        studio.Frame();

        foreach (var (query, kept) in new[] { ("Opac", false), ("Wid", true), ("Разм", true), ("", true) })
        {
            panel.List!.Query = query;
            studio.Frame();

            Assert.True(
                panel.List.Items.OfType<SectionCell>().Any() == kept,
                kept ? $"поиск «{query}» убрал раздел, которому он отвечает" : $"поиск «{query}» оставил раздел, которому не отвечает");
        }
    }

    /// <summary>Раздел шапки встаёт под именем цели, а не в список.</summary>
    [AvaloniaFact]
    public void A_header_section_stands_under_the_targets_name()
    {
        using var studio = new InspectorStudio();

        Plugin(studio);
        studio.Raise();

        var panel = studio.Panel();

        using var shown = studio.Service.Show(Button());

        studio.Frame();

        var header = Assert.IsType<SectionHost>(Assert.Single(panel.View!.HeaderSections.Children));

        Assert.Equal("Быстрое", header.Title);
        Assert.True(panel.View.HeaderSections.IsVisible);
        Assert.DoesNotContain(panel.List!.Items.OfType<SectionCell>(), cell => cell.Host.Title == "Быстрое");
    }

    /// <summary>Раздел «целиком» показывает цель сам: строк по умолчанию нет вовсе.</summary>
    [AvaloniaFact]
    public void A_section_that_replaces_all_leaves_no_default_rows()
    {
        using var studio = new InspectorStudio();

        Plugin(studio);
        studio.Raise();

        var panel = studio.Panel();
        var target = new FakeTarget(typeof(Border), FakeTarget.Property("Width", typeof(double)), FakeTarget.Property("Padding", typeof(Avalonia.Thickness)));

        using var shown = studio.Service.Show(target);

        studio.Frame();

        Assert.Empty(Cells(panel));
        Assert.Equal("Целиком", Assert.Single(panel.List!.Items.OfType<SectionCell>()).Host.Title);
    }

    /// <summary>
    /// Выгруженный плагин уносит свои разделы: строки возвращаются в список, а контекст плагина уходит из процесса.
    /// </summary>
    [AvaloniaFact]
    public void An_unloaded_plugin_takes_its_sections_away_and_the_rows_come_back()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Button();

        using var shown = studio.Service.Show(target);

        var context = RaiseAndDrop(studio, panel);

        studio.Frame();

        Assert.Empty(panel.List!.Items.OfType<SectionCell>());
        Assert.Empty(panel.View!.HeaderSections.Children);
        Assert.Contains(Cells(panel), cell => cell.Row.Property.Id == "Width");

        for (var attempt = 0; attempt < 10 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(context.IsAlive, "разделы выгруженного плагина держат его контекст");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RaiseAndDrop(InspectorStudio studio, InspectorPanel panel)
    {
        Plugin(studio);
        studio.Raise();
        studio.Frame();

        Assert.Single(panel.List!.Items.OfType<SectionCell>());

        var context = new WeakReference(studio.Host.Loaded.Single(plugin => plugin.Installed.Id == "probe.sections").Context);

        Assert.True(studio.Host.Drop("probe.sections"));

        return context;
    }

    private static FakeTarget Button() => new(
        typeof(Avalonia.Controls.Button),
        FakeTarget.Property("Width", typeof(double)),
        FakeTarget.Property("Height", typeof(double)),
        FakeTarget.Property("Opacity", typeof(double)));

    private static List<PropertyCell> Cells(InspectorPanel panel) =>
        panel.List!.Items.OfType<PropertyCell>().ToList();

    /// <summary>Плагин с тремя разделами: взявший размер у кнопки, шапки у кнопки и «целиком» у рамки.</summary>
    private static void Plugin(InspectorStudio studio) =>
        TestAssembly.EmitPlugin(studio.Plugins, "probe.sections", "Probe.Sections", source: """
            using System.Collections.Generic;
            using ArxisStudio.Inspector;
            using ArxisStudio.Sdk;
            using Avalonia.Controls;

            namespace Probe.Sections;

            public sealed class Entry : StudioPlugin
            {
            }

            [InspectorSection(typeof(Button))]
            public sealed class SizeSection : InspectorSection
            {
                public override string Title => "Размер";

                public override IReadOnlyCollection<string> Claims => ["Width", "Height"];

                public override InspectorSectionView CreateView(InspectorSectionContext context) => new Rows(context, "Width", "Height");
            }

            [InspectorSection(typeof(Button))]
            public sealed class QuickSection : InspectorSection
            {
                public override string Title => "Быстрое";

                public override InspectorSectionPlacement Placement => InspectorSectionPlacement.Header;

                public override InspectorSectionView CreateView(InspectorSectionContext context) => new Rows(context);
            }

            [InspectorSection(typeof(Border))]
            public sealed class WholeSection : InspectorSection
            {
                public override string Title => "Целиком";

                public override bool ReplaceAll => true;

                public override InspectorSectionView CreateView(InspectorSectionContext context) => new Rows(context, "Padding");
            }

            public sealed class Rows : InspectorSectionView
            {
                private readonly StackPanel _rows = new();

                public Rows(InspectorSectionContext context, params string[] ids)
                {
                    foreach (var id in ids)
                    {
                        if (context.CreateRow(id) is { } row)
                            _rows.Children.Add(row);
                    }
                }

                public override Control View => _rows;
            }
            """);
}
