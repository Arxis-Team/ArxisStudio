using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.UiDesigner;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Model;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using static ArxisStudio.Tests.UiDesignerStudio;

namespace ArxisStudio.Tests;

/// <summary>
/// Режимы дизайнера: во вкладках форма открывается своей вкладкой с холстом на одну неё, а на доске —
/// показывается на доске, выбранной целиком и в кадре; файл вне решения на доске открывает текст.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class UiDesignerFormTabTests
{
    private const string Styles = "<Styles xmlns='https://github.com/avaloniaui'/>";

    /// <summary>
    /// На доске дизайнер берёт формы открытого решения — их он покажет на доске; файл вне решения доска не
    /// знает, и его, как прежде, открывает просмотрщик разметки.
    /// </summary>
    [AvaloniaFact]
    public async Task On_the_board_the_designer_takes_the_solutions_forms_and_leaves_the_rest_to_the_text_view()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(("Views/MainWindow.axaml", WindowXaml("MainWindow"))));
        studio.Settings.Set(UiDesignerModule.TabsKey, false);

        var outside = Path.Combine(studio.Root, "Elsewhere.axaml");

        File.WriteAllText(outside, WindowXaml("Elsewhere"));

        Assert.True(studio.Editor().CanOpen(studio.PathOf("Views/MainWindow.axaml").Value), "форму решения доска не взяла");
        Assert.False(studio.Editor().CanOpen(outside), "файл вне решения взят на доску");
        Assert.False(await studio.Editor().RevealAsync(outside), "файл вне решения показан на доске");
    }

    /// <summary>
    /// Форма, открытая в режиме доски, показывается на доске — выбранной целиком и в кадре, — а не
    /// вкладкой.
    /// </summary>
    [AvaloniaFact]
    public async Task On_the_board_an_opened_form_is_shown_on_the_board_whole_and_framed()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(
            ("Views/MainWindow.axaml", WindowXaml("MainWindow")),
            ("Views/Card.axaml", ControlXaml("Card"))));
        studio.Settings.Set(UiDesignerModule.TabsKey, false);

        var card = studio.Card("Card.axaml");
        var sheet = studio.View.Sheet;

        sheet.ViewportLocation = new Avalonia.Point(100_000, 100_000);

        Assert.True(await studio.Editor().RevealAsync(card.Path.Value), "форма не показана на доске");

        UiDesignerStudio.Frame();

        var item = studio.Container(card);
        var shown = new Avalonia.Rect(sheet.ViewportLocation, sheet.Bounds.Size / sheet.ViewportZoom);

        Assert.Same(item, Assert.Single(sheet.SelectedTargets).Target);
        Assert.True(shown.Contains(new Avalonia.Rect(card.Location, item.Bounds.Size)), $"форма не в кадре: {shown}");
    }

    /// <summary>
    /// Убранная с доски форма, открытая в режиме доски, возвращается на доску — записью истории, как из
    /// меню: Ctrl+Z уберёт её снова.
    /// </summary>
    [AvaloniaFact]
    public async Task On_the_board_opening_a_removed_form_brings_it_back()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(
            ("Views/MainWindow.axaml", WindowXaml("MainWindow")),
            ("Views/Card.axaml", ControlXaml("Card"))));
        studio.Settings.Set(UiDesignerModule.TabsKey, false);

        var path = studio.PathOf("Views/Card.axaml");

        studio.Model.Remove([studio.Card("Card.axaml")]);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(studio.Model.Cards, card => card.Path == path);
        Assert.True(await studio.Editor().RevealAsync(path.Value), "убранная форма не показана");
        Assert.Contains(studio.Model.Cards, card => card.Path == path);

        // Вернувшаяся — ещё и выбрана: её контейнер свежий, и выбрать его можно только разложенным.
        Assert.Same(studio.Container(studio.Card("Card.axaml")), Assert.Single(studio.View.Sheet.SelectedTargets).Target);

        Assert.True(studio.Panel.History!.Undo(), "возврат не лёг в историю");
        Assert.DoesNotContain(studio.Model.Cards, card => card.Path == path);
    }

    /// <summary>Во вкладках форма на доске не показывается: её открывает вкладка.</summary>
    [AvaloniaFact]
    public async Task In_tabs_an_opened_form_is_not_shown_on_the_board()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(("Views/MainWindow.axaml", WindowXaml("MainWindow"))));
        studio.Settings.Set(UiDesignerModule.TabsKey, true);

        Assert.False(await studio.Editor().RevealAsync(studio.PathOf("Views/MainWindow.axaml").Value));
        Assert.Empty(studio.View.Sheet.SelectedTargets);
    }

    /// <summary>
    /// Во вкладках дизайнер берёт окна и элементы, а приложение, словарь стилей, нечитаемую разметку и не
    /// разметку — нет: их открывает текст.
    /// </summary>
    [AvaloniaFact]
    public void In_tabs_the_designer_takes_forms_and_nothing_else()
    {
        using var studio = new UiDesignerStudio();

        studio.Solution(
            ("Views/MainWindow.axaml", WindowXaml("MainWindow")),
            ("Views/Card.axaml", ControlXaml("Card")),
            ("App.axaml", ApplicationXaml),
            ("Styles.axaml", Styles),
            ("Views/Broken.axaml", "<Window Width="),
            ("Views/Notes.txt", WindowXaml("Notes")));
        studio.Settings.Set(UiDesignerModule.TabsKey, true);

        var editor = studio.Editor();

        Assert.True(editor.CanOpen(studio.PathOf("Views/MainWindow.axaml").Value), "окно не открылось вкладкой");
        Assert.True(editor.CanOpen(studio.PathOf("Views/Card.axaml").Value), "элемент не открылся вкладкой");
        Assert.False(editor.CanOpen(studio.PathOf("App.axaml").Value), "приложение открылось формой");
        Assert.False(editor.CanOpen(studio.PathOf("Styles.axaml").Value), "словарь стилей открылся формой");
        Assert.False(editor.CanOpen(studio.PathOf("Views/Broken.axaml").Value), "сломанную разметку не видно текстом");
        Assert.False(editor.CanOpen(studio.PathOf("Views/Notes.txt").Value), "дизайнер взял не разметку");
    }

    /// <summary>
    /// Вкладка подписана именем файла, рамка — ровно объявленного размера, а заголовок окна стоит в
    /// подписи над ней.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_opens_in_its_own_tab_at_its_declared_size()
    {
        using var studio = new UiDesignerStudio();

        studio.Solution(("Views/MainWindow.axaml", WindowXaml("MainWindow", "Width=\"800\" Height=\"450\" Title=\"Main\"")));

        var document = await studio.OpenTabAsync("Views/MainWindow.axaml");
        var frame = Frame(document);

        Assert.Equal("MainWindow.axaml", document.Title);
        Assert.Equal((800d, 450d), (frame.Bounds.Width, frame.Bounds.Height));
        Assert.Equal("MainWindow.axaml · Main", document.Sheet.Caption);
        Assert.Contains("800", document.Sheet.SizeText);
        Assert.Equal(FormKind.Window, document.Sheet.Root.Kind);
    }

    /// <summary>Заголовок, записанный привязкой, — выражение, а не подпись, и в подпись не идёт.</summary>
    [AvaloniaFact]
    public async Task A_bound_title_is_not_shown_as_a_caption()
    {
        using var studio = new UiDesignerStudio();

        studio.Solution(("Views/MainWindow.axaml", WindowXaml("MainWindow", "Title=\"{Binding Title}\"")));

        var document = await studio.OpenTabAsync("Views/MainWindow.axaml");

        Assert.Equal("MainWindow.axaml", document.Sheet.Caption);
    }

    /// <summary>
    /// Форма без объявленного размера рисуется размером из темы — превью шаблонов Avalonia, — и подпись
    /// говорит, что размер не объявлен.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_without_a_size_takes_the_theme_size_and_says_so()
    {
        using var studio = new UiDesignerStudio();

        studio.Solution(("Views/Card.axaml", "<UserControl xmlns='https://github.com/avaloniaui'/>"));

        var document = await studio.OpenTabAsync("Views/Card.axaml");
        var frame = Frame(document);

        Assert.True(studio.Window.TryFindResource("AxFormFrameWidth", studio.Window.ActualThemeVariant, out var width));
        Assert.True(studio.Window.TryFindResource("AxFormFrameHeight", studio.Window.ActualThemeVariant, out var height));
        Assert.Equal((width, height), ((object?)frame.Bounds.Width, (object?)frame.Bounds.Height));
        Assert.Equal(studio.Strings["form.size.none"], document.Sheet.SizeText);
    }

    /// <summary>
    /// Вкладка показывает форму целиком, но не крупнее ста процентов: маленький элемент стоит в
    /// натуральную величину, а большое окно ужимается до холста.
    /// </summary>
    [AvaloniaFact]
    public async Task A_tab_shows_its_form_whole_but_never_larger_than_actual_size()
    {
        using var studio = new UiDesignerStudio();

        studio.Solution(
            ("Views/Small.axaml", WindowXaml("Small", "Width=\"320\" Height=\"200\"")),
            ("Views/Large.axaml", WindowXaml("Large", "Width=\"4000\" Height=\"3000\"")));

        var small = await studio.OpenTabAsync("Views/Small.axaml");

        Assert.Equal(1, small.View.Sheet.ViewportZoom);

        var large = await studio.OpenTabAsync("Views/Large.axaml");

        Assert.InRange(large.View.Sheet.ViewportZoom, 0.01, 0.5);
    }

    /// <summary>
    /// Вернувшись на вкладку, дизайнер перечитывает форму: размер, поменявшийся в другом редакторе,
    /// рамка принимает, а файл, переставший быть формой, она так и называет.
    /// </summary>
    [AvaloniaFact]
    public async Task Showing_the_tab_again_reads_the_form_anew()
    {
        using var studio = new UiDesignerStudio();

        studio.Solution(("Views/MainWindow.axaml", WindowXaml("MainWindow")));

        var document = await studio.OpenTabAsync("Views/MainWindow.axaml");
        var file = studio.PathOf("Views/MainWindow.axaml").Value;

        File.WriteAllText(file, WindowXaml("MainWindow", "Width=\"1024\" Height=\"768\""));
        document.OnActivated();
        Dispatcher.UIThread.RunJobs();

        var frame = Frame(document);

        Assert.Equal((1024d, 768d), (frame.Bounds.Width, frame.Bounds.Height));

        File.WriteAllText(file, Styles);
        document.OnActivated();
        Dispatcher.UIThread.RunJobs();

        Assert.True(document.Sheet.IsUnreadable, "словарь стилей остался формой");
        Assert.Contains("MainWindow.axaml", document.Sheet.Problem);
    }

    /// <summary>
    /// Каретку вкладки студия отдаёт её цели — холсту, а не первой кнопке полосы: F и стрелки работают
    /// сразу. Отдаёт тем же путём, что группа доков при возврате на вкладку: вставив содержимое и ещё до
    /// раскладки.
    /// </summary>
    [AvaloniaFact]
    public async Task The_studio_hands_the_tab_keyboard_to_its_canvas()
    {
        using var studio = new UiDesignerStudio();

        studio.Solution(("Views/MainWindow.axaml", WindowXaml("MainWindow")));

        var (view, _) = await studio.Editor().OpenAsync(studio.PathOf("Views/MainWindow.axaml").Value);
        var document = Assert.IsType<FormDocument>(view);
        var host = new ContentControl();

        // Цель кладут содержимому вкладки, спросив документ при открытии, — как StudioDocuments.
        DockFocus.SetTarget(document.Content, document.FocusTarget);

        studio.Window.Content = host;
        host.Content = document.Content;
        Dispatcher.UIThread.RunJobs();

        // Вкладку закрыли собой другую, а потом выбрали снова — как DockGroupView: содержимое подменено,
        // ребёнок прицеплен, раскладки ещё не было.
        host.Content = null;
        Dispatcher.UIThread.RunJobs();
        host.Content = document.Content;
        host.Presenter?.UpdateChild();

        Assert.True(DockFocus.Restore(document.Content), "вкладке некому отдать каретку");
        Assert.True(document.View.Sheet.IsFocused, "каретка вкладки не на холсте");
    }

    /// <summary>
    /// Сквозь студию: реестр спрашивает дизайнер первым, и тот берёт форму только во вкладках — тогда
    /// студия открывает её вкладкой дизайнера, и целью каретки вкладки становится холст.
    /// </summary>
    [AvaloniaFact]
    public async Task The_studio_opens_a_form_in_a_designer_tab_only_in_tabs()
    {
        using var harness = new StudioPluginsHarness();
        using var studio = new UiDesignerStudio();

        harness.Show();
        harness.Dock.Shown();

        var plugins = harness.Build(modules: [typeof(UiDesignerModule).Assembly]);

        plugins.LoadModules();
        Dispatcher.UIThread.RunJobs();

        studio.Solution(("Views/MainWindow.axaml", WindowXaml("MainWindow")));

        var path = studio.PathOf("Views/MainWindow.axaml").Value;
        var (manifest, _) = ModuleManifest.Load(typeof(UiDesignerModule).Assembly);
        var tabs = manifest!.Contributions.Settings.Single(setting => setting.Key == UiDesignerModule.TabsKey);

        Assert.Null(plugins.Settings.Write("arxis.ui-designer", tabs, false));
        Assert.Null(harness.Contributions.EditorFor(path));

        Assert.Null(plugins.Settings.Write("arxis.ui-designer", tabs, true));

        var match = harness.Contributions.EditorFor(path);

        Assert.IsType<FormEditor>(match?.Editor);
        Assert.Equal("arxis.ui-designer", match?.PluginId);

        await harness.Documents.OpenAsync(path);
        Dispatcher.UIThread.RunJobs();

        var opened = Assert.Single(harness.Documents.Opened);

        Assert.Equal("arxis.ui-designer", opened.PluginId);

        var document = Assert.IsType<FormDocument>(opened.View);

        Assert.Same(document.View.Sheet, DockFocus.GetTarget(document.Content));
    }

    private static Border Frame(FormDocument document) =>
        document.View.Sheet.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Frame");
}
