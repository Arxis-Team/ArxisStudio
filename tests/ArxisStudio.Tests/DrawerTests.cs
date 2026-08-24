using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Рисовальщики свойств и свои инспекторы: реестр вкладов и мост между строкой
/// инспектора и плагином.
/// </summary>
public class DrawerTests
{
    [AvaloniaFact]
    public async Task A_drawer_reads_the_row_and_writes_through_the_document()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;
        var button = fixture.Node("AddButton");

        var row = InspectorModel.Build(button, document.Session)
            .SelectMany(section => section.Rows)
            .Single(candidate => candidate.Name == "Width");

        var written = new List<string?>();
        var context = new RowPropertyContext(row, (target, value) =>
        {
            written.Add(value);
            return document.SetAttributeAsync(target == row ? button : button, "Width", value);
        });

        Assert.Equal("Width", context.Name);
        Assert.False(context.IsSet);

        try
        {
            context.Set("180");

            Assert.Equal(["180"], written);
            Assert.Contains("Width=\"180\"", document.Text);
        }
        finally
        {
            await DesignerFixture.RollbackAsync(document);
        }
    }

    /// <summary>
    /// Правка с другой стороны — из канвы или из текста — должна доходить до
    /// контрола плагина: своих привязок к строке у него нет.
    /// </summary>
    [AvaloniaFact]
    public void A_drawer_is_told_when_the_row_is_refilled()
    {
        var row = new InspectorRow("Margin", InspectorRowKind.Text, typeof(Avalonia.Thickness));
        var context = new RowPropertyContext(row, (_, _) => Task.CompletedTask);
        var told = 0;

        context.Changed += (_, _) => told++;
        row.Fill("4,4,0,0", null, null);

        Assert.Equal(1, told);
        Assert.Equal("4,4,0,0", context.Value);
        Assert.True(context.IsSet);
    }

    [AvaloniaFact]
    public void A_row_with_a_drawer_hides_the_editors_it_would_have_shown()
    {
        var row = new InspectorRow("IsEnabled", InspectorRowKind.Toggle, typeof(bool));

        Assert.True(row.IsToggle);

        row.Drawer = new TextBlock();

        Assert.True(row.IsDrawn);
        Assert.False(row.IsToggle);
    }

    /// <summary>
    /// Два рисовальщика на один тип — это не выбор, а гонка: выиграл бы тот,
    /// кого раньше загрузили, поэтому второй отвергается со словом о том, кто
    /// занял тип.
    /// </summary>
    [Fact]
    public void A_type_that_is_already_taken_is_refused_with_a_word()
    {
        var registry = new PluginContributionRegistry();
        var conflicts = new List<string>();
        var assembly = typeof(FirstDrawer).Assembly;

        registry.Add("first", "Первый", [assembly]);

        var winner = registry.DrawerFor(typeof(int));

        Assert.NotNull(winner);

        registry.Conflict += (_, message) => conflicts.Add(message);
        registry.Add("second", "Второй", [assembly]);

        Assert.NotEmpty(conflicts);
        Assert.All(conflicts, message => Assert.Contains("first", message));
        Assert.All(conflicts, message => Assert.Contains("Второй", message));
        Assert.IsType(winner.GetType(), registry.DrawerFor(typeof(int)));
    }

    /// <summary>
    /// Инспектор, заявленный на базовый тип, должен доставаться и наследнику:
    /// иначе своя кнопка в библиотеке отменяла бы чужую работу.
    /// </summary>
    [Fact]
    public void An_inspector_declared_on_a_base_type_serves_its_heirs()
    {
        var registry = new PluginContributionRegistry();

        registry.Add("editor", "Редактор", [typeof(ButtonInspector).Assembly]);

        Assert.IsType<ButtonInspector>(registry.InspectorFor(typeof(Button))!.Editor);
        Assert.IsType<ButtonInspector>(registry.InspectorFor(typeof(HeirButton))!.Editor);
        Assert.Null(registry.InspectorFor(typeof(TextBlock)));
    }

    [Fact]
    public void A_disabled_plugin_takes_its_contributions_with_it()
    {
        var registry = new PluginContributionRegistry();

        registry.Add("first", "Первый", [typeof(FirstDrawer).Assembly]);
        Assert.NotNull(registry.DrawerFor(typeof(int)));

        registry.Remove("first");
        Assert.Null(registry.DrawerFor(typeof(int)));
    }

}

/// <summary>Рисовальщик-пустышка, занимающий тип.</summary>
[PropertyDrawer(typeof(int))]
public sealed class FirstDrawer : PropertyDrawer
{
    /// <inheritdoc/>
    public override Control Build(IPropertyContext property) => new TextBlock { Text = "первый" };
}

/// <summary>Свой инспектор для кнопок.</summary>
[CustomInspector(typeof(Button))]
public sealed class ButtonInspector : InspectorEditor
{
    /// <inheritdoc/>
    public override Control Build(IInspectorContext element) => new TextBlock { Text = element.TypeName };
}

/// <summary>Наследник кнопки: инспектор базового типа должен доставаться и ему.</summary>
public sealed class HeirButton : Button;
