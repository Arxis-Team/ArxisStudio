using ArxisStudio.Markup.Xaml;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Палитра контролов: что она предлагает документу и какую разметку из этого
/// собирает.
/// </summary>
public class ToolboxTests
{
    private const string Plain = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <StackPanel/>
        </Window>
        """;

    private const string WithControls = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:ax="using:ArxisStudio.Controls">
          <StackPanel/>
        </Window>
        """;

    /// <summary>
    /// Контрол из библиотеки, которую документ не объявил, при вставке дал бы
    /// неразрешимый тип — предлагать его нельзя.
    /// </summary>
    [Fact]
    public void A_library_the_document_did_not_declare_is_not_offered()
    {
        var groups = ToolboxCatalog.For(Root(Plain));

        Assert.NotEmpty(groups);
        Assert.DoesNotContain(groups, group => group.TitleKey == "toolbox.group.studio");
        Assert.Contains(groups.SelectMany(group => group.Items), item => item.TypeName == "Button");
    }

    [Fact]
    public void A_declared_library_brings_its_own_section()
    {
        var groups = ToolboxCatalog.For(Root(WithControls));

        var studio = Assert.Single(groups, group => group.TitleKey == "toolbox.group.studio");
        Assert.Contains(studio.Items, item => item.TypeName == "AxButton");
    }

    [Fact]
    public void Search_narrows_the_sections_and_drops_the_empty_ones()
    {
        var groups = ToolboxCatalog.For(Root(Plain), "grid");

        var items = groups.SelectMany(group => group.Items).ToList();

        Assert.All(items, item => Assert.Contains("grid", item.TypeName, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, item => item.TypeName == "Grid");
        Assert.All(groups, group => Assert.NotEmpty(group.Items));
    }

    [Fact]
    public void Markup_carries_the_prefix_the_document_uses()
    {
        var root = Root(WithControls);
        var item = new ToolboxItem("AxButton", ToolboxCatalog.ControlsNamespace, "Content=\"Кнопка\"");

        Assert.Equal("<ax:AxButton Content=\"Кнопка\"/>", ToolboxCatalog.Markup(item, root));
    }

    /// <summary>
    /// Пустой контейнер без размера занимает ноль пикселей, и вставка выглядела
    /// бы как ничего не произошло.
    /// </summary>
    [Fact]
    public void A_container_is_inserted_with_a_size()
    {
        var item = new ToolboxItem("Grid", ToolboxCatalog.AvaloniaNamespace, NeedsSize: true);
        var markup = ToolboxCatalog.Markup(item, Root(Plain));

        Assert.Equal("<Grid Width=\"120\" Height=\"80\"/>", markup);
    }

    [Fact]
    public void Placement_is_appended_to_what_the_item_already_carries()
    {
        var item = new ToolboxItem("Button", ToolboxCatalog.AvaloniaNamespace, "Content=\"Button\"");
        var markup = ToolboxCatalog.Markup(item, Root(Plain), "Canvas.Left=\"40\" Canvas.Top=\"20\"");

        Assert.Equal("<Button Content=\"Button\" Canvas.Left=\"40\" Canvas.Top=\"20\"/>", markup);
    }

    [Fact]
    public void An_undeclared_namespace_yields_no_markup()
    {
        var item = new ToolboxItem("AxButton", ToolboxCatalog.ControlsNamespace);

        Assert.Null(ToolboxCatalog.Markup(item, Root(Plain)));
    }

    private static XamlElement Root(string markup) => XamlDocument.Parse(markup).Root!;
}
