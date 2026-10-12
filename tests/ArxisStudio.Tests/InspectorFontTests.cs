using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Panel;
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
/// Шрифт списком семейств, каждое — своим начертанием, с вводом своего имени: что видно в поле, что пишется и когда
/// (запись 380).
/// </summary>
public class InspectorFontTests
{
    /// <summary>
    /// Семейство темы — составное, а в поле его имя, как у Unity: «Brand Sans», а не «fonts:Brand#…»; у «$Default» — имя
    /// шрифта, который за ним стоит. Нынешнее семейство стоит в списке первым, даже если в системе его нет, и каждое имя
    /// списка написано своим шрифтом.
    /// </summary>
    [AvaloniaFact]
    public void The_field_shows_the_family_name_and_each_family_is_written_in_its_own_face()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var theme = FontFamily.Parse("fonts:Brand#Brand Sans, $Default");
        var target = Target(Families);

        target.Put("FontFamily", new InspectorValue(theme.ToString()) { Shared = theme, Source = InspectorValueSource.Style });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var box = Box(panel);

        Assert.Equal("Brand Sans", box.Text);
        Assert.Equal("Brand Sans", box.Items.OfType<string>().First());

        target.Set("FontFamily", new InspectorValue(FontFamily.Default.ToString()) { Shared = FontFamily.Default });
        studio.Frame();

        Assert.Equal(FontManager.Current.DefaultFontFamily.Name, box.Text);
        Assert.NotEqual(FontFamily.DefaultFontFamilyName, box.Text);

        box.IsDropDownOpen = true;
        studio.Frame();

        var rows = studio.Window.GetVisualDescendants().OfType<AxComboBoxItem>().ToList();

        Assert.NotEmpty(rows);
        Assert.All(rows, static row =>
        {
            var sample = row.GetVisualDescendants().OfType<TextBlock>().First(text => text.DataContext is string);

            Assert.Equal(sample.DataContext, sample.FontFamily.Name);
        });
        Assert.Empty(target.Writes);
    }

    /// <summary>Выбранное в списке пишется именем — когда список закрывается, одной записью.</summary>
    [AvaloniaFact]
    public void A_family_chosen_in_the_list_is_written_by_its_name()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target(Families);

        target.Put("FontFamily", new InspectorValue("Arial") { Shared = new FontFamily("Arial"), Source = InspectorValueSource.Local });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var box = Box(panel);

        box.IsDropDownOpen = true;
        studio.Frame();
        box.SelectedItem = "Consolas";
        box.IsDropDownOpen = false;
        studio.Frame();

        Assert.Equal([new InspectorWrite("FontFamily", "Consolas")], Assert.Single(target.Writes));
    }

    /// <summary>
    /// Набор не пишет на каждой букве — и имя, совпавшее со строкой списка, ещё не выбор; Enter и уход каретки пишут
    /// набранное, своё имя тоже, а Esc возвращает показанное.
    /// </summary>
    [AvaloniaFact]
    public void A_typed_name_is_written_on_enter_or_on_leaving_and_escape_brings_the_shown_back()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target(Families);

        target.Put("FontFamily", new InspectorValue("Arial") { Shared = new FontFamily("Arial"), Source = InspectorValueSource.Local });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var box = Box(panel);
        var input = box.GetVisualDescendants().OfType<AxTextBox>().Single(part => part.Name == "PART_EditableTextBox");

        input.Focus();
        input.SelectAll();
        studio.Window.KeyTextInput("Consolas");
        studio.Frame();

        Assert.Empty(target.Writes);

        Press(studio, Key.Enter);

        Assert.Equal([new InspectorWrite("FontFamily", "Consolas")], Assert.Single(target.Writes));

        input.SelectAll();
        studio.Window.KeyTextInput("Brand Sans");
        Press(studio, Key.Enter);

        Assert.Equal([new InspectorWrite("FontFamily", "Brand Sans")], target.Writes[^1]);

        input.SelectAll();
        studio.Window.KeyTextInput("Typo");
        Press(studio, Key.Escape);

        Assert.Equal("Brand Sans", box.Text);
        Assert.Equal(2, target.Writes.Count);

        input.SelectAll();
        studio.Window.KeyTextInput("Georgia");
        studio.Window.GetVisualDescendants().OfType<AxSearchField>().First().Focus();
        studio.Frame();

        Assert.Equal([new InspectorWrite("FontFamily", "Georgia")], target.Writes[^1]);
        Assert.Equal(3, target.Writes.Count);
    }

    /// <summary>Список, открытый с набранным и закрытый без выбора, набранное не пишет: оно ждёт Enter или ухода каретки.</summary>
    [AvaloniaFact]
    public void A_list_closed_without_a_choice_keeps_the_typed_name_as_a_draft()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target(Families);

        target.Put("FontFamily", new InspectorValue("Arial") { Shared = new FontFamily("Arial"), Source = InspectorValueSource.Local });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var box = Box(panel);
        var input = box.GetVisualDescendants().OfType<AxTextBox>().Single(part => part.Name == "PART_EditableTextBox");

        input.Focus();
        input.SelectAll();
        studio.Window.KeyTextInput("Cons");
        box.IsDropDownOpen = true;
        studio.Frame();
        box.IsDropDownOpen = false;
        studio.Frame();

        Assert.Empty(target.Writes);
        Assert.Equal("Cons", box.Text);

        Press(studio, Key.Enter);

        Assert.Equal([new InspectorWrite("FontFamily", "Cons")], Assert.Single(target.Writes));
    }

    /// <summary>Пустое поле снимает семейство: шрифт снова приходит от темы.</summary>
    [AvaloniaFact]
    public void An_emptied_field_takes_the_family_out()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target(Families);

        target.Put("FontFamily", new InspectorValue("Arial") { Shared = new FontFamily("Arial"), Source = InspectorValueSource.Local });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var input = Box(panel).GetVisualDescendants().OfType<AxTextBox>().Single(part => part.Name == "PART_EditableTextBox");

        input.Focus();
        input.SelectAll();
        Press(studio, Key.Back);
        Press(studio, Key.Enter);

        Assert.Equal([new InspectorWrite("FontFamily", null)], Assert.Single(target.Writes));
    }

    /// <summary>Список виртуализован: строк построено меньше, чем семейств, — сотни имён своим шрифтом не строятся разом.</summary>
    [AvaloniaFact]
    public void The_list_builds_fewer_rows_than_families()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var many = Enumerable.Range(1, 300).Select(static at => $"Family {at:000}").ToArray();
        var target = Target(many);

        target.Put("FontFamily", new InspectorValue("Family 001") { Shared = new FontFamily("Family 001"), Source = InspectorValueSource.Local });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var box = Box(panel);

        box.IsDropDownOpen = true;
        studio.Frame();

        var built = studio.Window.GetVisualDescendants().OfType<AxComboBoxItem>().Count();

        Assert.InRange(built, 1, many.Length / 2);
    }

    private static readonly string[] Families = ["Arial", "Consolas", "Segoe UI"];

    // Тип не контрол: таблица Avalonia ставит FontFamily контролов шрифты системы, а проба держит свои семейства.
    private static FakeTarget Target(IReadOnlyList<string> families) => new(
        typeof(object),
        FakeTarget.Property("FontFamily", typeof(FontFamily)) with
        {
            Constraints = new InspectorConstraints { Choices = [.. families.Select(static family => new InspectorChoice(family))] },
        });

    private static AxComboBox Box(InspectorPanel panel)
    {
        var row = Assert.Single(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == "FontFamily").Row;

        return Assert.IsType<AxComboBox>(Assert.IsType<PluginSurface>(row.Content).Child);
    }

    private static void Press(InspectorStudio studio, Key key)
    {
        studio.Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        studio.Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        studio.Frame();
    }
}
