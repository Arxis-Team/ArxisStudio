using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Dragging;
using ArxisStudio.Inspector;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.Inspector.Panel;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Раздел «Данные» инспектора дизайнера (запись 423): откуда данные элемента, класс модели — выбором и броском из окна
/// проекта, сама форма своими данными, тип данных и привязки, которым он нужен.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class DesignerDataTests
{
    /// <summary>Форма без данных.</summary>
    private const string Plain = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
          <StackPanel>
            <TextBlock x:Name="Caption" Text="Подпись" />
          </StackPanel>
        </Window>
        """;

    private static XamlElementPath Label => XamlElementPath.Parse("/0/0");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Раздел данных — первый; корень, создающий модель, показывает её класс, а подпись, наследующая данные, — от кого
    /// и какого типа они пришли.
    /// </summary>
    [AvaloniaFact]
    public async Task The_data_section_comes_first_and_says_where_the_data_comes_from()
    {
        await using var studio = new LiveFormStudio();

        DataProject.Emit(studio.Xaml);

        var document = await studio.OpenAsync("MainWindow.axaml", """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:vm="using:App.ViewModels"
                    x:CompileBindings="false"
                    Width="400" Height="300">
              <Window.DataContext>
                <vm:MainViewModel />
              </Window.DataContext>
              <StackPanel DataContext="{Binding Customer}">
                <TextBlock Text="{Binding Name}" />
              </StackPanel>
            </Window>
            """);
        var panel = studio.Inspector();

        LiveFormStudio.Frame();

        Assert.Equal("data", panel.List!.Items.OfType<GroupCell>().First().CategoryId);

        var source = Row(panel, "DataContext");

        Assert.Equal(studio.Strings["inspector.data.source"], source.Property.Label);
        Assert.False(source.Property.IsReadOnly, "длинная запись заперла источник данных");
        Assert.Equal("MainViewModel", Field(source).Text);

        // Написанное «false» — тот же сегмент «Нет», и написанная компиляция привязок не прячется в дополнительное.
        var compile = Row(panel, "x:CompileBindings");

        Assert.Equal("False", compile.Value?.Text);
        Assert.False(compile.Property.IsAdvanced, "написанная компиляция привязок спрятана в дополнительное");

        document.Select([Label]);
        LiveFormStudio.Frame();

        var inherited = Format(studio, "inspector.data.inherited", "Customer", "StackPanel");

        // Поле берётся один раз: ответ службы приходит позже, и строка обязана переспросить его сама.
        var field = Field(Row(panel, "DataContext"));

        await XamlStudio.UntilAsync(() => field.PlaceholderText == inherited, "подпись не сказала, откуда её данные");

        Assert.Null(field.Text);
    }

    /// <summary>
    /// Файл модели, брошенный на источник данных, пишется одной правкой: экземпляр элементом свойства, тип данных и
    /// пространство на корне — и отменяется одним шагом.
    /// </summary>
    [AvaloniaFact]
    public async Task A_model_file_dropped_on_the_data_source_writes_the_instance_and_its_data_type_in_one_step()
    {
        await using var studio = new LiveFormStudio();

        DataProject.Emit(studio.Xaml);

        var model = studio.Xaml.Write("ViewModels/MainViewModel.cs", DataProject.Model);
        var document = await studio.OpenAsync("MainWindow.axaml", Plain);
        var before = Text(document);
        var panel = studio.Inspector();
        var (drags, source, over, field) = Aim(studio, panel, "DataContext");

        using var drag = drags.Begin(source, StudioDragData.FromFiles([model.Value]), DragDropEffects.Copy | DragDropEffects.Link, new StudioDragVisual("MainViewModel.cs"));

        await XamlStudio.UntilAsync(() => drag.Over(over, KeyModifiers.None) == DragDropEffects.Link, "модель не принята");

        Assert.Equal(Format(studio, "inspector.data.drop", "MainViewModel"), drag.Hint);
        Assert.True(field.IsDropTarget, "поле не подсветилось целью");
        Assert.Equal(DragDropEffects.Link, drag.Drop());

        await XamlStudio.UntilAsync(() => Text(document).Contains("<vm:MainViewModel />", StringComparison.Ordinal), "модель не записана");

        var text = Text(document);

        Assert.Contains("xmlns:vm=\"using:App.ViewModels\"", text, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"vm:MainViewModel\"", text, StringComparison.Ordinal);
        Assert.Contains("<Window.DataContext>", text, StringComparison.Ordinal);

        await XamlStudio.UntilAsync(() => Field(Row(panel, "DataContext")).Text == "MainViewModel", "поле не показало класс модели");
        await XamlStudio.UntilAsync(() => Field(Row(panel, "x:DataType")).Text == "MainViewModel", "тип данных не показан");

        await document.Document!.UndoAsync(Token);

        await XamlStudio.UntilAsync(() => Text(document) == before, "правка модели отменилась не одним шагом");
    }

    /// <summary>Класс формы — контрол, а не данные: бросок его файла отклонён, и подсказка говорит почему.</summary>
    [AvaloniaFact]
    public async Task A_control_class_dropped_on_the_data_source_is_refused_with_the_reason()
    {
        await using var studio = new LiveFormStudio();

        DataProject.Emit(studio.Xaml);

        var view = studio.Xaml.Write("Views/SelfView.cs", DataProject.View);
        var document = await studio.OpenAsync("MainWindow.axaml", Plain);
        var before = Text(document);
        var panel = studio.Inspector();
        var (drags, source, over, field) = Aim(studio, panel, "DataContext");
        var refusal = Format(studio, "inspector.data.isControl", "SelfView");

        using (var drag = drags.Begin(source, StudioDragData.FromFiles([view.Value]), DragDropEffects.Copy | DragDropEffects.Link, new StudioDragVisual("SelfView.cs")))
        {
            await XamlStudio.UntilAsync(
                () => drag.Over(over, KeyModifiers.None) == DragDropEffects.None && drag.Hint == refusal, "контрол не отклонён с причиной");

            Assert.False(field.IsDropTarget, "отказ подсветил поле целью");
            drag.Drop();
        }

        LiveFormStudio.Frame();

        Assert.Equal(before, Text(document));
    }

    /// <summary>
    /// Первым в выборе стоит сама форма: она становится своими данными — <c>{Binding $self}</c>, — а её класс — типом
    /// данных той же правкой.
    /// </summary>
    [AvaloniaFact]
    public async Task The_form_itself_becomes_its_data_with_its_class_as_the_data_type()
    {
        await using var studio = new LiveFormStudio();

        DataProject.Emit(studio.Xaml);

        var document = await studio.OpenAsync("SelfView.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="App.Views.SelfView">
              <TextBlock Text="{Binding Greeting}" />
            </UserControl>
            """);
        var panel = studio.Inspector();

        LiveFormStudio.Frame();

        var field = Field(Row(panel, "DataContext"));

        field.Focus();
        LiveFormStudio.Press(field, Key.Enter);
        LiveFormStudio.Frame();

        var list = Picker(studio);
        var self = Format(studio, "inspector.data.self", "SelfView");

        await XamlStudio.UntilAsync(() => list.Items.OfType<InspectorChoice>().FirstOrDefault()?.Title == self, "сама форма не первая в выборе");

        Assert.Contains(list.Items.OfType<InspectorChoice>(), static choice => choice.Title == "MainViewModel");
        Assert.DoesNotContain(list.Items.OfType<InspectorChoice>(), static choice => choice.Title is "Shout" or "SelfView");

        list.SelectedIndex = 0;
        LiveFormStudio.Press(list, Key.Enter);

        await XamlStudio.UntilAsync(() => Text(document).Contains("DataContext=\"{Binding $self}\"", StringComparison.Ordinal), "сама форма не записана");

        var text = Text(document);

        Assert.Contains("x:DataType=\"views:SelfView\"", text, StringComparison.Ordinal);
        Assert.Contains("xmlns:views=\"using:App.Views\"", text, StringComparison.Ordinal);

        await XamlStudio.UntilAsync(() => Field(Row(panel, "DataContext")).Text == self, "поле не назвало саму форму");
    }

    /// <summary>
    /// Крестик снимает длинную запись источника, а тип данных оставляет: данные может поставить и код, а пути привязок
    /// всё так же проверяет сборка.
    /// </summary>
    [AvaloniaFact]
    public async Task Clearing_the_data_source_takes_the_long_form_out_and_keeps_the_data_type()
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
              <TextBlock Text="{Binding Title}" />
            </Window>
            """);
        var panel = studio.Inspector();

        LiveFormStudio.Frame();

        Field(Row(panel, "DataContext")).RaiseEvent(new RoutedEventArgs(AxObjectField.ClearedEvent));

        await XamlStudio.UntilAsync(() => !Text(document).Contains("Window.DataContext", StringComparison.Ordinal), "длинная запись не снята");

        Assert.Contains("x:DataType=\"vm:MainViewModel\"", Text(document), StringComparison.Ordinal);
    }

    /// <summary>
    /// Модель вместо выражения: атрибут уходит, встаёт длинная запись с типом данных — и атрибут, стоявший последним,
    /// с правкой типа данных не сталкивается.
    /// </summary>
    [AvaloniaFact]
    public async Task A_model_replaces_an_expression_written_last()
    {
        await using var studio = new LiveFormStudio();

        DataProject.Emit(studio.Xaml);

        var document = await studio.OpenAsync("MainWindow.axaml", """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300"
                    DataContext="{Binding Source={x:Null}}">
              <TextBlock />
            </Window>
            """);
        var panel = studio.Inspector();

        LiveFormStudio.Frame();

        var written = new DataClass("using:App.ViewModels", "MainViewModel", "vm").Text;

        Assert.True(await panel.List!.WriteAsync([new InspectorWrite("DataContext", written)], null), "модель не записана");

        await XamlStudio.UntilAsync(() => Text(document).Contains("<vm:MainViewModel />", StringComparison.Ordinal), "модель не встала");

        var text = Text(document);

        Assert.DoesNotContain("DataContext=\"{Binding", text, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"vm:MainViewModel\"", text, StringComparison.Ordinal);
        Assert.Contains("<Window.DataContext>", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Тип данных, написанный под своей приставкой пространства XAML, переписывается на месте, а не встаёт вторым
    /// атрибутом, и снимается.
    /// </summary>
    [AvaloniaFact]
    public async Task A_data_type_under_its_own_prefix_is_rewritten_and_cleared_in_place()
    {
        await using var studio = new LiveFormStudio();

        DataProject.Emit(studio.Xaml);

        var document = await studio.OpenAsync("MainWindow.axaml", """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:xx="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:vm="using:App.ViewModels"
                    xx:DataType="vm:Customer"
                    Width="400" Height="300">
              <TextBlock />
            </Window>
            """);
        var panel = studio.Inspector();

        LiveFormStudio.Frame();

        Assert.Equal("Customer", Field(Row(panel, "x:DataType")).Text);

        var written = new DataClass("using:App.ViewModels", "MainViewModel", "vm").Text;

        Assert.True(await panel.List!.WriteAsync([new InspectorWrite("x:DataType", written)], null), "тип данных не записан");

        await XamlStudio.UntilAsync(() => Text(document).Contains("xx:DataType=\"vm:MainViewModel\"", StringComparison.Ordinal), "тип данных не переписан");

        Assert.Single(ElementAt(document, XamlElementPath.Root).Directives, static directive => directive.Name.LocalName == "DataType");

        // Снять — тоже по пространству: директива под чужой приставкой снимается, а не остаётся.
        Assert.True(await panel.List.WriteAsync([new InspectorWrite("x:DataType", null)], null), "тип данных не снят");

        await XamlStudio.UntilAsync(() => !Text(document).Contains("DataType", StringComparison.Ordinal), "директива под своей приставкой не снята");
    }

    /// <summary>
    /// Проект компилирует привязки, а типа данных нет: строка говорит, что проект не соберётся, и меню предлагает тип
    /// данных, которые форма показывает, — записанный, он снимает заметку. Данные для дизайна стоят своей строкой.
    /// </summary>
    [AvaloniaFact]
    public async Task Compiled_bindings_without_a_data_type_say_so_and_the_menu_offers_the_type()
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
              <TextBlock Text="{Binding Title}" />
            </Window>
            """);
        var panel = studio.Inspector();
        var needed = studio.Strings["inspector.data.needsType"];

        LiveFormStudio.Frame();

        Assert.Equal("MainViewModel", Field(Row(panel, "Design.DataContext")).Text);
        Assert.DoesNotContain(panel.List!.Items.OfType<PropertyCell>(), static cell => cell.Row.Property.Id == "d:DataContext");

        await XamlStudio.UntilAsync(() => Row(panel, "x:DataType").Value?.Error == needed, "строка не сказала, что без типа данных проект не соберётся");

        var offer = Format(studio, "inspector.data.setType", "MainViewModel");

        await XamlStudio.UntilAsync(
            () => panel.Menus!.ForRow(Row(panel, "x:DataType")).OfType<AxMenuItem>().Any(item => Equals(item.Header, offer)), "меню не предложило тип данных");

        var item = Assert.Single(panel.Menus!.ForRow(Row(panel, "x:DataType")).OfType<AxMenuItem>(), item => Equals(item.Header, offer));

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        await XamlStudio.UntilAsync(() => Text(document).Contains("x:DataType=\"vm:MainViewModel\"", StringComparison.Ordinal), "тип данных не записан");
        await XamlStudio.UntilAsync(() => Row(panel, "x:DataType").Value?.Error is null, "заметка осталась после записи типа данных");
    }

    /// <summary>Поле строки, тяга студии и точка над ним — так бросают файл из окна проекта.</summary>
    private static (StudioDrags Drags, Control Source, Point Over, AxObjectField Field) Aim(LiveFormStudio studio, InspectorPanel panel, string id)
    {
        LiveFormStudio.Frame();

        var field = Field(Row(panel, id));
        var source = panel.View!;
        var over = field.TranslatePoint(new Point(field.Bounds.Width / 2, field.Bounds.Height / 2), source)!.Value;

        LiveFormStudio.Frame();

        return (new StudioDrags(() => [studio.Window], studio.Xaml.Log), source, over, field);
    }

    private static InspectorRow Row(InspectorPanel panel, string id)
    {
        var cell = Assert.Single(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == id);

        panel.View!.List.ScrollIntoView(cell);
        LiveFormStudio.Frame();

        return cell.Row;
    }

    private static AxObjectField Field(InspectorRow row) => Assert.Single(row.GetVisualDescendants().OfType<AxObjectField>());

    private static AxListBox Picker(LiveFormStudio studio) =>
        Assert.Single(studio.Window.GetVisualDescendants().OfType<AxListBox>(), list => list.ItemsSource is IEnumerable<InspectorChoice>);

    private static string Format(LiveFormStudio studio, string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, studio.Strings[key], arguments);

    private static string Text(LiveFormDocument document) => document.Document!.Syntax.SourceText.ToString();

    private static XamlElement ElementAt(LiveFormDocument document, XamlElementPath path) =>
        path.Resolve(document.Document!.Syntax) ?? throw new InvalidOperationException("элемента нет в тексте");
}
