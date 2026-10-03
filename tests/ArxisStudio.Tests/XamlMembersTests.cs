using ArxisStudio.Markup.Xaml;
using ArxisStudio.Xaml;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Контракт XAML 1.1 у службы: что построил элемент, какие члены документ может на нём написать и чем их
/// править, прочтётся ли значение, какие контролы проекта можно поставить и разрешится ли имя.
/// </summary>
/// <remarks>Ответы — имена и строки: объектов поколения в них нет.</remarks>
[Collection(StudioStateCollection.Name)]
public class XamlMembersTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300" FontSize="20">
          <StackPanel x:Name="Panel">
            <Button x:Name="Go" Content="Пуск" Width="120" />
            <Canvas x:Name="Board" Width="200" Height="100">
              <Border x:Name="Note" Canvas.Left="10" Width="50" Height="20" />
            </Canvas>
            <TextBlock x:Name="Caption" Text="Подпись" />
          </StackPanel>
        </Window>
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static XamlElementPath Panel => XamlElementPath.Parse("/0");

    private static XamlElementPath Go => XamlElementPath.Parse("/0/0");

    private static XamlElementPath Note => XamlElementPath.Parse("/0/1/0");

    private static XamlElementPath Caption => XamlElementPath.Parse("/0/2");

    /// <summary>Элемент описан именами: тип, его пространство, тип родителя и держит ли он детей.</summary>
    [AvaloniaFact]
    public async Task A_view_describes_what_an_element_built_by_names()
    {
        await using var studio = new XamlStudio();
        using var view = await ShowAsync(studio);

        var go = Assert.IsType<XamlElementInfo>(view.DescribeElement(Go));

        Assert.Equal("Button", go.TypeName);
        Assert.Equal("Avalonia.Controls", go.TypeNamespace);
        Assert.Equal("StackPanel", go.ParentTypeName);
        Assert.False(go.HoldsChildren);

        Assert.True(view.DescribeElement(Panel)!.HoldsChildren);
        Assert.Null(view.DescribeElement(XamlElementPath.Root)!.ParentTypeName);
        Assert.Null(view.DescribeElement(XamlElementPath.Parse("/9")));
    }

    /// <summary>
    /// Члены элемента — свои и присоединённые, которые читает родитель: у ребёнка Canvas — его положение, а
    /// не строка сетки; событий и списков среди них нет; у каждого — редактор по типу и нынешнее значение.
    /// </summary>
    [AvaloniaFact]
    public async Task The_members_are_the_elements_own_and_those_its_parent_reads()
    {
        await using var studio = new XamlStudio();
        using var view = await ShowAsync(studio);

        var note = view.GetMembers(Note).ToDictionary(row => row.Name);

        Assert.Equal(XamlValueEditor.Number, note["Width"].Editor);
        Assert.Equal(XamlValueOrigin.Document, note["Width"].Origin);
        Assert.Equal("50", note["Width"].ValueText);

        Assert.True(note["Canvas.Left"].IsAttached);
        Assert.Equal("Canvas", note["Canvas.Left"].OwnerTypeName);
        Assert.Equal("10", note["Canvas.Left"].ValueText);
        Assert.Contains("Canvas.Top", note.Keys);
        Assert.DoesNotContain("Grid.Row", note.Keys);
        Assert.DoesNotContain("DockPanel.Dock", note.Keys);

        Assert.Equal(XamlValueEditor.Choice, note["HorizontalAlignment"].Editor);
        Assert.Contains("Center", note["HorizontalAlignment"].Choices);
        Assert.Equal(XamlValueEditor.Flag, note["IsEnabled"].Editor);
        Assert.Equal(XamlValueEditor.Brush, note["Background"].Editor);

        var go = view.GetMembers(Go).Select(row => row.Name).ToList();

        Assert.DoesNotContain("Click", go);
        Assert.DoesNotContain("Canvas.Left", go);
    }

    /// <summary>
    /// Член стороннего владельца находится по имени; имя, которое ничто не разрешило, и событие — нет.
    /// </summary>
    [AvaloniaFact]
    public async Task A_member_of_a_third_owner_is_found_by_name()
    {
        await using var studio = new XamlStudio();
        using var view = await ShowAsync(studio);

        var tip = Assert.IsType<XamlMemberRow>(view.GetMember(Go, "ToolTip.Tip"));

        Assert.True(tip.IsAttached);
        Assert.Equal("ToolTip", tip.OwnerTypeName);
        Assert.Null(view.GetMember(Go, "Nonsense"));

        // Событие по имени находится, но строкой значения не бывает: обработчик — не значение члена.
        Assert.Null(view.GetMember(Go, "Click"));
    }

    /// <summary>
    /// Значение проверяется так, как его прочтёт загрузка: число — числом, выражение и пустое — проходят,
    /// неразрешённый член проверять не у кого.
    /// </summary>
    [AvaloniaFact]
    public async Task A_value_is_checked_the_way_the_load_reads_it()
    {
        await using var studio = new XamlStudio();
        using var view = await ShowAsync(studio);

        var wide = view.CheckValue(Go, "Width", "широко");

        Assert.False(wide.Succeeded);
        Assert.Contains("широко", wide.Error, StringComparison.Ordinal);

        Assert.True(view.CheckValue(Go, "Width", "150").Succeeded);
        Assert.True(view.CheckValue(Go, "Width", string.Empty).Succeeded);
        Assert.True(view.CheckValue(Go, "Width", "{Binding Size}").Succeeded);
        Assert.True(view.CheckValue(Go, "HorizontalAlignment", "Center").Succeeded);
        Assert.False(view.CheckValue(Go, "HorizontalAlignment", "Middle").Succeeded);
        Assert.True(view.CheckValue(Go, "Nonsense", "что угодно").Succeeded);
    }

    /// <summary>Унаследованное значение говорит, откуда оно: кегль подписи — от окна.</summary>
    [AvaloniaFact]
    public async Task An_inherited_value_says_where_it_comes_from()
    {
        await using var studio = new XamlStudio();
        using var view = await ShowAsync(studio);

        var size = Assert.Single(view.GetMembers(Caption), row => row.Name == "FontSize");

        Assert.Equal(XamlValueOrigin.Inherited, size.Origin);
        Assert.Equal("20", size.ValueText);
    }

    /// <summary>
    /// Значение, которое стиль ставит ресурсом, — от стиля, а не привязка: выражение стоит в стиле, и в
    /// документе привязки нет.
    /// </summary>
    [AvaloniaFact]
    public async Task A_value_a_style_sets_from_a_resource_comes_from_the_style()
    {
        const string Styled = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300">
              <Window.Resources>
                <x:Double x:Key="Tall">44</x:Double>
              </Window.Resources>
              <Window.Styles>
                <Style Selector="Button">
                  <Setter Property="MinHeight" Value="{DynamicResource Tall}" />
                </Style>
              </Window.Styles>
              <StackPanel>
                <Button x:Name="Go" Content="Пуск" />
              </StackPanel>
            </Window>
            """;

        await using var studio = new XamlStudio();
        var path = studio.Write("MainWindow.axaml", Styled);

        await studio.OpenAsync();

        var handle = await studio.Documents.OpenAsync(path, Token);

        using var view = await handle.ShowAsync(null, Token);

        var height = Assert.IsType<XamlMemberRow>(view.GetMember(Go, "MinHeight"));

        Assert.Equal(XamlValueOrigin.Style, height.Origin);
        Assert.Equal("44", height.ValueText);
    }

    /// <summary>
    /// Контролы проекта перечислены именами — ещё не собранный тоже, — а имя, которого нет ни в проекте, ни в
    /// Avalonia, не разрешится.
    /// </summary>
    [AvaloniaFact]
    public async Task Project_controls_are_listed_by_names_and_an_unknown_type_does_not_resolve()
    {
        const string Badge = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="App.Controls.Badge">
              <TextBlock Text="Метка" />
            </UserControl>
            """;

        await using var studio = new XamlStudio();
        var badge = studio.Write("Controls/Badge.axaml", Badge);

        using var view = await ShowAsync(studio);

        var path = view.Document.Path;
        var placeable = await studio.Types.GetPlaceableAsync(path, Token);
        var found = Assert.Single(placeable, control => control.Name == "Badge");

        Assert.Equal("App.Controls.Badge", found.ClassName);
        Assert.Equal(badge, found.Document);
        Assert.False(found.IsBuilt);
        Assert.DoesNotContain(placeable, control => control.Document == path);

        Assert.True(await studio.Types.ResolvesAsync(path, new XamlSnippet(XamlSnippet.AvaloniaNamespace, "Button"), Token));
        Assert.False(await studio.Types.ResolvesAsync(path, new XamlSnippet(XamlSnippet.AvaloniaNamespace, "NoSuchControl"), Token));
    }

    private static async Task<IXamlDesignView> ShowAsync(XamlStudio studio)
    {
        var path = studio.Write("MainWindow.axaml", Form);

        await studio.OpenAsync();

        var handle = await studio.Documents.OpenAsync(path, Token);

        return await handle.ShowAsync(null, Token);
    }
}
