using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.Inspector.Panel;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Sdk;
using ArxisStudio.Dragging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Картинка формы из проекта (запись 381): загрузка читает её из файлов проекта, ещё до сборки, а инспектор ставит её
/// полем объекта — выбором ⊙ из картинок решения и броском из окна проекта.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class DesignerImageTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
          <StackPanel>
            <Image x:Name="Logo" Width="32" Height="32" />
          </StackPanel>
        </Window>
        """;

    private static XamlElementPath Logo => XamlElementPath.Parse("/0/0");

    /// <summary>
    /// Картинка, которой нет ни в одной сборке, — файл проекта, ещё не собранный, — встаёт на форму: её читает служба
    /// XAML из файлов проекта, а не загрузчик Avalonia из сборок.
    /// </summary>
    [AvaloniaFact]
    public async Task An_image_of_the_project_shows_on_the_form_before_any_build()
    {
        await using var studio = new LiveFormStudio();

        studio.Xaml.Write("Assets/logo.png", Png(6));

        var document = await studio.OpenAsync(
            "MainWindow.axaml", Form.Replace("<Image x:Name=\"Logo\"", "<Image x:Name=\"Logo\" Source=\"/Assets/logo.png\"", StringComparison.Ordinal));

        var shown = Assert.IsType<Bitmap>(Shown(studio).Source);

        Assert.Equal(new PixelSize(6, 6), shown.PixelSize);

        // Картинка пишется текстом, а читает её загрузка: проверка значения имя ресурса пропускает.
        Assert.True(document.Shown!.GetMember(Logo, "Source") is { AcceptsText: true }, "у картинки нет текстовой формы");
        Assert.Null(document.Shown.CheckValue(Logo, "Source", "/Assets/elsewhere.png").Error);
    }

    /// <summary>
    /// Поле картинки — заглушка «Нет (Image)», ⊙ — картинки решения, ставшие ресурсами, а файл вне ресурсов в списке
    /// не стоит; выбранная пишется путём от корня сборки, и форма её показывает.
    /// </summary>
    [AvaloniaFact]
    public async Task The_target_lists_the_projects_images_and_a_choice_is_written_and_shown()
    {
        await using var studio = new LiveFormStudio();

        studio.Xaml.Write("Assets/logo.png", Png(6));
        studio.Xaml.Write("Assets/Icons/star.png", Png(8));
        studio.Xaml.Write("Docs/shot.png", Png(4));

        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Logo]);
        LiveFormStudio.Frame();

        var field = Field(panel);

        Assert.True(field.IsEmpty, "у картинки, которой нет, поле не пустое");
        Assert.Equal("common", panel.List!.CategoryOf(Assert.Single(panel.List.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == "Source").Row.Property));
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, studio.Strings["inspector.image.none"], "Image"), field.PlaceholderText);

        field.Focus();
        LiveFormStudio.Press(field, Key.Enter);
        LiveFormStudio.Frame();

        var list = Picker(studio);
        var titles = list.Items.OfType<InspectorChoice>().Select(static choice => choice.Title).ToList();

        Assert.Equal(["star.png", "logo.png"], titles);

        // Поиск отбирает по имени и пути, и строки переживают отбор: ячейку, которую список перерабатывает, шаблон строит
        // без варианта.
        var search = Assert.Single(Popup(studio).GetVisualDescendants().OfType<AxSearchField>());

        await XamlStudio.UntilAsync(() => search.IsKeyboardFocusWithin, "каретка не встала в поиск выбора");

        foreach (var letter in "sta")
        {
            studio.Window.KeyTextInput(letter.ToString());
            LiveFormStudio.Frame();
        }

        Assert.Equal(["star.png"], list.Items.OfType<InspectorChoice>().Select(static choice => choice.Title));

        search.Text = string.Empty;
        LiveFormStudio.Frame();

        Assert.Equal(2, list.ItemCount);

        list.SelectedItem = list.Items.OfType<InspectorChoice>().Single(choice => choice.Title == "star.png");
        LiveFormStudio.Press(list, Key.Enter);

        await XamlStudio.UntilAsync(
            () => Element(document).GetAttribute("Source")?.GetValueText() == "/Assets/Icons/star.png", "выбранная картинка не записана");
        await XamlStudio.UntilAsync(() => Shown(studio).Source is Bitmap { PixelSize.Width: 8 }, "форма не показала выбранную картинку");
        await XamlStudio.UntilAsync(() => Field(panel).Text == "star.png", "поле не показало имя картинки");
    }

    /// <summary>
    /// Картинку проекта бросают на поле из окна проекта — ссылкой, — и она пишется; файл, который не ресурс Avalonia,
    /// бросить нельзя, и подсказка тяги говорит почему.
    /// </summary>
    [AvaloniaFact]
    public async Task A_project_image_dropped_on_the_field_is_written_and_a_file_outside_the_resources_is_refused()
    {
        await using var studio = new LiveFormStudio();

        var logo = studio.Xaml.Write("Assets/logo.png", Png(6));
        var shot = studio.Xaml.Write("Docs/shot.png", Png(4));
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Logo]);
        LiveFormStudio.Frame();

        var field = Field(panel);
        var drags = new StudioDrags(() => [studio.Window], studio.Xaml.Log);
        var source = panel.View!;
        var over = field.TranslatePoint(new Point(field.Bounds.Width / 2, field.Bounds.Height / 2), source)!.Value;

        LiveFormStudio.Frame();

        using (var refused = drags.Begin(source, StudioDragData.FromFiles([shot.Value]), DragDropEffects.Copy | DragDropEffects.Link, new StudioDragVisual("shot.png")))
        {
            Assert.Equal(DragDropEffects.None, refused.Over(over, KeyModifiers.None));
            Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, studio.Strings["inspector.image.notResource"], "shot.png"), refused.Hint);
            Assert.False(field.IsDropTarget, "отказ подсветил поле целью");
        }

        using var taken = drags.Begin(source, StudioDragData.FromFiles([logo.Value]), DragDropEffects.Copy | DragDropEffects.Link, new StudioDragVisual("logo.png"));

        Assert.Equal(DragDropEffects.Link, taken.Over(over, KeyModifiers.None));
        Assert.True(field.IsDropTarget, "поле не подсветилось целью");
        Assert.Equal(DragDropEffects.Link, taken.Drop());
        Assert.False(field.IsDropTarget, "подсветка цели осталась после броска");

        await XamlStudio.UntilAsync(
            () => Element(document).GetAttribute("Source")?.GetValueText() == "/Assets/logo.png", "брошенная картинка не записана");
    }

    /// <summary>Крестик снимает картинку: атрибута больше нет, и форма её не показывает.</summary>
    [AvaloniaFact]
    public async Task The_cross_takes_the_image_out()
    {
        await using var studio = new LiveFormStudio();

        studio.Xaml.Write("Assets/logo.png", Png(6));

        var document = await studio.OpenAsync("MainWindow.axaml", Form.Replace("<Image x:Name=\"Logo\"", "<Image x:Name=\"Logo\" Source=\"/Assets/logo.png\"", StringComparison.Ordinal));
        var panel = studio.Inspector();

        document.Select([Logo]);
        LiveFormStudio.Frame();

        var field = Field(panel);

        Assert.Equal("logo.png", field.Text);

        await XamlStudio.UntilAsync(() => field.Thumbnail is Bitmap, "у поля нет миниатюры картинки");

        field.RaiseEvent(new RoutedEventArgs(AxObjectField.ClearedEvent));
        LiveFormStudio.Frame();

        await XamlStudio.UntilAsync(() => Element(document).GetAttribute("Source") is null, "картинка не снята");
        await XamlStudio.UntilAsync(() => Shown(studio).Source is null, "форма показывает снятую картинку");
    }

    /// <summary>
    /// Щелчок мимо строк выбора ничего не выбирает, а выбранная нынешняя картинка второй раз не пишется.
    /// </summary>
    /// <remarks>
    /// Щелчок ловился на всём списке и выбирал выделенное, а выделено нынешнее значение: щелчок по пустому месту или по
    /// полосе прокрутки закрывал выбор и переписывал то же значение лишним шагом истории (запись 396).
    /// </remarks>
    [AvaloniaFact]
    public async Task A_tap_beside_the_rows_chooses_nothing_and_the_current_image_is_not_written_again()
    {
        await using var studio = new LiveFormStudio();

        studio.Xaml.Write("Assets/logo.png", Png(6));

        var document = await studio.OpenAsync(
            "MainWindow.axaml", Form.Replace("<Image x:Name=\"Logo\"", "<Image x:Name=\"Logo\" Source=\"/Assets/logo.png\"", StringComparison.Ordinal));
        var panel = studio.Inspector();

        document.Select([Logo]);
        LiveFormStudio.Frame();

        var field = Field(panel);

        field.Focus();
        LiveFormStudio.Press(field, Key.Enter);
        LiveFormStudio.Frame();

        var list = Picker(studio);

        Assert.Equal("logo.png", Assert.IsType<InspectorChoice>(list.SelectedItem).Title);

        // Щелчок по самому списку — мимо строк: так приходит щелчок по пустому месту и по полосе прокрутки.
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var released = new PointerEventArgs(
            InputElement.PointerReleasedEvent, list, pointer, studio.Window, default, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None);

        list.RaiseEvent(new TappedEventArgs(InputElement.TappedEvent, released));
        LiveFormStudio.Frame();

        Assert.Same(list, Picker(studio));

        LiveFormStudio.Press(list, Key.Enter);
        LiveFormStudio.Frame();

        Assert.False(document.Document!.CanUndo, "нынешняя картинка записана второй раз — шаг истории без перемены");
    }

    /// <summary>Картинка в 6 или 8 точек — PNG, который читает декодер студии.</summary>
    private static byte[] Png(int size)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        using var stream = new MemoryStream();

        bitmap.Save(stream, new PngBitmapEncoderOptions());

        return stream.ToArray();
    }

    private static Image Shown(LiveFormStudio studio) =>
        studio.Window.GetVisualDescendants().OfType<Image>().Single(image => image.Name == "Logo");

    private static AxObjectField Field(InspectorPanel panel)
    {
        var cell = Assert.Single(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == "Source");

        panel.View!.List.ScrollIntoView(cell);
        LiveFormStudio.Frame();

        return Assert.Single(cell.Row.GetVisualDescendants().OfType<AxObjectField>());
    }

    private static AxListBox Picker(LiveFormStudio studio) =>
        Assert.Single(Popup(studio).GetVisualDescendants().OfType<AxListBox>());

    /// <summary>Содержимое открытого выбора — попап безголовой платформы лежит слоем в окне.</summary>
    private static Control Popup(LiveFormStudio studio) =>
        Assert.Single(
            studio.Window.GetVisualDescendants().OfType<AxListBox>(), list => list.ItemsSource is IEnumerable<InspectorChoice>)
            .FindAncestorOfType<DockPanel>()!;

    private static XamlElement Element(LiveFormDocument document) =>
        Logo.Resolve(document.Document!.Syntax) ?? throw new InvalidOperationException("элемента нет в тексте");
}
