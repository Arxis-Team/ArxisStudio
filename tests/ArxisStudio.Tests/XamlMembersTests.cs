using System.Runtime.CompilerServices;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Контракт XAML 1.1 у службы: что построил элемент, какие члены у него есть и откуда их значения, прочтётся
/// ли значение и как показать его, не трогая документ (запись 359).
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

    /// <summary>Контролы проекта: наследник сетки, карточка со своими членами и присоединённым.</summary>
    private const string ProjectSource = """
        using System.ComponentModel;
        using System.ComponentModel.DataAnnotations;
        using Avalonia;
        using Avalonia.Controls;

        namespace App.Controls;

        public class Lattice : Grid
        {
        }

        public class Card : Border
        {
            public static readonly StyledProperty<double> DepthProperty =
                AvaloniaProperty.Register<Card, double>(nameof(Depth));

            public static readonly AttachedProperty<int> ElevationProperty =
                AvaloniaProperty.RegisterAttached<Card, Control, int>("Elevation");

            public Card() => Height = 77;

            [Category("Вид")]
            [Description("Глубина тени")]
            [DisplayName("Глубина")]
            [Range(0, 24)]
            public double Depth
            {
                get => GetValue(DepthProperty);
                set => SetValue(DepthProperty, value);
            }

            [Browsable(false)]
            public string? Hint { get; set; }

            public static int GetElevation(Control control) => control.GetValue(ElevationProperty);

            public static void SetElevation(Control control, int value) => control.SetValue(ElevationProperty, value);
        }
        """;

    private const string ProjectForm = """
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                     xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                     xmlns:c="using:App.Controls"
                     mc:Ignorable="d">
          <c:Lattice>
            <c:Card x:Name="Shadow" Depth="4" d:Width="90" />
          </c:Lattice>
        </UserControl>
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static XamlElementPath Panel => XamlElementPath.Parse("/0");

    private static XamlElementPath Go => XamlElementPath.Parse("/0/0");

    private static XamlElementPath Note => XamlElementPath.Parse("/0/1/0");

    private static XamlElementPath Caption => XamlElementPath.Parse("/0/2");

    private static XamlElementPath Shadow => XamlElementPath.Parse("/0/0");

    /// <summary>Элемент описан именами: тип и его базы, тип родителя, держит ли он детей, куда ложится содержимое.</summary>
    [AvaloniaFact]
    public async Task A_view_describes_what_an_element_built_by_names()
    {
        await using var studio = new XamlStudio();
        using var view = await ShowAsync(studio, Form);

        var go = Assert.IsType<XamlElementInfo>(view.DescribeElement(Go));

        Assert.Equal("Avalonia.Controls.Button", go.TypeFullName);
        Assert.Equal("Button", go.TypeName);
        Assert.Equal("Avalonia.Controls.ContentControl", go.BaseTypes[0]);
        Assert.True(go.Is("Avalonia.Controls.Control"));
        Assert.Equal("Avalonia.Controls.StackPanel", go.ParentTypeFullName);
        Assert.Contains("Avalonia.Controls.Panel", go.ParentBaseTypes);
        Assert.False(go.HoldsChildren);
        Assert.Equal("Content", go.ContentMember);

        var panel = view.DescribeElement(Panel)!;

        Assert.True(panel.HoldsChildren);
        Assert.Equal("Children", panel.ContentMember);
        Assert.Null(view.DescribeElement(XamlElementPath.Root)!.ParentTypeFullName);
        Assert.Empty(view.DescribeElement(XamlElementPath.Root)!.ParentBaseTypes);
        Assert.Null(view.DescribeElement(XamlElementPath.Parse("/9")));
    }

    /// <summary>
    /// Члены элемента — свои и присоединённые, которые читает родитель: у ребёнка Canvas — его положение, а
    /// не строка сетки; событий и списков среди них нет; у каждого — тип значения и нынешнее значение.
    /// </summary>
    [AvaloniaFact]
    public async Task The_members_are_the_elements_own_and_those_its_parent_reads()
    {
        await using var studio = new XamlStudio();
        using var view = await ShowAsync(studio, Form);

        var note = view.GetMembers(Note).ToDictionary(member => member.Name);

        Assert.Equal("System.Double", note["Width"].ValueType.FullName);
        Assert.Equal(XamlPropertyKind.Styled, note["Width"].Kind);
        Assert.Equal(XamlValueOrigin.Document, note["Width"].Value.Origin);
        Assert.Equal("50", note["Width"].Value.Text);
        Assert.Equal(50d, note["Width"].Value.Shared);
        Assert.True(note["Width"].CanWrite);
        Assert.True(note["Width"].AcceptsText);

        var left = note["Canvas.Left"];

        Assert.Equal(XamlPropertyKind.Attached, left.Kind);
        Assert.True(left.IsAttached);
        Assert.True(left.ParentReads);
        Assert.Equal("Avalonia.Controls.Canvas", left.OwnerTypeFullName);
        Assert.Equal("https://github.com/avaloniaui", left.OwnerXmlNamespace);
        Assert.Equal("10", left.Value.Text);
        Assert.Contains("Canvas.Top", note.Keys);
        Assert.DoesNotContain("Grid.Row", note.Keys);
        Assert.DoesNotContain("DockPanel.Dock", note.Keys);

        Assert.Contains("Center", note["HorizontalAlignment"].ValueType.EnumNames);
        Assert.Equal("System.Boolean", note["IsEnabled"].ValueType.FullName);
        Assert.True(note["Background"].ValueType.Is("Avalonia.Media.IBrush"));
        Assert.Equal(XamlValueOrigin.Default, note["Opacity"].Value.Origin);

        var go = view.GetMembers(Go).ToDictionary(member => member.Name);

        Assert.DoesNotContain("Click", go.Keys);
        Assert.DoesNotContain("Canvas.Left", go.Keys);
        Assert.True(go["Content"].IsContent);
        Assert.Equal("Пуск", go["Content"].Value.Text);
    }

    /// <summary>
    /// Член стороннего владельца находится по имени; имя, которое ничто не разрешило, и событие — нет.
    /// </summary>
    [AvaloniaFact]
    public async Task A_member_of_a_third_owner_is_found_by_name()
    {
        await using var studio = new XamlStudio();
        using var view = await ShowAsync(studio, Form);

        var tip = Assert.IsType<XamlMemberInfo>(view.GetMember(Go, "ToolTip.Tip"));

        Assert.True(tip.IsAttached);
        Assert.False(tip.ParentReads);
        Assert.Equal("Avalonia.Controls.ToolTip", tip.OwnerTypeFullName);
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
        using var view = await ShowAsync(studio, Form);

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

    /// <summary>Унаследованное значение говорит, откуда оно и от кого: кегль подписи — от окна.</summary>
    [AvaloniaFact]
    public async Task An_inherited_value_says_where_it_comes_from()
    {
        await using var studio = new XamlStudio();
        using var view = await ShowAsync(studio, Form);

        var size = Assert.Single(view.GetMembers(Caption), member => member.Name == "FontSize");

        Assert.Equal(XamlValueOrigin.Inherited, size.Value.Origin);
        Assert.Equal("20", size.Value.Text);
        Assert.Equal("Window", size.Value.OriginDetail);
    }

    /// <summary>
    /// Значение, которое стиль ставит ресурсом, — от стиля, а не привязка, и источник назван селектором: выражение
    /// стоит в стиле, и в документе привязки нет.
    /// </summary>
    [AvaloniaFact]
    public async Task A_value_a_style_sets_from_a_resource_comes_from_the_style_named_by_its_selector()
    {
        await using var studio = new XamlStudio();
        using var view = await ShowAsync(studio, Styled);

        var height = Assert.IsType<XamlMemberInfo>(view.GetMember(Go, "MinHeight"));

        Assert.Equal(XamlValueOrigin.Style, height.Value.Origin);
        Assert.Equal("44", height.Value.Text);
        Assert.Equal("Button", height.Value.OriginDetail);

        var faint = Assert.IsType<XamlMemberInfo>(view.GetMember(Go, "Opacity"));

        Assert.Equal(XamlValueOrigin.Expression, faint.Value.Origin);
        Assert.Equal("{DynamicResource Faint}", faint.Value.OriginDetail);
    }

    /// <summary>
    /// У контрола проекта: ребёнок наследника сетки читает <c>Grid.Row</c>, присоединённое владельца из проекта
    /// называет своё пространство, значение из конструктора — от кода, из <c>d:</c> — для дизайна, а атрибуты
    /// .NET доходят до члена.
    /// </summary>
    [AvaloniaFact]
    public async Task A_project_controls_members_say_who_reads_them_where_their_values_come_from_and_what_their_author_wrote()
    {
        await using var studio = new XamlStudio();

        Emit(studio);

        using var view = await ShowAsync(studio, ProjectForm);

        var shadow = view.GetMembers(Shadow).ToDictionary(member => member.Name);

        Assert.True(shadow["Grid.Row"].ParentReads, "ребёнок наследника сетки не читает Grid.Row");
        Assert.Equal(XamlValueOrigin.Code, shadow["Height"].Value.Origin);
        Assert.Equal("77", shadow["Height"].Value.Text);
        Assert.Equal(XamlValueOrigin.Design, shadow["Width"].Value.Origin);
        Assert.Equal(90d, shadow["Width"].Value.Shared);

        var depth = shadow["Depth"];

        Assert.Equal("Вид", depth.Attributes.Category);
        Assert.Equal("Глубина тени", depth.Attributes.Description);
        Assert.Equal("Глубина", depth.Attributes.DisplayName);
        Assert.Equal(0d, depth.Attributes.RangeMinimum);
        Assert.Equal(24d, depth.Attributes.RangeMaximum);
        Assert.Equal(XamlValueOrigin.Document, depth.Value.Origin);

        Assert.False(shadow["Hint"].Attributes.Browsable);
        Assert.Equal(XamlPropertyKind.Clr, shadow["Hint"].Kind);

        var elevation = Assert.IsType<XamlMemberInfo>(view.GetMember(Shadow, "Card.Elevation"));

        Assert.Equal("App.Controls.Card", elevation.OwnerTypeFullName);
        Assert.Equal("using:App.Controls", elevation.OwnerXmlNamespace);
    }

    /// <summary>
    /// Предпросмотр показывает значение на объекте и не трогает текст; отпущенный — возвращает и написанное
    /// документом, и стиль, и привязку.
    /// </summary>
    [AvaloniaFact]
    public async Task A_preview_shows_on_the_object_and_its_end_brings_back_the_local_value_the_style_and_the_binding()
    {
        await using var studio = new XamlStudio();
        using var view = await ShowAsync(studio, Styled);

        var text = view.Document.Syntax.SourceText.ToString();
        var go = (Button)view.ObjectAt(Go)!;

        using (var width = view.Preview(Go, "Width")!)
        using (var height = view.Preview(Go, "MinHeight")!)
        using (var faint = view.Preview(Go, "Opacity")!)
        {
            Assert.True(width.Show("150").Succeeded);
            Assert.True(height.Show("60").Succeeded);
            Assert.True(faint.Show("0.9").Succeeded);
            Assert.False(width.Show("широко").Succeeded);

            Assert.Equal(150, go.Width);
            Assert.Equal(60, go.MinHeight);
            Assert.Equal(0.9, go.Opacity);
            Assert.Equal(text, view.Document.Syntax.SourceText.ToString());
        }

        Assert.Equal(120, go.Width);
        Assert.Equal(44, go.MinHeight);
        Assert.Equal(0.5, go.Opacity);
        Assert.NotNull(BindingOperations.GetBindingExpressionBase(go, Visual.OpacityProperty));
        Assert.Equal(text, view.Document.Syntax.SourceText.ToString());
    }

    /// <summary>Правка текста кончает предпросмотр: записанное значение пришло, и показывать больше нечего.</summary>
    [AvaloniaFact]
    public async Task A_preview_ends_when_the_text_moves()
    {
        await using var studio = new XamlStudio();
        using var view = await ShowAsync(studio, Form);

        var go = (Button)view.ObjectAt(Go)!;
        using var width = view.Preview(Go, "Width")!;

        width.Show("150");

        await view.Document.EditAsync("ширина", editor =>
            editor.SetAttribute(Go.Resolve(editor.Document)!, XamlQualifiedName.Parse("Width"), "200"), Token);

        go = (Button)view.ObjectAt(Go)!;

        Assert.Equal(200, go.Width);

        width.Show("170");

        Assert.Equal(200, go.Width);
    }

    /// <summary>Пока предпросмотр жив, поколение не заменяют: недописанное значение держит замену.</summary>
    [AvaloniaFact]
    public async Task A_preview_holds_the_swap_until_it_ends()
    {
        await using var studio = new XamlStudio();
        var output = Emit(studio);

        using var view = await ShowAsync(studio, ProjectForm);
        var preview = view.Preview(Shadow, "Depth")!;

        studio.Provider.Executed = request =>
        {
            if (request.Kind == ProjectOperationKind.Build)
                File.SetLastWriteTimeUtc(output.Value, File.GetLastWriteTimeUtc(output.Value).AddSeconds(2));

            return ProjectOperationResult.Succeeded();
        };

        Assert.True((await studio.Design.RebuildAsync(Token)).TypesChanged);

        await XamlStudio.UntilAsync(() => studio.Design.State == XamlDesignState.SwapPending, "типы не встали в ожидание замены");

        Assert.Contains(studio.Service.Words.Preview, studio.Design.StateReason, StringComparison.Ordinal);

        preview.Dispose();

        await XamlStudio.UntilAsync(() => studio.Design.State == XamlDesignState.Live, "замена не пошла после предпросмотра");
    }

    /// <summary>
    /// У прямого свойства и свойства CLR предпросмотра нет: поставленное значение не снять без следа.
    /// </summary>
    [AvaloniaFact]
    public async Task A_preview_of_a_direct_or_clr_member_is_refused()
    {
        await using var studio = new XamlStudio();

        Emit(studio);

        using var view = await ShowAsync(studio, ProjectForm);

        Assert.Null(view.Preview(Shadow, "Hint"));
        Assert.Null(view.Preview(Shadow, "Bounds"));
        Assert.Null(view.Preview(Shadow, "Nonsense"));
        Assert.NotNull(view.Preview(Shadow, "Depth"));
    }

    /// <summary>
    /// Чтение членов не держит заменённое поколение: сведения о них — имена, а кэши — слабые.
    /// </summary>
    [AvaloniaFact]
    public async Task Reading_members_does_not_keep_a_replaced_generation()
    {
        await using var studio = new XamlStudio();

        Emit(studio);

        using var view = await ShowAsync(studio, ProjectForm);

        Read(view);

        var report = await studio.Session.Host.SwapAsync("проверка выгрузки", Token);

        Assert.True(report.Reclaimed, report.ToString());
    }

    private const string Styled = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
          <Window.Resources>
            <x:Double x:Key="Tall">44</x:Double>
            <x:Double x:Key="Faint">0.5</x:Double>
          </Window.Resources>
          <Window.Styles>
            <Style Selector="Button">
              <Setter Property="MinHeight" Value="{DynamicResource Tall}" />
            </Style>
          </Window.Styles>
          <StackPanel>
            <Button x:Name="Go" Content="Пуск" Width="120" Opacity="{DynamicResource Faint}" />
          </StackPanel>
        </Window>
        """;

    /// <summary>Читает члены контрола проекта — отдельным методом, чтобы ответы не остались в кадре теста.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Read(IXamlDesignView view)
    {
        Assert.NotEmpty(view.GetMembers(Shadow));
        Assert.NotNull(view.GetMember(Shadow, "Card.Elevation"));
        Assert.NotNull(view.DescribeElement(Shadow));
    }

    /// <summary>Кладёт выход проекта: сборку с контролами — поколению собирать нечего.</summary>
    private static CanonicalPath Emit(XamlStudio studio)
    {
        var name = "XamlMembers" + Guid.NewGuid().ToString("N")[..8];
        var output = Path.Combine(studio.ProjectFolder, "bin", name + ".dll");

        // [Range] контролов проекта — из DataAnnotations, а проба ссылается на сборки, уже загруженные в процесс:
        // эту грузит первое касание, и без него тест, пошедший раньше других, не собирал проект.
        _ = typeof(System.ComponentModel.DataAnnotations.RangeAttribute).Assembly;

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        TestAssembly.EmitFile(output, name, ProjectSource);

        studio.AssemblyName = name;
        studio.Output = CanonicalPath.Create(output);

        return studio.Output.Value;
    }

    private static async Task<IXamlDesignView> ShowAsync(XamlStudio studio, string form)
    {
        var path = studio.Write("MainWindow.axaml", form);

        await studio.OpenAsync();

        var handle = await studio.Documents.OpenAsync(path, Token);

        return await handle.ShowAsync(null, Token);
    }
}
