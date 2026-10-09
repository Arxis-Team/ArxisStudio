using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Inspector;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.Inspector.Panel;
using ArxisStudio.Modules.Sample;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Shell;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Образец дополняет инспектор так, как это делает плагин: редактор тени, раздел доступности, таблица метаданных и
/// свои настройки в инспекторе (запись 365).
/// </summary>
[Collection(StudioStateCollection.Name)]
public class SampleInspectorTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
          <StackPanel>
            <Border x:Name="Card" BoxShadow="2 4 8 0 #40000000, 0 0 1 0 #20000000" Padding="8">
              <TextBlock Text="Карточка" />
            </Border>
            <Button x:Name="Go" />
          </StackPanel>
        </Window>
        """;

    /// <summary>
    /// Тень рамки правит редактор образца: ячейка смещения пишет первую тень, вторая остаётся как была, а пояснение
    /// строки дописала таблица образца поверх таблицы Avalonia.
    /// </summary>
    [AvaloniaFact]
    public async Task The_samples_shadow_editor_writes_the_first_shadow_and_keeps_the_rest()
    {
        await using var studio = new LiveFormStudio();
        var sample = Sample(studio);
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([XamlElementPath.Parse("/0/0")]);
        LiveFormStudio.Frame();

        var row = Row(panel, "BoxShadow");
        var vector = row.GetVisualDescendants().OfType<AxVectorField>().Single();

        Assert.Equal(new[] { "X", "Y", "Blur", "Spread" }, vector.Labels);
        Assert.Equal(sample.Studio!.Strings["sample.shadow.description"], row.Property.Description);

        var cell = vector.Cells[0];

        cell.Focus();
        cell.SelectAll();
        studio.Window.KeyTextInput("6");
        studio.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        LiveFormStudio.Frame();

        await XamlStudio.UntilAsync(
            () => Shadows(document) is { Count: 2 } shadows && shadows[0].OffsetX == 6,
            "смещение первой тени не записано");

        var written = Shadows(document)!.Value;

        Assert.Equal(4, written[0].OffsetY);
        Assert.Equal(8, written[0].Blur);
        Assert.Equal(Color.FromArgb(0x40, 0, 0, 0), written[0].Color);
        Assert.Equal(1, written[1].Blur);
    }

    /// <summary>
    /// Раздел доступности образца берёт себе строки имени и пояснения для диктора и предупреждает, пока кнопке
    /// нечем назваться; имя, набранное в его строке, предупреждение снимает.
    /// </summary>
    [AvaloniaFact]
    public async Task The_samples_accessibility_section_warns_until_a_button_has_a_name()
    {
        await using var studio = new LiveFormStudio();
        var sample = Sample(studio);
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([XamlElementPath.Parse("/0/1")]);
        LiveFormStudio.Frame();

        Assert.DoesNotContain(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == "AutomationProperties.Name");

        var section = Assert.Single(panel.List.Items.OfType<SectionCell>());

        panel.View!.List.ScrollIntoView(section);
        LiveFormStudio.Frame();

        var warning = section.GetVisualDescendants().OfType<AxBanner>().Single();
        var name = section.GetVisualDescendants().OfType<InspectorRow>().Single(row => row.Property.Id == "AutomationProperties.Name");

        Assert.True(warning.IsVisible, "кнопка без имени не вызвала предупреждения");
        Assert.Equal(sample.Studio!.Strings["sample.accessibility.unnamed"], Assert.IsType<TextBlock>(warning.Content).Text);

        var box = name.GetVisualDescendants().OfType<TextBox>().First();

        box.Focus();
        box.SelectAll();
        studio.Window.KeyTextInput("Пуск");
        studio.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, string.Empty);

        await XamlStudio.UntilAsync(() => !warning.IsVisible, "имя для диктора не сняло предупреждение");

        Assert.Contains("AutomationProperties.Name=\"Пуск\"", document.Document!.Syntax.SourceText.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Панель образца показывает свои настройки в инспекторе: подписи и раздел — из таблицы образца, границы и
    /// значения по умолчанию — из атрибутов .NET; правка в инспекторе меняет приветствие панели, а ушедшая панель
    /// уносит настройки из инспектора.
    /// </summary>
    [AvaloniaFact]
    public async Task The_sample_panel_shows_its_settings_in_the_inspector_and_follows_them()
    {
        await using var studio = new LiveFormStudio();
        var sample = Sample(studio);
        var panel = studio.Inspector();
        var strings = sample.Studio!.Strings;
        var model = new SamplePanelModel(sample.Studio);

        Assert.True(model.CanInspect, "образец не нашёл инспектор");

        model.Inspect();
        LiveFormStudio.Frame();

        Assert.Equal(strings["sample.settings"], panel.View!.Title.Text);

        var group = Assert.Single(panel.List!.Items.OfType<GroupCell>(), cell => cell.CategoryId == SampleSettings.Category);

        Assert.Equal(strings["sample.category"], group.Header.Content);

        var greeting = Row(panel, nameof(SampleSettings.Greeting));
        var repeat = Row(panel, nameof(SampleSettings.Repeat));

        Assert.Equal(strings["sample.greeting"], greeting.Property.Label);
        Assert.Equal(5, repeat.Property.Constraints.Maximum);
        Assert.Equal(3, Row(panel, nameof(SampleSettings.Tone)).GetVisualDescendants().OfType<AxSegmentedControl>().Single().Items.Count);

        var field = Assert.IsType<AxNumberField>(Assert.IsType<PluginSurface>(repeat.Content).Child);

        field.Focus();
        field.SelectAll();
        studio.Window.KeyTextInput("2");
        studio.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        LiveFormStudio.Frame();

        Assert.Equal(2, model.Settings.Repeat);
        Assert.Equal(
            string.Join(' ', Enumerable.Repeat(string.Format(CultureInfo.CurrentCulture, strings["module.sample.greeting"], "Arxis"), 2)),
            model.Greeting);
        Assert.True(repeat.IsOverridden, "изменённое значение не помечено заданным");

        model.Dispose();
        LiveFormStudio.Frame();

        Assert.Null(panel.List.Target);
    }

    /// <summary>Поднимает образец и отдаёт его вклады инспектору — как студия поднимает встроенный модуль.</summary>
    private static LoadedPlugin Sample(LiveFormStudio studio)
    {
        var sample = studio.Xaml.Host.LoadBuiltIn(typeof(SampleModule).Assembly);

        Assert.True(sample.IsLoaded, sample.Error);
        studio.Xaml.Contributions.Add(sample);

        return sample;
    }

    private static InspectorRow Row(InspectorPanel panel, string id)
    {
        LiveFormStudio.Frame();

        var cell = Assert.Single(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == id);

        panel.View!.List.ScrollIntoView(cell);
        LiveFormStudio.Frame();

        return cell.Row;
    }

    private static BoxShadows? Shadows(LiveFormDocument document) =>
        XamlElementPath.Parse("/0/0").Resolve(document.Document!.Syntax)?.GetAttribute("BoxShadow")?.GetValueText() is { } text
            ? BoxShadows.Parse(text)
            : null;
}
