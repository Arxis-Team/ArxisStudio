using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Editors;
using ArxisStudio.Modules.Inspector.Panel;
using ArxisStudio.Shell;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Число в границах — ползунком с полем, как <c>[Range]</c> у Unity: кто его получает и как он пишет (запись 378).
/// </summary>
public class InspectorSliderTests
{
    /// <summary>
    /// <c>Opacity</c> — ползунок с полем и границами метаданных, а подпись строки тянет его число; <c>FontSize</c> с
    /// границами таблицы 1…1000 — числовое поле: тянуть шрифт по тысяче делений незачем. Подсказка ползунка у числа без
    /// конечных границ — тоже числовое поле: дорожке не на чем стоять.
    /// </summary>
    [AvaloniaFact]
    public void Opacity_is_a_slider_and_font_size_stays_a_number()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();

        using var shown = studio.Service.Show(Target());

        studio.Frame();

        var range = Range(panel);

        Assert.Equal(0, range.Minimum);
        Assert.Equal(1, range.Maximum);
        Assert.Equal(0.01, range.Step);
        Assert.Equal(2, range.Decimals);
        Assert.Same(range.NumberField, Row(panel, "Opacity").ScrubTarget);
        Assert.IsType<AxNumberField>(Field(panel, "FontSize"));
        Assert.IsType<AxNumberField>(Field(panel, "Depth"));
    }

    /// <summary>Протяжку ползунка цель получает одной записью — при отпускании.</summary>
    [AvaloniaFact]
    public void A_drag_on_the_slider_is_one_write_at_release()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target();

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var slider = Range(panel).Slider;

        studio.Window.MouseDown(OnTrack(slider, studio.Window, 0.2), MouseButton.Left);
        studio.Frame();
        studio.Window.MouseMove(OnTrack(slider, studio.Window, 0.4));
        studio.Frame();
        studio.Window.MouseMove(OnTrack(slider, studio.Window, 0.5));
        studio.Frame();

        Assert.Empty(target.Writes);

        studio.Window.MouseUp(OnTrack(slider, studio.Window, 0.5), MouseButton.Left);
        studio.Frame();

        Assert.Equal([new InspectorWrite("Opacity", "0.5")], Assert.Single(target.Writes));
    }

    /// <summary>Esc посреди протяжки — ни одной записи, и поле снова показывает значение цели.</summary>
    [AvaloniaFact]
    public void Escape_in_the_middle_of_a_drag_writes_nothing()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target();

        target.Put("Opacity", new InspectorValue("0.3") { Source = InspectorValueSource.Local });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var range = Range(panel);

        studio.Window.MouseDown(OnTrack(range.Slider, studio.Window, 0.7), MouseButton.Left);
        studio.Frame();
        studio.Window.MouseMove(OnTrack(range.Slider, studio.Window, 0.9));
        studio.Frame();
        studio.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        studio.Frame();
        studio.Window.MouseUp(OnTrack(range.Slider, studio.Window, 0.9), MouseButton.Left);
        studio.Frame();

        Assert.Empty(target.Writes);
        Assert.Equal(0.3, range.Value, 6);
        Assert.Equal("0.3", range.NumberField.Text);
    }

    /// <summary>Шаг ползунка клавишей — запись на каждый шаг; набранное в поле — одна запись.</summary>
    [AvaloniaFact]
    public void Keys_and_typed_numbers_write_once_each()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target();

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var range = Range(panel);

        Assert.True(range.Slider.Focus(NavigationMethod.Tab), "ползунок не взял клавиатуру");

        Press(studio, Key.Right);
        Press(studio, Key.End);

        Assert.Equal(
            ["0.01", "1"],
            target.Writes.Select(static write => Assert.Single(write).Text));

        range.NumberField.Focus();
        range.NumberField.SelectAll();
        studio.Window.KeyTextInput("0.25");
        Press(studio, Key.Enter);

        Assert.Equal(3, target.Writes.Count);
        Assert.Equal([new InspectorWrite("Opacity", "0.25")], target.Writes[^1]);
        Assert.Equal(0.25, range.Slider.Value, 6);
    }

    private static FakeTarget Target() => new(
        typeof(Button),
        FakeTarget.Property("Opacity", typeof(double)),
        FakeTarget.Property("FontSize", typeof(double)),
        FakeTarget.Property("Depth", typeof(double)) with
        {
            Editor = SliderEditor.Hint,
            Constraints = new InspectorConstraints { Minimum = 0, Maximum = double.PositiveInfinity },
        });

    private static AxRangeField Range(InspectorPanel panel) => Assert.IsType<AxRangeField>(Field(panel, "Opacity"));

    private static Control? Field(InspectorPanel panel, string id) =>
        Assert.IsType<PluginSurface>(Row(panel, id).Content).Child;

    private static InspectorRow Row(InspectorPanel panel, string id) =>
        Assert.Single(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == id).Row;

    /// <summary>Точка дорожки, которая значит <paramref name="value"/> между нулём и единицей, — в координатах окна.</summary>
    private static Point OnTrack(AxSlider slider, Window window, double value)
    {
        var track = slider.GetVisualDescendants().OfType<Track>().Single();
        var thumb = Assert.IsAssignableFrom<Thumb>(track.Thumb);
        var x = thumb.Bounds.Width / 2 + value * (track.Bounds.Width - thumb.Bounds.Width);

        return track.TranslatePoint(new Point(x, track.Bounds.Height / 2), window)!.Value;
    }

    private static void Press(InspectorStudio studio, Key key)
    {
        studio.Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        studio.Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        studio.Frame();
    }
}
