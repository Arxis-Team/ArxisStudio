using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.Inspector.Panel;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Inspection;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Панель привязки инспектора дизайнера (запись 425): источник, путь с подсказками членов, режим — и запись одной правкой,
/// в которой меняется только тронутое.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class DesignerBindingTests
{
    private static XamlElementPath Label => XamlElementPath.Parse("/0/0");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Путь подсказывают члены данных и шаг вглубь; сломанный путь не пишется, найденный пишется одной правкой и
    /// отменяется одним шагом, а режим, сменённый у написанной привязки, не трогает остального.
    /// </summary>
    [AvaloniaFact]
    public async Task A_path_is_suggested_checked_and_written_in_one_step()
    {
        await using var studio = new LiveFormStudio();

        DataProject.Emit(studio.Xaml);

        var document = await studio.OpenAsync("MainWindow.axaml", """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:vm="using:App.ViewModels"
                    x:DataType="vm:MainViewModel"
                    Width="400" Height="300">
              <Window.DataContext>
                <vm:MainViewModel />
              </Window.DataContext>
              <StackPanel>
                <TextBlock Text="Подпись" />
              </StackPanel>
            </Window>
            """);
        var panel = studio.Inspector();
        var before = Text(document);

        document.Select([Label]);
        LiveFormStudio.Frame();

        var editor = Open(panel, "Text");

        // Сегменты источника видны целиком: тесно — они уходят под подпись, а не обрезаются у края панели.
        var last = editor.SourceField.GetVisualDescendants().OfType<AxSegmentItem>().Last();
        var edge = last.TranslatePoint(new Point(last.Bounds.Width, 0), editor.SourceField)!.Value.X;

        Assert.True(edge <= editor.SourceField.Bounds.Width + 0.5, $"сегменты источника обрезаны: край {edge} при ширине {editor.SourceField.Bounds.Width}");

        Assert.Contains(Members(editor), static member => member is { Name: "Customer", HasMembers: true });
        Assert.Contains(Members(editor), static member => member.Name == "Title");

        // Набранное после последней точки сужает список.
        editor.PathField.Text = "Ti";
        LiveFormStudio.Frame();

        Assert.Equal(["Title"], Members(editor).Select(static member => member.Name));

        editor.PathField.Text = "Customer.";
        LiveFormStudio.Frame();

        Assert.Equal(["Name"], Members(editor).Select(static member => member.Name));

        editor.PathField.Text = "Customer.Nmae";
        LiveFormStudio.Frame();

        Assert.False(panel.Expressions!.CanApply, "сломанный путь можно применить");
        Assert.Contains("Nmae", panel.Expressions.Note, StringComparison.Ordinal);
        Assert.False(await panel.Expressions.CommitAsync());

        editor.PathField.Text = "Customer.";
        LiveFormStudio.Frame();
        editor.Members.SelectedIndex = 0;
        editor.Members.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = editor.Members });
        LiveFormStudio.Frame();

        Assert.Equal("Customer.Name", editor.PathField.Text);
        Assert.Equal("MainViewModel → String", editor.State);
        Assert.True(await panel.Expressions.CommitAsync());

        await XamlStudio.UntilAsync(() => Text(document).Contains("Text=\"{Binding Customer.Name}\"", StringComparison.Ordinal), "привязка не записана");
        Assert.False(panel.Expressions.IsOpen);

        await document.Document!.UndoAsync(Token);
        await XamlStudio.UntilAsync(() => Text(document) == before, "привязка отменилась не одним шагом");

        // Написанная привязка открывается панелью: режим меняется, а аргумент, которого панель не трогала, остаётся.
        await document.Document.EditAsync(
            "Подпись",
            editor => editor.SetAttribute(Label.Resolve(editor.Document)!, XamlQualifiedName.Unprefixed("Text"), "{Binding Title,  FallbackValue=нет}"),
            Token);
        LiveFormStudio.Frame();

        var row = Row(panel, "Text");

        await XamlStudio.UntilAsync(() => row.PanelKind == InspectorExpressionKind.Binding, "у привязки нет панели");
        Assert.True(row.OpenExpression());
        LiveFormStudio.Frame();

        var written = Assert.IsType<BindingEditor>(panel.Expressions.Editor);

        Assert.Equal("Title", written.PathField.Text);

        written.ModeField.SelectedIndex = 3;
        LiveFormStudio.Frame();

        Assert.True(await panel.Expressions.CommitAsync());
        await XamlStudio.UntilAsync(
            () => Text(document).Contains("Text=\"{Binding Title,  FallbackValue=нет, Mode=OneTime}\"", StringComparison.Ordinal),
            "режим записан не на месте");
    }

    /// <summary>
    /// Привязка к элементу без имени даёт ему имя той же правкой — одним шагом с привязкой; источник по имени — контрол
    /// формы, а не кисть из ресурсов.
    /// </summary>
    [AvaloniaFact]
    public async Task Binding_to_an_unnamed_element_names_it_in_the_same_step()
    {
        await using var studio = new LiveFormStudio();

        var document = await studio.OpenAsync("MainWindow.axaml", """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300">
              <Window.Resources>
                <SolidColorBrush x:Key="Accent" Color="Red" />
              </Window.Resources>
              <StackPanel>
                <TextBlock Text="Подпись" />
                <Slider />
              </StackPanel>
            </Window>
            """);
        var panel = studio.Inspector();
        var before = Text(document);

        document.Select([Label]);
        LiveFormStudio.Frame();

        var editor = Open(panel, "Text");

        editor.SourceField.SelectedIndex = (int)BindingSourceKind.Element;
        LiveFormStudio.Frame();

        Assert.DoesNotContain(editor.ElementField.Items.OfType<BindingEditor.ElementChoice>(), static choice => choice.Type == "SolidColorBrush");

        var slider = Assert.Single(editor.ElementField.Items.OfType<BindingEditor.ElementChoice>(), static choice => choice.Type == "Slider");

        Assert.Null(slider.Name);
        Assert.Equal("slider", slider.NewName);

        editor.ElementField.SelectedItem = slider;
        LiveFormStudio.Frame();

        Assert.Same(slider, editor.ElementField.SelectedItem);
        Assert.Contains(Members(editor), static member => member.Name == "Value");

        editor.PathField.Text = "Value";
        LiveFormStudio.Frame();

        Assert.Equal("Slider → Double", editor.State);
        Assert.True(await panel.Expressions!.CommitAsync());

        await XamlStudio.UntilAsync(() => Text(document).Contains("Text=\"{Binding #slider.Value}\"", StringComparison.Ordinal), "привязка не записана");

        var text = Text(document);

        Assert.Contains("<Slider x:Name=\"slider\" />", text, StringComparison.Ordinal);

        await document.Document!.UndoAsync(Token);
        await XamlStudio.UntilAsync(() => Text(document) == before, "имя и привязка отменились не одним шагом");
    }

    /// <summary>
    /// Привязка к ползунку, которому та же правка дала имя, сразу показывает на холсте его значение и идёт за ним — у
    /// окна с классом в проекте, где привязки компилируются. Живой показ не знал имени до перезагрузки формы: подпись
    /// показывала прежнее «0», а Avalonia писала «NameScope not found» (запись 426).
    /// </summary>
    [AvaloniaFact]
    public async Task A_binding_to_an_element_named_in_the_same_step_shows_its_value_on_the_canvas()
    {
        await using var studio = new LiveFormStudio();

        studio.Xaml.CompiledBindings = true;
        EmitWindow(studio);

        var document = await studio.OpenAsync("MainWindow.axaml", """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.Views.MainWindow"
                    Width="400" Height="300">
              <StackPanel>
                <TextBlock Text="0" />
                <Slider Minimum="0" Maximum="100" Value="30" />
              </StackPanel>
            </Window>
            """);
        var panel = studio.Inspector();

        document.Select([Label]);
        LiveFormStudio.Frame();

        var editor = Open(panel, "Text");

        editor.SourceField.SelectedIndex = (int)BindingSourceKind.Element;
        LiveFormStudio.Frame();
        editor.ElementField.SelectedItem = Assert.Single(
            editor.ElementField.Items.OfType<BindingEditor.ElementChoice>(), static choice => choice.Type == "Slider");
        LiveFormStudio.Frame();
        editor.PathField.Text = "Value";
        LiveFormStudio.Frame();

        Assert.True(await panel.Expressions!.CommitAsync());
        await XamlStudio.UntilAsync(() => Text(document).Contains("Text=\"{Binding #slider.Value}\"", StringComparison.Ordinal), "привязка не записана");
        Assert.Contains("x:Name=\"slider\"", Text(document), StringComparison.Ordinal);

        await XamlStudio.UntilAsync(() => Shown<TextBlock>(document, Label).Text == "30", "подпись на холсте не показала значение ползунка");

        Shown<Slider>(document, XamlElementPath.Parse("/0/1")).Value = 70;
        LiveFormStudio.Frame();

        Assert.Equal("70", Shown<TextBlock>(document, Label).Text);
    }

    /// <summary>
    /// Проект компилирует привязки, а типа данных нет: панель не пишет сломанное, а предлагает задать тип данных той же
    /// правкой — или писать привязку без проверки типа.
    /// </summary>
    [AvaloniaFact]
    public async Task Without_a_data_type_the_panel_offers_one_or_no_type_check()
    {
        await using var studio = new LiveFormStudio();

        studio.Xaml.CompiledBindings = true;
        DataProject.Emit(studio.Xaml);

        var document = await studio.OpenAsync("MainWindow.axaml", """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:vm="using:App.ViewModels"
                    Width="400" Height="300">
              <Design.DataContext>
                <vm:MainViewModel />
              </Design.DataContext>
              <StackPanel>
                <TextBlock Text="Подпись" />
              </StackPanel>
            </Window>
            """);
        var panel = studio.Inspector();
        var before = Text(document);

        document.Select([Label]);
        LiveFormStudio.Frame();

        // Тип данных предлагают модели проекта — их цель берёт у показа заранее.
        var suggest = Format(studio, "inspector.data.setType", "MainViewModel");

        await XamlStudio.UntilAsync(
            () => panel.Menus!.ForRow(Row(panel, "x:DataType")).OfType<AxMenuItem>().Any(item => Equals(item.Header, suggest)),
            "модели проекта не пришли");

        var editor = Open(panel, "Text");

        editor.PathField.Text = "Title";
        LiveFormStudio.Frame();

        Assert.False(panel.Expressions!.CanApply, "привязку без типа данных можно применить");
        Assert.True(editor.SetTypeButton.IsEffectivelyVisible, "тип данных не предложен");

        editor.SetTypeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        LiveFormStudio.Frame();

        Assert.True(panel.Expressions.CanApply, "заданный тип данных не открыл запись");

        // Тип встаёт той же правкой, и путь проверяется по нему: опечатку вместе с типом не записать — проект не собрался бы.
        editor.PathField.Text = "Titel";
        LiveFormStudio.Frame();

        Assert.False(panel.Expressions.CanApply, "путь, которого у типа нет, записывается вместе с типом");
        Assert.Contains("Titel", panel.Expressions.Note, StringComparison.Ordinal);

        editor.PathField.Text = "Title";
        LiveFormStudio.Frame();

        Assert.True(panel.Expressions.CanApply, "найденный путь с типом не открыл запись");
        Assert.True(await panel.Expressions.CommitAsync());

        await XamlStudio.UntilAsync(() => Text(document).Contains("x:DataType=\"vm:MainViewModel\"", StringComparison.Ordinal), "тип данных не записан");
        Assert.Contains("Text=\"{Binding Title}\"", Text(document), StringComparison.Ordinal);

        await document.Document!.UndoAsync(Token);
        await XamlStudio.UntilAsync(() => Text(document) == before, "тип и привязка отменились не одним шагом");

        var again = Open(panel, "Text");

        again.PathField.Text = "Title";
        LiveFormStudio.Frame();
        again.ReflectionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        LiveFormStudio.Frame();

        Assert.True(panel.Expressions.CanApply, "привязка без проверки типа не открыла запись");
        Assert.True(await panel.Expressions.CommitAsync());

        await XamlStudio.UntilAsync(() => Text(document).Contains("Text=\"{ReflectionBinding Title}\"", StringComparison.Ordinal), "привязка без проверки типа не записана");
        Assert.DoesNotContain("x:DataType", Text(document), StringComparison.Ordinal);
    }

    /// <summary>Открывает панель привязки строки — как пункт «Привязать…» её меню.</summary>
    private static BindingEditor Open(InspectorPanel panel, string id)
    {
        var row = Row(panel, id);

        Assert.True(row.OpenPanel(InspectorExpressionKind.Binding), "панель привязки не открылась");
        LiveFormStudio.Frame();

        return Assert.IsType<BindingEditor>(panel.Expressions!.Editor);
    }

    /// <summary>Кладёт выход проекта: сборку с классом окна <c>App.Views.MainWindow</c>.</summary>
    private static void EmitWindow(LiveFormStudio studio)
    {
        const string Source = """
            using Avalonia.Controls;

            namespace App.Views;

            public class MainWindow : Window
            {
            }
            """;

        var name = "DesignerBinding" + Guid.NewGuid().ToString("N")[..8];
        var output = Path.Combine(studio.Xaml.ProjectFolder, "bin", name + ".dll");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        TestAssembly.EmitFile(output, name, Source);
        studio.Xaml.AssemblyName = name;
        studio.Xaml.Output = ArxisStudio.ProjectSystem.CanonicalPath.Create(output);
    }

    /// <summary>Объект, который элемент формы поставил на холст.</summary>
    private static T Shown<T>(LiveFormDocument document, XamlElementPath path) =>
        Assert.IsType<T>(document.Shown!.ObjectAt(path));

    private static List<XamlBindableMemberInfo> Members(BindingEditor editor) =>
        editor.Members.ItemsSource?.OfType<XamlBindableMemberInfo>().ToList() ?? [];

    private static InspectorRow Row(InspectorPanel panel, string id)
    {
        var cell = Assert.Single(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == id);

        panel.View!.List.ScrollIntoView(cell);
        LiveFormStudio.Frame();

        return cell.Row;
    }

    private static string Format(LiveFormStudio studio, string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, studio.Strings[key], arguments);

    private static string Text(LiveFormDocument document) => document.Document!.Syntax.SourceText.ToString();
}
