using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.Inspector.Panel;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Inspection;
using ArxisStudio.Xaml;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Панель ресурса инспектора дизайнера (запись 427): ресурсы формы и её приложения с образцами, отбор и поиск, вид ссылки
/// там, где он что-то найдёт, — и запись одной правкой.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class DesignerResourceTests
{
    private const string Application = """
        <Application xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Application.Resources>
            <ResourceDictionary>
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <SolidColorBrush x:Key="Surface" Color="White" />
                </ResourceDictionary>
                <ResourceDictionary x:Key="Dark">
                  <SolidColorBrush x:Key="Surface" Color="Black" />
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
              <SolidColorBrush x:Key="AppBrush" Color="#FF336699" />
              <x:Double x:Key="Gap">8</x:Double>
            </ResourceDictionary>
          </Application.Resources>
        </Application>
        """;

    private const string Form = """
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <UserControl.Resources>
            <SolidColorBrush x:Key="FormBrush" Color="Red" />
            <Thickness x:Key="Pad">4</Thickness>
          </UserControl.Resources>
          <StackPanel>
            <Border Width="{StaticResource Gap}" />
          </StackPanel>
        </UserControl>
        """;

    private static XamlElementPath Border => XamlElementPath.Parse("/0/0");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Ресурсы формы и приложения — подходящие свойству, поиск сужает их; найденная кисть пишется динамической ссылкой одной
    /// правкой и отменяется одним шагом.
    /// </summary>
    [AvaloniaFact]
    public async Task A_resource_is_chosen_from_the_form_and_its_application_and_written_in_one_step()
    {
        await using var studio = new LiveFormStudio();

        studio.Xaml.Write("App.axaml", Application);

        var document = await Opened(studio);
        var panel = studio.Inspector();
        var before = Text(document);
        var editor = await OpenAsync(panel, "Background");

        // Пока ничего не выбрано, это не ошибка: «Применить» выключено без причины под панелью, а вид ссылки не выбран —
        // умолчание зависит от ресурса.
        Assert.Null(panel.Expressions!.Note);
        Assert.False(panel.Expressions.CanApply);
        Assert.Null(editor.KindField.SelectedItem);
        Assert.Equal(["FormBrush", "Surface", "AppBrush"], Keys(editor));

        // Толщина и число кисти не подходят — их видно только без отбора.
        editor.FitsOnlyField.IsChecked = false;
        LiveFormStudio.Frame();

        Assert.Equal(["FormBrush", "Pad", "Surface", "AppBrush", "Gap"], Keys(editor));

        editor.SearchField.Text = "app";
        LiveFormStudio.Frame();

        Assert.Equal(["AppBrush"], Keys(editor));
        Assert.Equal("{DynamicResource AppBrush}", editor.Written);
        Assert.True(await panel.Expressions.CommitAsync());

        await XamlStudio.UntilAsync(() => Text(document).Contains("<Border Width=\"{StaticResource Gap}\" Background=\"{DynamicResource AppBrush}\" />", StringComparison.Ordinal), "ресурс не записан");

        // Жетон ресурса — с образцом его цвета.
        var row = Row(panel, "Background");

        await XamlStudio.UntilAsync(
            () => row.GetVisualDescendants().OfType<AxColorSwatch>().Any(static swatch => swatch.IsEffectivelyVisible && swatch.Color == Color.Parse("#FF336699")),
            "у жетона ресурса нет образца");

        await document.Document!.UndoAsync(Token);
        await XamlStudio.UntilAsync(() => Text(document) == before, "ресурс отменился не одним шагом");
    }

    /// <summary>
    /// Статическая ссылка не предлагается там, где ничего не найдёт: ключ одной темы вне окна. У остального выбирают сами.
    /// </summary>
    [AvaloniaFact]
    public async Task A_static_reference_is_offered_only_where_it_finds_the_key()
    {
        await using var studio = new LiveFormStudio();

        studio.Xaml.Write("App.axaml", Application);

        var document = await Opened(studio);
        var panel = studio.Inspector();
        var editor = await OpenAsync(panel, "Background");

        Choose(editor, "Surface");

        Assert.False(editor.StaticAllowed, "статическая ссылка на ключ одной темы вне окна предложена");
        Assert.Equal("{DynamicResource Surface}", editor.Written);

        Choose(editor, "FormBrush");

        Assert.True(editor.StaticAllowed);

        editor.KindField.SelectedIndex = 1;
        LiveFormStudio.Frame();

        Assert.Equal("{StaticResource FormBrush}", editor.Written);
        Assert.True(await panel.Expressions!.CommitAsync());
        await XamlStudio.UntilAsync(() => Text(document).Contains("Background=\"{StaticResource FormBrush}\"", StringComparison.Ordinal), "ресурс не записан");
    }

    /// <summary>Написанная ссылка открывает панель со своим ключом и видом, а смена вида переписывает её.</summary>
    [AvaloniaFact]
    public async Task A_written_reference_opens_with_its_key_and_kind()
    {
        await using var studio = new LiveFormStudio();

        studio.Xaml.Write("App.axaml", Application);

        var document = await Opened(studio);
        var panel = studio.Inspector();
        var row = Row(panel, "Width");

        await XamlStudio.UntilAsync(() => row.PanelKind == InspectorExpressionKind.Resource, "у ресурса нет панели");
        Assert.True(row.OpenExpression());
        LiveFormStudio.Frame();

        var editor = Assert.IsType<ResourceEditor>(panel.Expressions!.Editor);

        await XamlStudio.UntilAsync(() => editor.Loaded, "служба не ответила о ресурсах");

        Assert.Equal("Gap", (editor.List.SelectedItem as XamlResourceInfo)?.Key);
        Assert.Equal("{StaticResource Gap}", editor.Written);

        editor.KindField.SelectedIndex = 0;
        LiveFormStudio.Frame();

        Assert.True(await panel.Expressions.CommitAsync());
        await XamlStudio.UntilAsync(() => Text(document).Contains("<Border Width=\"{DynamicResource Gap}\" />", StringComparison.Ordinal), "вид ссылки не сменился");
    }

    /// <summary>
    /// Карточка, показывая окно, берёт себе его словарь и стили, а у приложения — его стили; ключ окна и ключ темы
    /// приложения всё равно находят и проверка, и панель — со значениями.
    /// </summary>
    [AvaloniaFact]
    public async Task A_window_and_its_application_theme_keep_their_keys_while_the_card_shows_them()
    {
        await using var studio = new LiveFormStudio();

        studio.Xaml.Write("App.axaml", """
            <Application xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Application.Styles>
                <Styles>
                  <Styles.Resources>
                    <SolidColorBrush x:Key="ThemeBrush" Color="#FF00AA00" />
                  </Styles.Resources>
                </Styles>
              </Application.Styles>
            </Application>
            """);

        studio.Xaml.Write("Colors.axaml", """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="Brand">#FFAA5500</Color>
            </ResourceDictionary>
            """);

        // Ключ окна записан в нём самом, а ключ словаря, который окно включает, синтаксису окна не виден.
        var document = await studio.OpenAsync("MainWindow.axaml", """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300">
              <Window.Resources>
                <ResourceDictionary>
                  <ResourceDictionary.MergedDictionaries>
                    <ResourceInclude Source="/Colors.axaml" />
                  </ResourceDictionary.MergedDictionaries>
                  <SolidColorBrush x:Key="Accent" Color="#FF2563EB" />
                </ResourceDictionary>
              </Window.Resources>
              <StackPanel Background="{DynamicResource Brand}">
                <TextBlock Text="Подпись" Foreground="{DynamicResource Accent}" Background="{DynamicResource ThemeBrush}" />
              </StackPanel>
            </Window>
            """);

        await XamlStudio.UntilAsync(() => document.Form.ApplicationRoot is not null, "приложение формы не встало");

        var panel = studio.Inspector();

        document.Select([XamlElementPath.Parse("/0/0")]);
        LiveFormStudio.Frame();

        await XamlStudio.UntilAsync(() => Chip(panel, "Foreground").Kind == AxChipKind.Accent, "ключ окна назван пропавшим");
        await XamlStudio.UntilAsync(() => Chip(panel, "Background").Kind == AxChipKind.Accent, "ключ темы приложения назван пропавшим");

        var editor = await OpenAsync(panel, "Background");
        var shown = editor.List.ItemsSource!.OfType<XamlResourceInfo>().ToList();

        Assert.Equal(Color.Parse("#FF2563EB"), Assert.Single(shown, static resource => resource.Key == "Accent").Sample);
        Assert.Equal(Color.Parse("#FFAA5500"), Assert.Single(shown, static resource => resource.Key == "Brand").Sample);
        Assert.Equal(Color.Parse("#FF00AA00"), Assert.Single(shown, static resource => resource.Key == "ThemeBrush").Sample);

        panel.Expressions!.Close();
        document.Select([XamlElementPath.Parse("/0")]);
        LiveFormStudio.Frame();

        await XamlStudio.UntilAsync(() => Chip(panel, "Background").Kind == AxChipKind.Accent, "ключ словаря, который окно включает, назван пропавшим");
    }

    /// <summary>
    /// Конвертер из ресурсов формы стоит в списке конвертеров панели привязки статической ссылкой — динамическая на
    /// аргументе привязки не встанет, — а кисть из тех же ресурсов там не стоит.
    /// </summary>
    [AvaloniaFact]
    public async Task A_converter_among_the_resources_is_offered_to_a_binding()
    {
        await using var studio = new LiveFormStudio();

        var document = await studio.OpenAsync("MainView.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:conv="using:Avalonia.Controls.Converters">
              <UserControl.Resources>
                <conv:EnumToBoolConverter x:Key="IsChosen" />
                <SolidColorBrush x:Key="FormBrush" Color="Red" />
              </UserControl.Resources>
              <StackPanel>
                <TextBlock Text="Подпись" />
              </StackPanel>
            </UserControl>
            """);
        var panel = studio.Inspector();

        document.Select([XamlElementPath.Parse("/0/0")]);
        LiveFormStudio.Frame();

        Assert.True(Row(panel, "Text").OpenPanel(InspectorExpressionKind.Binding), "панель привязки не открылась");
        LiveFormStudio.Frame();

        var editor = Assert.IsType<BindingEditor>(panel.Expressions!.Editor);

        await XamlStudio.UntilAsync(() => Converters(editor).Contains("{StaticResource IsChosen}"), "конвертера из ресурсов в списке нет");
        Assert.DoesNotContain("{StaticResource FormBrush}", Converters(editor));

        editor.ConverterField.SelectedItem = "{StaticResource IsChosen}";
        LiveFormStudio.Frame();

        Assert.Contains("Converter={StaticResource IsChosen}", editor.Written, StringComparison.Ordinal);
    }

    private static async Task<LiveFormDocument> Opened(LiveFormStudio studio)
    {
        var document = await studio.OpenAsync("MainView.axaml", Form);

        await XamlStudio.UntilAsync(() => document.Form.ApplicationRoot is not null, "приложение формы не встало");

        document.Select([Border]);
        LiveFormStudio.Frame();

        return document;
    }

    /// <summary>Открывает панель ресурса строки — как пункт «Ресурс…» её меню.</summary>
    private static async Task<ResourceEditor> OpenAsync(InspectorPanel panel, string id)
    {
        Assert.True(Row(panel, id).OpenPanel(InspectorExpressionKind.Resource), "панель ресурса не открылась");
        LiveFormStudio.Frame();

        var editor = Assert.IsType<ResourceEditor>(panel.Expressions!.Editor);

        await XamlStudio.UntilAsync(() => editor.Loaded, "служба не ответила о ресурсах");

        return editor;
    }

    /// <summary>Выбирает ресурс по ключу среди показанных — как щелчок по строке списка.</summary>
    private static void Choose(ResourceEditor editor, string key)
    {
        editor.List.SelectedItem = Assert.Single(editor.List.ItemsSource!.OfType<XamlResourceInfo>(), resource => resource.Key == key);
        LiveFormStudio.Frame();
    }

    private static List<string> Keys(ResourceEditor editor) =>
        editor.List.ItemsSource?.OfType<XamlResourceInfo>().Select(static resource => resource.Key).ToList() ?? [];

    private static List<string> Converters(BindingEditor editor) => editor.ConverterField.ItemsSource?.OfType<string>().ToList() ?? [];

    private static AxChip Chip(InspectorPanel panel, string id) => Assert.IsType<AxChip>(Row(panel, id).Content);

    private static InspectorRow Row(InspectorPanel panel, string id)
    {
        var cell = Assert.Single(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == id);

        panel.View!.List.ScrollIntoView(cell);
        LiveFormStudio.Frame();

        return cell.Row;
    }

    private static string Text(LiveFormDocument document) => document.Document!.Syntax.SourceText.ToString();
}
