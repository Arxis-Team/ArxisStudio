using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.Inspector.Panel;
using ArxisStudio.Xaml;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Выражения формы в инспекторе дизайнера (запись 424): жетон с видом и короткой записью, работает ли выражение — тоном,
/// а почему нет — заметкой под строкой.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class DesignerExpressionTests
{
    private static XamlElementPath Label => XamlElementPath.Parse("/0/0");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Привязка, чей путь нашёлся, — акцентный жетон с путём и значком; опечатка — тоном ошибки и заметкой, а
    /// исправленная — снова акцентом, без заметки.
    /// </summary>
    [AvaloniaFact]
    public async Task A_binding_is_a_token_that_says_whether_its_path_is_there()
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
                <TextBlock Text="{Binding Title}" ToolTip.Tip="{Binding Nmae}" />
              </StackPanel>
            </Window>
            """);
        var panel = studio.Inspector();

        document.Select([Label]);
        LiveFormStudio.Frame();

        await XamlStudio.UntilAsync(() => Chip(panel, "Text").Kind == AxChipKind.Accent, "найденная привязка не стала акцентным жетоном");

        var (title, icon, path) = Parts(panel, "Text");

        Assert.Equal("Title", path.Text);
        Assert.Same(AxIcons.Binding, icon.Data);
        Assert.Contains("MainViewModel → String", ToolTip.GetTip(title) as string, StringComparison.Ordinal);

        var noMember = Format(studio, "module.xaml.binding.noMember", "MainViewModel", "Nmae");

        await XamlStudio.UntilAsync(() => Row(panel, "ToolTip.Tip").Note as string == noMember, "сломанная привязка не сказала почему");

        var typo = Chip(panel, "ToolTip.Tip");

        Assert.Equal(AxValidationState.Error, AxValidation.GetState(typo));
        Assert.Equal("Nmae", Parts(panel, "ToolTip.Tip").Text.Text);

        await document.Document!.EditAsync(
            "Подсказка",
            editor => editor.SetAttribute(ElementAt(editor.Document), XamlQualifiedName.Unprefixed("ToolTip.Tip"), "{Binding Customer.Name}"),
            Token);

        await XamlStudio.UntilAsync(() => Chip(panel, "ToolTip.Tip").Kind == AxChipKind.Accent, "исправленная привязка не стала акцентной");

        Assert.Equal(AxValidationState.None, AxValidation.GetState(Chip(panel, "ToolTip.Tip")));
        Assert.Null(Row(panel, "ToolTip.Tip").Note);
        Assert.Equal("Customer.Name", Parts(panel, "ToolTip.Tip").Text.Text);
    }

    /// <summary>
    /// Без показа выражение не проверить, но форма, чьё выражение называет член, которого нет, не загружается и говорит,
    /// на каком атрибуте упала: жетон этого выражения — тоном ошибки, с её словами под строкой.
    /// </summary>
    [AvaloniaFact]
    public async Task Without_objects_the_expression_the_load_failed_on_is_broken()
    {
        await using var studio = new LiveFormStudio();

        var document = await studio.OpenAsync("MainWindow.axaml", """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300">
              <StackPanel>
                <TextBlock Text="Подпись" />
              </StackPanel>
            </Window>
            """);
        var panel = studio.Inspector();

        document.Select([Label]);
        LiveFormStudio.Frame();

        await document.Document!.EditAsync(
            "Метка",
            editor => editor.SetAttribute(ElementAt(editor.Document), XamlQualifiedName.Unprefixed("Tag"), "{x:Static TextBlock.Nope}"),
            Token);

        await XamlStudio.UntilAsync(() => document.Document!.State != XamlDocumentState.Live, "форма с членом, которого нет, встала живой");

        var failure = document.Document!.Diagnostics.First(static diagnostic => diagnostic is { IsError: true, Span: not null }).Message;

        // Строка написанного встаёт, когда инспектор перечитал форму, уже не живую.
        await XamlStudio.UntilAsync(() => Find(panel, "Tag")?.Note as string == failure, "выражение, на котором упала загрузка, не названо сломанным");

        var (chip, icon, text) = Parts(panel, "Tag");

        Assert.Equal(AxValidationState.Error, AxValidation.GetState(chip));
        Assert.Equal("TextBlock.Nope", text.Text);
        Assert.Same(AxIcons.Pin, icon.Data);

        // Подпись написана значением, а не выражением: о ней загрузка не говорила.
        Assert.Null(Row(panel, "Text").Note);
    }

    private static InspectorRow Row(InspectorPanel panel, string id) =>
        Find(panel, id) ?? throw new InvalidOperationException($"строки {id} нет");

    /// <summary>Строка свойства, прокрученная на вид; null — такой строки нет.</summary>
    private static InspectorRow? Find(InspectorPanel panel, string id)
    {
        if (panel.List!.Items.OfType<PropertyCell>().SingleOrDefault(cell => cell.Row.Property.Id == id) is not { } cell)
            return null;

        panel.View!.List.ScrollIntoView(cell);
        LiveFormStudio.Frame();

        return cell.Row;
    }

    private static AxChip Chip(InspectorPanel panel, string id) => Assert.IsType<AxChip>(Row(panel, id).Content);

    /// <summary>Жетон строки: чип, значок вида и текст записи.</summary>
    private static (AxChip Chip, AxIcon Icon, TextBlock Text) Parts(InspectorPanel panel, string id)
    {
        var chip = Chip(panel, id);
        var parts = Assert.IsType<DockPanel>(chip.Content);

        return (chip, Assert.IsType<AxIcon>(parts.Children[0]), Assert.IsType<TextBlock>(parts.Children[1]));
    }

    private static XamlElement ElementAt(XamlDocument document) =>
        Label.Resolve(document) ?? throw new InvalidOperationException("подписи нет в тексте");

    private static string Format(LiveFormStudio studio, string key, params object[] arguments) =>
        string.Format(
            CultureInfo.CurrentCulture,
            (studio.Xaml.XamlModule.Studio ?? throw new InvalidOperationException("у модуля нет словаря")).Strings[key],
            arguments);
}
