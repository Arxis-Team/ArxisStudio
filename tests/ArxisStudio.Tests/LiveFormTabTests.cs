using ArxisStudio.Controls;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using SurfaceLayout = ArxisStudio.Surface.UiDesigner.Layout;

namespace ArxisStudio.Tests;

/// <summary>
/// Живая вкладка формы: форма, построенная службой XAML, на холсте дизайнера и её XAML рядом — правка
/// жестом, отмена, удаление, выбор в обе стороны, клавиатура, автосохранение, чужая запись, замена типов.
/// </summary>
/// <remarks>
/// Формы стоят на встроенных контролах: поколению хватает Avalonia самой студии, сборки проекта не нужны.
/// Темы приложения у решения нет, и шаблонный контрол без своего шаблона — нулевой высоты; выбрать на
/// холсте можно то, у чего есть рамка, поэтому размеры у контролов формы объявлены.
/// Жест размера — клавиатурный (<c>Alt</c> + стрелка): это та же единица правки, что у ручки, и та же
/// дорога в документ, а безголовой мыши ручка выбранного не досталась бы без кадра раскладки адорнеров.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class LiveFormTabTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
          <StackPanel x:Name="Panel">
            <Button x:Name="Go" Content="Пуск" Width="120" MinHeight="30" />
            <TextBox x:Name="Input" Text="поле" Width="200" Height="24" />
            <TextBlock Text="Подпись" />
          </StackPanel>
        </Window>
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Форма открывается живой: корень — окно, построенное службой, его содержимое предложено к выбору, а
    /// XAML рядом показывает текст файла с ролями.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_opens_live_on_the_canvas_with_its_xaml_beside_it()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);

        Assert.Equal("MainWindow.axaml", document.Title);
        Assert.IsType<Window>(document.Form.Root);
        Assert.False(document.IsModified);
        Assert.Equal(XamlDesignState.Live, studio.Xaml.Design.State);
        Assert.Equal(studio.Strings["form.state.live"], document.View.State.Content);
        Assert.False(document.View.Notice.IsVisible, "живая форма не должна просить внимания");

        var declared = document.Shown!.GetDeclaredObjects().OfType<Control>().ToList();

        Assert.Contains(declared, control => control is Button);
        Assert.All(declared, control => Assert.True(SurfaceLayout.GetIsTracked(control), $"{control.GetType().Name} не предложен к выбору"));

        Assert.Equal(Form, document.View.Code.Text);
        Assert.NotEmpty(document.View.Code.Spans ?? []);
        Assert.Equal(LiveFormDocument.FormViewMode.Split, document.Mode);
        Assert.True(document.View.Sheet.IsVisible && document.View.Code.IsVisible, "разделение показывает не холст и XAML");
    }

    /// <summary>
    /// Встав, форма вписывается в холст целиком — с заголовком окна над карточкой: до корня карточка
    /// стояла объявленным размером без него.
    /// </summary>
    [AvaloniaFact]
    public async Task The_form_is_framed_with_its_title_bar_once_it_stands()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form.Replace("Height=\"300\"", "Height=\"1000\"", StringComparison.Ordinal));
        var sheet = document.View.Sheet;
        var title = SheetControls.LengthOf(document.View, "UiDesigner.Form.TitleBar.Height");

        LiveFormStudio.Frame();

        Assert.True(title > 0, "у темы дизайнера нет высоты заголовка окна");
        Assert.True(document.Form.IsTopLevel, "окно не встало окном");

        // Высокая форма вписывается по высоте, и середина кадра — середина того, что вписали.
        var middle = sheet.ViewportLocation.Y + sheet.Bounds.Height / sheet.ViewportZoom / 2;

        Assert.Equal((document.Form.Bounds.Height - title) / 2, middle, 0);
        Assert.True(sheet.ViewportZoom < 1, "высокая форма не ужалась в холст");
    }

    /// <summary>
    /// Размер, изменённый жестом, ложится в текст одним шагом истории — только сменившаяся сторона, — вкладка
    /// метит несохранённое, а Ctrl+Z на холсте отменяет шаг.
    /// </summary>
    [AvaloniaFact]
    public async Task A_resize_writes_the_size_and_ctrl_z_takes_it_back()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var sheet = document.View.Sheet;
        var button = GoButton(document);

        Assert.True(sheet.SelectTarget(button), "кнопку не выбрать");

        sheet.Focus();
        LiveFormStudio.Press(sheet, Key.Right, KeyModifiers.Alt);

        await XamlStudio.UntilAsync(() => Text(document).Contains("Width=\"121\"", StringComparison.Ordinal), "ширина не записана");

        Assert.Null(Element(document, "Go").GetAttribute("Height"));
        Assert.True(document.IsModified, "вкладка не метит несохранённое");
        Assert.Equal(1, Element(document, "Go").Attributes.Count(attribute => attribute.Name.LocalName == "Width"));

        LiveFormStudio.Press(sheet, Key.Z, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Text(document) == Form, "отмена не вернула текст");

        Assert.False(document.IsModified, "отменённая до сохранённого форма всё ещё метится изменённой");
    }

    /// <summary>Delete убирает элемент из текста, а выбор переходит к тому, в чём он стоял.</summary>
    [AvaloniaFact]
    public async Task Delete_takes_the_element_out_of_the_text()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var sheet = document.View.Sheet;

        Assert.True(sheet.SelectTarget(GoButton(document)));

        sheet.Focus();
        LiveFormStudio.Press(sheet, Key.Delete);

        await XamlStudio.UntilAsync(() => !Text(document).Contains("x:Name=\"Go\"", StringComparison.Ordinal), "кнопка осталась в тексте");
        await XamlStudio.UntilAsync(() => document.Selection.Count == 1 && document.Selection[0].Equals(PathTo(document, "Panel")), "выбор не перешёл к панели");

        LiveFormStudio.Frame();

        var panel = Assert.IsType<StackPanel>(document.Shown!.ObjectAt(PathTo(document, "Panel")));

        Assert.Same(panel, sheet.SelectedTargets.Single().Target);
        Assert.Equal(Range(document, "Panel"), document.View.Code.Highlight);
    }

    /// <summary>
    /// Содержимое окна, переписанное правкой, встаёт на холст: на время записи карточка отдаёт окну то,
    /// что взяла, и берёт заново то, что в нём стало.
    /// </summary>
    [AvaloniaFact]
    public async Task The_window_content_rewritten_by_an_edit_shows_on_the_canvas()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var sheet = document.View.Sheet;
        var panel = Assert.IsType<StackPanel>(document.Shown!.ObjectAt(PathTo(document, "Panel")));

        Assert.True(document.Form.IsVisualAncestorOf(panel), "панели формы нет на холсте");
        Assert.True(sheet.SelectTarget(panel));

        sheet.Focus();
        LiveFormStudio.Press(sheet, Key.Delete);

        await XamlStudio.UntilAsync(() => !Text(document).Contains("StackPanel", StringComparison.Ordinal), "панель осталась в тексте");
        LiveFormStudio.Frame();

        Assert.False(document.Form.IsVisualAncestorOf(panel), "удалённая панель осталась на холсте");
        Assert.Null(Assert.IsType<Window>(document.Form.Root).Content);
        Assert.Equal(XamlElementPath.Root, Assert.Single(document.Selection));
        Assert.Same(document.Form, sheet.SelectedTargets.Single().Target);
    }

    /// <summary>
    /// Пока на холсте идёт жест, замена типов ждёт его конца; перестановка в панели потока ложится в текст
    /// переносом элемента, и выбор идёт за ним.
    /// </summary>
    [AvaloniaFact]
    public async Task A_gesture_on_the_canvas_holds_the_swap_and_a_reorder_moves_the_element()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var sheet = document.View.Sheet;
        var button = GoButton(document);

        Assert.True(sheet.SelectTarget(button));
        LiveFormStudio.Frame();

        var from = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), studio.Window)!.Value;

        // Как у ядра в его тестах: шаг за порог начинает жест, следующий ведёт точку вставки.
        studio.Window.MouseMove(from);
        studio.Window.MouseDown(from, MouseButton.Left);
        studio.Window.MouseMove(from + new Point(4, 6));
        studio.Window.MouseMove(from + new Point(0, 40));
        LiveFormStudio.Frame();

        Assert.True(sheet.IsInteracting, "жест не начался");
        Assert.Contains(studio.Xaml.Session.Host.Gate.Reasons, reason => reason.Contains("MainWindow.axaml", StringComparison.Ordinal));

        studio.Window.MouseUp(from + new Point(0, 40), MouseButton.Left);
        LiveFormStudio.Frame();

        Assert.False(sheet.IsInteracting);
        Assert.DoesNotContain(studio.Xaml.Session.Host.Gate.Reasons, reason => reason.Contains("MainWindow.axaml", StringComparison.Ordinal));

        // Кнопку унесли ниже поля: она встала за ним — в тексте, а не только на холсте.
        await XamlStudio.UntilAsync(
            () => Text(document).IndexOf("x:Name=\"Go\"", StringComparison.Ordinal) > Text(document).IndexOf("x:Name=\"Input\"", StringComparison.Ordinal),
            "перестановка не легла в текст");
        await XamlStudio.UntilAsync(() => sheet.SelectedTargets.Count == 1 && ReferenceEquals(sheet.SelectedTargets[0].Target, GoButton(document)), "выбор не пошёл за кнопкой");

        Assert.Equal(XamlElementPath.Parse("/0/1"), Assert.Single(document.Selection));
        Assert.Equal(PathTo(document, "Go"), document.Selection[0]);
    }

    /// <summary>
    /// Выбранное на холсте отмечено в XAML от открывающего тега до закрывающего, а каретка, поставленная в
    /// XAML, выбирает на холсте элемент под собой.
    /// </summary>
    [AvaloniaFact]
    public async Task The_xaml_marks_the_selection_and_a_caret_in_it_selects_on_the_canvas()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var sheet = document.View.Sheet;
        var code = document.View.Code;
        var input = Assert.IsType<TextBox>(document.Shown!.ObjectAt(PathTo(document, "Input")));

        Assert.True(sheet.SelectTarget(input));
        Assert.Equal(Range(document, "Input"), code.Highlight);

        var inside = Text(document).IndexOf("Content=\"Пуск\"", StringComparison.Ordinal);

        code.Focus();
        code.CaretOffset = inside;
        LiveFormStudio.Press(code, Key.Right);

        Assert.Equal(inside + 1, code.CaretOffset);
        Assert.Same(GoButton(document), sheet.SelectedTargets.Single().Target);
        Assert.Equal(Range(document, "Go"), code.Highlight);
        Assert.Equal(PathTo(document, "Go"), Assert.Single(document.Selection));

        // Холст выбор только показывает: следующая стрелка — каретки в коде, а не сдвиг выбранного.
        Assert.True(code.IsKeyboardFocusWithin, "каретка в XAML увела клавиатуру на холст");
    }

    /// <summary>
    /// Клавиатура в форму не заходит: Tab с холста проходит мимо её полей к XAML, а фокус, который
    /// контрол формы берёт сам, отменяется.
    /// </summary>
    [AvaloniaFact]
    public async Task Tab_from_the_canvas_does_not_walk_into_the_form()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var sheet = document.View.Sheet;
        var input = Assert.IsType<TextBox>(document.Shown!.ObjectAt(PathTo(document, "Input")));
        var reached = false;

        sheet.Focus();

        for (var step = 0; step < 12 && !reached; step++)
        {
            studio.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            studio.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();

            var focused = studio.Window.FocusManager?.GetFocusedElement();

            Assert.False(InForm(document, focused), $"Tab на шаге {step} зашёл в форму");
            reached = ReferenceEquals(focused, document.View.Code);
        }

        // Мимо, а не поперёк: запертая форма оставляла бы каретку на холсте, и до XAML Tab не дошёл бы.
        Assert.True(reached, "Tab с холста не дошёл до XAML");

        input.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.False(InForm(document, studio.Window.FocusManager?.GetFocusedElement()), "поле формы взяло фокус само");
    }

    /// <summary>
    /// Ушли из окна студии — форма сохраняется сама, через службу файлов: на диске правка, отметки нет.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_saves_itself_when_the_studio_window_loses_focus()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);

        await WidenAsync(document);

        studio.Deactivate();

        await XamlStudio.UntilAsync(() => !document.IsModified, "форма не сохранилась при уходе из окна");

        Assert.Contains("Width=\"121\"", File.ReadAllText(studio.Xaml.PathOf("MainWindow.axaml").Value), StringComparison.Ordinal);
    }

    /// <summary>После паузы в правках форма сохраняется сама, а выключенное автосохранение её не трогает.</summary>
    [AvaloniaFact]
    public async Task A_form_saves_itself_after_a_pause_unless_told_not_to()
    {
        await using var studio = new LiveFormStudio(autoSave: TimeSpan.FromMilliseconds(30));
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var file = studio.Xaml.PathOf("MainWindow.axaml").Value;

        await WidenAsync(document);
        await XamlStudio.UntilAsync(() => !document.IsModified, "форма не сохранилась после паузы");

        Assert.Contains("Width=\"121\"", File.ReadAllText(file), StringComparison.Ordinal);

        studio.Context.Settings.Set(UiDesignerModule.AutoSaveKey, false);

        await WidenAsync(document, "122");
        await Task.Delay(200, Token);
        Dispatcher.UIThread.RunJobs();
        studio.Deactivate();

        Assert.True(document.IsModified, "форма сохранилась, хотя самой сохраняться ей не велено");
        Assert.Contains("Width=\"121\"", File.ReadAllText(file), StringComparison.Ordinal);

        Assert.True(await document.SaveAsync(), "Ctrl+S не сохранил");
        Assert.Contains("Width=\"122\"", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.False(document.IsModified);
    }

    /// <summary>
    /// Файл переписали на диске поверх несохранённого — баннер спрашивает; «Оставить мои» оставляет правку,
    /// и сохранение пишет её поверх файла.
    /// </summary>
    [AvaloniaFact]
    public async Task A_file_written_over_unsaved_edits_asks_on_a_banner()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var file = studio.Xaml.PathOf("MainWindow.axaml").Value;

        await WidenAsync(document);

        File.WriteAllText(file, Form.Replace("Пуск", "Rider", StringComparison.Ordinal));

        await XamlStudio.UntilAsync(() => document.View.Conflict.IsVisible, "баннер чужой записи не показан");

        Assert.Contains("MainWindow.axaml", document.View.ConflictText.Text, StringComparison.Ordinal);
        Assert.True(document.Document!.HasConflict);

        studio.Deactivate();

        // Вопрос ждёт ответа: уход из окна не пишет поверх чужого и не жалуется на отказ.
        Assert.Contains("Rider", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Empty(studio.Xaml.Status.Said);

        // Крестик откладывает ответ, а не снимает вопрос: сохранение без ответа отказывает и возвращает
        // его. Каретка, ушедшая с крестиком, возвращается вкладке.
        Close(document.View.Conflict);

        Assert.False(document.View.Conflict.IsVisible);
        Assert.True(document.View.Sheet.IsFocused, "каретка ушла вместе с крестиком");
        Assert.False(await document.SaveAsync(), "сохранение прошло без ответа на вопрос");
        Assert.True(document.View.Conflict.IsVisible, "сохранение не вернуло вопрос");
        Assert.Contains("Rider", File.ReadAllText(file), StringComparison.Ordinal);

        document.View.KeepMine.Focus();
        document.View.KeepMine.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        await XamlStudio.UntilAsync(() => !document.View.Conflict.IsVisible, "баннер не ушёл после ответа");

        Assert.True(document.View.Sheet.IsFocused, "каретка ушла вместе с нажатой кнопкой баннера");
        Assert.True(document.IsModified);
        Assert.True(await document.SaveAsync());
        Assert.Contains("Width=\"121\"", File.ReadAllText(file), StringComparison.Ordinal);
    }

    /// <summary>
    /// Замена поколения: вкладка отдаёт всё, что построено из уходящих типов, — поколение уходит, — а после
    /// неё на холсте новая форма и выбран тот же элемент.
    /// </summary>
    [AvaloniaFact]
    public async Task A_swap_lets_the_generation_go_and_brings_the_selection_back()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var sheet = document.View.Sheet;

        Assert.True(sheet.SelectTarget(GoButton(document)));

        var before = RootOf(document);
        var report = await studio.Xaml.Session.Host.SwapAsync("проверка вкладки", Token);

        Assert.True(report.Reclaimed, report.ToString());
        Assert.False(before.IsAlive, "прежняя форма пережила замену поколения");

        await XamlStudio.UntilAsync(() => document.Form.Root is Window, "новая форма не встала");
        LiveFormStudio.Frame();
        await XamlStudio.UntilAsync(() => sheet.SelectedTargets.Count == 1, "выбор не вернулся");

        Assert.False(sheet.IsFrozen, "холст остался стоп-кадром");
        Assert.Same(GoButton(document), sheet.SelectedTargets[0].Target);
        Assert.Equal(PathTo(document, "Go"), Assert.Single(document.Selection));
    }

    /// <summary>
    /// Форма носит тему, которую просит её приложение, а не студийную, и идёт за правкой <c>App.axaml</c>;
    /// приложение, сказавшее «по умолчанию», показывается в теме платформы.
    /// </summary>
    [AvaloniaFact]
    public async Task The_form_wears_the_theme_its_application_asks_for()
    {
        await using var studio = new LiveFormStudio();

        studio.Xaml.Write("App.axaml", ApplicationMarkup("Light"));

        var document = await studio.OpenAsync("MainWindow.axaml", Form);

        await XamlStudio.UntilAsync(() => document.Form.ApplicationRoot is not null, "приложение формы не встало");

        Assert.Equal(ThemeVariant.Light, document.Form.ApplicationThemeVariant);

        studio.Xaml.Write("App.axaml", ApplicationMarkup("Dark"));

        await XamlStudio.UntilAsync(
            () => document.Form.ApplicationThemeVariant == ThemeVariant.Dark, "правка App.axaml не дошла до темы формы");

        var platform = Avalonia.Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark
            ? ThemeVariant.Dark
            : ThemeVariant.Light;

        studio.Xaml.Write("App.axaml", ApplicationMarkup("Default"));

        await XamlStudio.UntilAsync(
            () => document.Form.ApplicationThemeVariant == platform, "«по умолчанию» показано не темой платформы");
    }

    /// <summary>Вид вкладки выбирают на полосе: дизайн, XAML, разделение; выбранный — вид следующих вкладок.</summary>
    [AvaloniaFact]
    public async Task The_view_switches_and_the_next_tab_opens_the_same()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var view = document.View;

        view.Mode.SelectedIndex = (int)LiveFormDocument.FormViewMode.Xaml;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(LiveFormDocument.FormViewMode.Xaml, document.Mode);
        Assert.False(view.Sheet.IsVisible);
        Assert.True(view.Code.IsVisible);
        Assert.False(view.Split.IsVisible, "граница стоит, когда разделять нечего");
        Assert.Same(view.Code, document.FocusTarget);
        Assert.Equal("xaml", studio.Context.Settings.Get<string>(UiDesignerModule.ViewKey));

        view.Mode.SelectedIndex = (int)LiveFormDocument.FormViewMode.Design;
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.Sheet.IsVisible);
        Assert.False(view.Code.IsVisible);
        Assert.Same(view.Sheet, document.FocusTarget);

        var next = await studio.OpenAsync("Other.axaml", Form);

        Assert.Equal(LiveFormDocument.FormViewMode.Design, next.Mode);
    }

    /// <summary>
    /// Живой вкладка бывает только у формы открытого решения: файл вне его дизайнер показывает рамкой, как
    /// до службы XAML.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_outside_the_solution_opens_as_its_frame()
    {
        await using var studio = new LiveFormStudio();

        studio.Xaml.Write("MainWindow.axaml", Form);
        await studio.Xaml.OpenAsync();

        var outside = Path.Combine(studio.Xaml.Root, "Elsewhere.axaml");

        File.WriteAllText(outside, Form);

        var (view, error) = await studio.Editor().OpenAsync(outside);

        Assert.Null(error);
        Assert.IsType<FormDocument>(view);

        await view!.DisposeAsync();
    }

    /// <summary>
    /// Ключи ядра и дизайнера, которые студия перекрашивает токенами, есть в их темах: переименованный
    /// ключ молча оставил бы цвет библиотеки.
    /// </summary>
    [AvaloniaFact]
    public void Every_canvas_key_the_studio_recolours_is_in_the_canvas_theme()
    {
        var view = new LiveFormView();
        var window = new Window { Content = view };

        window.Show();

        try
        {
            foreach (var (key, token) in CanvasLooks.Map)
            {
                Assert.True(view.Sheet.TryFindResource(key, view.Sheet.ActualThemeVariant, out _), $"у холста нет ключа {key}");
                Assert.True(view.Sheet.TryFindResource(token, view.Sheet.ActualThemeVariant, out _), $"у темы студии нет токена {token}");
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Удлиняет кнопку на шаг жестом с клавиатуры и ждёт, пока правка ляжет в текст.</summary>
    private static async Task WidenAsync(LiveFormDocument document, string expected = "121")
    {
        var sheet = document.View.Sheet;

        Assert.True(sheet.SelectTarget(GoButton(document)), "кнопку не выбрать");

        sheet.Focus();
        LiveFormStudio.Press(sheet, Key.Right, KeyModifiers.Alt);

        await XamlStudio.UntilAsync(
            () => Text(document).Contains($"Width=\"{expected}\"", StringComparison.Ordinal),
            $"ширина {expected} не записана");
    }

    /// <summary>Закрывает баннер крестиком — кнопкой его шаблона, взявшей каретку нажатием.</summary>
    private static void Close(AxBanner banner)
    {
        var close = banner.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_Close");

        close.Focus();
        close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Приложение решения без стилей — только тема, которую оно просит.</summary>
    private static string ApplicationMarkup(string variant) => $"""
        <Application xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     RequestedThemeVariant="{variant}" />
        """;

    private static Button GoButton(LiveFormDocument document) =>
        Assert.IsType<Button>(document.Shown!.ObjectAt(PathTo(document, "Go")));

    private static string Text(LiveFormDocument document) => document.Document!.Syntax.SourceText.ToString();

    private static XamlElement Element(LiveFormDocument document, string name) =>
        document.Document!.Syntax.Root!.DescendantElements().Single(element => element.Identity == name);

    private static XamlElementPath PathTo(LiveFormDocument document, string name) =>
        XamlElementPath.Of(Element(document, name));

    private static AxCodeRange Range(LiveFormDocument document, string name)
    {
        var element = Element(document, name);

        return new AxCodeRange(element.Span.Start, element.Span.Length);
    }

    private static bool InForm(LiveFormDocument document, IInputElement? focused) =>
        focused is Visual visual && document.Form.IsVisualAncestorOf(visual);

    /// <summary>Слабая ссылка на корень: держать его через замену — держать поколение.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference RootOf(LiveFormDocument document) => new(document.Form.Root);
}
