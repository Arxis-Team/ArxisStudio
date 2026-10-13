using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Panel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Жетон выражения в строке инспектора (контракт 1.3, запись 424): вид — значком, короткая запись — текстом,
/// работает ли оно — тоном, а сломанное — заметкой под строкой.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class InspectorExpressionTests
{
    /// <summary>
    /// Найденная привязка — акцентный жетон со значком привязки и путём; подсказка — полная запись, откуда и куда
    /// пришёл путь и значение сейчас.
    /// </summary>
    [AvaloniaFact]
    public void A_resolved_binding_is_an_accent_token_with_its_icon_its_path_and_what_it_gives()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target();

        target.Put("Text", Bound("Анна", "{Binding Customer.Name}", new InspectorExpression(InspectorExpressionKind.Binding, "Customer.Name")
        {
            State = InspectorExpressionState.Resolved,
            Detail = "MainViewModel → String",
        }));

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var (chip, icon, text) = Token(panel, "Text");

        Assert.Equal("Customer.Name", text.Text);
        Assert.True(icon.IsVisible, "у жетона нет значка вида");
        Assert.Same(AxIcons.Binding, icon.Data);
        Assert.Equal(AxChipKind.Accent, chip.Kind);
        Assert.Equal(AxValidationState.None, AxValidation.GetState(chip));
        Assert.Equal(
            string.Join(Environment.NewLine, "{Binding Customer.Name}", "MainViewModel → String", Now(studio, "Анна")),
            ToolTip.GetTip(chip));
        Assert.Null(Row(panel, "Text").Note);
    }

    /// <summary>
    /// Сломанное выражение — жетоном с тоном ошибки, а почему — заметкой под строкой; починили — тон и заметка уходят.
    /// </summary>
    [AvaloniaFact]
    public void A_broken_expression_takes_the_error_tone_and_a_note_until_it_is_fixed()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target();

        target.Put("Text", Bound(null, "{Binding Nmae}", new InspectorExpression(InspectorExpressionKind.Binding, "Nmae")
        {
            State = InspectorExpressionState.Broken,
        }, error: "У MainViewModel нет свойства «Nmae»"));

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var (chip, _, text) = Token(panel, "Text");
        var row = Row(panel, "Text");

        Assert.Equal("Nmae", text.Text);
        Assert.Equal(AxValidationState.Error, AxValidation.GetState(chip));
        Assert.NotEqual(AxChipKind.Accent, chip.Kind);
        Assert.Equal(Resource(chip, "AxErrorFillColor"), (chip.Background as ISolidColorBrush)?.Color);
        Assert.Equal("У MainViewModel нет свойства «Nmae»", row.Note);

        target.Set("Text", Bound("Главное окно", "{Binding Title}", new InspectorExpression(InspectorExpressionKind.Binding, "Title")
        {
            State = InspectorExpressionState.Resolved,
        }));
        studio.Frame();

        Assert.Equal("Title", text.Text);
        Assert.Equal(AxValidationState.None, AxValidation.GetState(chip));
        Assert.Equal(AxChipKind.Accent, chip.Kind);
        Assert.Null(row.Note);
    }

    /// <summary>
    /// Непроверенное — нейтральный жетон со значком своего вида: о нём не сказано ни «найдено», ни «сломано»; значок
    /// плагина подменяет значок вида.
    /// </summary>
    [AvaloniaFact]
    public void An_unchecked_expression_is_a_plain_token_with_the_icon_of_its_kind_or_its_own()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target();
        var own = new EllipseGeometry(new Avalonia.Rect(0, 0, 16, 16));

        target.Put("Background", Bound(null, "{DynamicResource Accent}", new InspectorExpression(InspectorExpressionKind.Resource, "Accent")));
        target.Put("Width", Bound(null, "{local:Upper Name}", new InspectorExpression(InspectorExpressionKind.Other, "Upper") { Icon = own }));

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var (resource, resourceIcon, resourceText) = Token(panel, "Background");

        Assert.Equal("Accent", resourceText.Text);
        Assert.Same(AxIcons.Palette, resourceIcon.Data);
        Assert.Equal(AxChipKind.Default, resource.Kind);
        Assert.Equal(AxValidationState.None, AxValidation.GetState(resource));

        var (_, ownIcon, _) = Token(panel, "Width");

        Assert.Same(own, ownIcon.Data);
    }

    /// <summary>Цель, которая о выражении ничего не говорит, получает прежний жетон: написанное, акцентом, без значка.</summary>
    [AvaloniaFact]
    public void Without_a_word_about_the_expression_the_token_shows_what_is_written()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target();

        target.Put("Text", new InspectorValue("Анна") { Source = InspectorValueSource.Expression, Written = "{Binding Customer.Name}" });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var (chip, icon, text) = Token(panel, "Text");

        Assert.Equal("{Binding Customer.Name}", text.Text);
        Assert.False(icon.IsVisible, "значок вида без слова о виде");
        Assert.Equal(AxChipKind.Accent, chip.Kind);
        Assert.Equal("{Binding Customer.Name}", ToolTip.GetTip(chip));
    }

    private static FakeTarget Target() => new(
        typeof(TextBlock),
        FakeTarget.Property("Text", typeof(string)),
        FakeTarget.Property("Background", typeof(IBrush)),
        FakeTarget.Property("Width", typeof(double)));

    private static InspectorValue Bound(string? now, string written, InspectorExpression expression, string? error = null) =>
        new(now) { Source = InspectorValueSource.Expression, Written = written, Expression = expression, Error = error };

    private static string Now(InspectorStudio studio, string value) =>
        string.Format(CultureInfo.CurrentCulture, studio.Strings["inspector.expression.now"], value);

    private static Color Resource(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is Color color
            ? color
            : throw new InvalidOperationException($"ключа {key} в теме нет");

    private static InspectorRow Row(InspectorPanel panel, string id) =>
        Assert.Single(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == id).Row;

    /// <summary>Жетон строки: сам чип, значок вида и текст записи.</summary>
    private static (AxChip Chip, AxIcon Icon, TextBlock Text) Token(InspectorPanel panel, string id)
    {
        var chip = Assert.IsType<AxChip>(Row(panel, id).Content);
        var parts = Assert.IsType<DockPanel>(chip.Content);

        return (chip, Assert.Single(parts.Children.OfType<AxIcon>()), Assert.Single(parts.Children.OfType<TextBlock>()));
    }
}
