using ArxisStudio.Controls;
using ArxisStudio.Settings;
using ArxisStudio.Shell.Localization;
using ArxisStudio.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Страница «Плагины» в окне: список с группами и флажком в строке, подробности справа.
/// </summary>
/// <remarks>
/// Как Plugins → Installed у Rider: Пробел в строке ставит и снимает флажок, флажок идёт через
/// вопрос о зависимых, а заголовок группы — строка, которую нельзя выбрать ни стрелкой, ни набором
/// букв. Модели страницы здесь мало: проверяется то, что человек делает руками.
/// <para>
/// Очередь общая с остальными: подписи берутся из словарей, а <c>Localizer</c> один на процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class SettingsPluginsTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly SettingsHarness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Пробел в строке плагина снимает флажок, строка получает точку, подробности — слова о непринятом.
    /// </summary>
    [AvaloniaFact]
    public async Task Space_on_a_row_switches_the_plugin_and_marks_it_unsaved()
    {
        _harness.Install("arxis.one", "Первый");

        var (owner, settings, shown) = _harness.Open(page: "studio.plugins");
        var page = Page(settings);
        var card = Assert.Single(page.Cards);
        var row = Row(settings, card);

        row.Focus(NavigationMethod.Tab);
        settings.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();

        Assert.False(card.IsOn, "Пробел в строке флажок не снял");
        Assert.True(
            row.GetVisualDescendants().OfType<Ellipse>().Single().IsEffectivelyVisible,
            "у строки с непринятой правкой нет точки");
        Assert.Contains(Localizer.Instance["plugins.pending.off"], Texts(settings));

        await Close(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Флажок того, кем пользуются соседи, спрашивает — и ответ «нет» оставляет флажок стоять.
    /// </summary>
    /// <remarks>
    /// Флажок переключается сам раньше, чем приходит щелчок, а человек может отказаться. Без сверки
    /// после ответа он остался бы снятым у включённого плагина.
    /// </remarks>
    [AvaloniaFact]
    public async Task Declining_the_question_leaves_the_check_box_on()
    {
        _harness.Install("arxis.one", "Первый");
        _harness.Install("arxis.two", "Второй", dependsOn: "arxis.one");

        var (owner, settings, shown) = _harness.Open(page: "studio.plugins");
        var card = Page(settings).Cards.Single(candidate => candidate.Plugin.Id == "arxis.one");
        var box = Row(settings, card).GetVisualDescendants().OfType<AxCheckBox>().Single();
        var at = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), settings)!.Value;

        settings.MouseDown(at, MouseButton.Left);
        settings.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var question = Assert.Single(settings.OwnedWindows.OfType<AxDialog>());

        // Esc на вопросе — это «нет».
        question.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.True(card.IsOn, "отказ выключил плагин");
        Assert.True(box.IsChecked, "после отказа флажок остался снятым");

        await Close(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// «Настройки» в подробностях ведут на страницу настроек плагина, а «назад» возвращает к списку.
    /// </summary>
    /// <remarks>
    /// Встроенный модуль харнесса — единственный в списке: группа встроенных раскрыта, и он выбран
    /// сразу.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_settings_button_opens_the_extension_page_and_back_returns()
    {
        var (owner, settings, shown) = _harness.Open(page: "studio.plugins");
        var model = (SettingsViewModel)settings.DataContext!;

        Assert.Equal("arxis.terminal", Page(settings).Card?.Plugin.Id);

        settings.GetVisualDescendants()
            .OfType<AxButton>()
            .Single(button => Equals(button.Content, Localizer.Instance["plugins.configure"]) && button.IsEffectivelyVisible)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("extension:arxis.terminal", model.Page?.Id);

        model.Back();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("studio.plugins", model.Page?.Id);

        await Close(settings, shown);
        owner.Close();
    }

    /// <summary>Стрелка идёт от плагина к плагину мимо заголовка группы, в обе стороны.</summary>
    [AvaloniaFact]
    public async Task Arrows_pass_the_group_headers()
    {
        _harness.Install("arxis.alpha", "Альфа");
        _harness.Install("arxis.beta", "Бета");

        var (owner, settings, shown) = _harness.Open(page: "studio.plugins");
        var page = Page(settings);
        var beta = page.Cards.Single(card => card.Plugin.Id == "arxis.beta");

        page.Groups.Single(group => group.Key == "builtin").IsExpanded = true;
        page.Selected = beta;
        Dispatcher.UIThread.RunJobs();

        Row(settings, beta).Focus(NavigationMethod.Tab);
        settings.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("arxis.terminal", page.Card?.Plugin.Id);

        settings.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("arxis.beta", page.Card?.Plugin.Id);

        await Close(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Набор букв заголовка группы не выбирает: выбранным остаётся плагин, и в списке тоже.
    /// </summary>
    /// <remarks>
    /// «В» ведёт к «Встроенным» — заголовку, который стоит сразу за выбранной «Бетой». Выбрать его
    /// нельзя, и список возвращает выбор плагину.
    /// </remarks>
    [AvaloniaFact]
    public async Task Typing_a_letter_does_not_choose_a_group_header()
    {
        _harness.Install("arxis.alpha", "Альфа");
        _harness.Install("arxis.beta", "Бета");

        var (owner, settings, shown) = _harness.Open(page: "studio.plugins");
        var page = Page(settings);
        var beta = page.Cards.Single(card => card.Plugin.Id == "arxis.beta");
        var list = settings.GetVisualDescendants().OfType<AxListBox>().Single(candidate => candidate.Name == "PluginList");

        page.Selected = beta;
        Dispatcher.UIThread.RunJobs();

        Row(settings, beta).Focus(NavigationMethod.Tab);
        settings.KeyTextInput(Localizer.Instance["plugins.group.builtin"][..1]);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(beta, page.Card);
        Assert.Same(beta, list.SelectedItem);

        await Close(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Группа, свёрнутая щелчком по заголовку, оставляет каретку на нём — и разворачивается снова.
    /// </summary>
    /// <remarks>
    /// Заголовок берёт каретку, когда его щёлкают. Список, собранный после этого заново, заменил бы
    /// его строку новой, и каретка осталась бы ни на чём. Строки правятся на месте, и заголовок — тот
    /// же контрол.
    /// </remarks>
    [AvaloniaFact]
    public async Task Folding_a_group_keeps_the_caret_on_its_header()
    {
        _harness.Install("arxis.one", "Первый");

        var (owner, settings, shown) = _harness.Open(page: "studio.plugins");
        var page = Page(settings);
        var group = page.Groups.Single(candidate => candidate.Key == "external");
        var header = settings.GetVisualDescendants().OfType<AxGroupHeader>().Single(candidate => ReferenceEquals(candidate.DataContext, group));
        var at = header.TranslatePoint(new Point(header.Bounds.Width / 4, header.Bounds.Height / 2), settings)!.Value;

        settings.MouseDown(at, MouseButton.Left);
        settings.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.False(group.IsExpanded, "щелчок по заголовку группу не свернул");
        Assert.DoesNotContain(page.Cards[0], page.Rows);
        Assert.Same(header, settings.FocusManager?.GetFocusedElement());

        settings.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();

        Assert.True(group.IsExpanded, "Пробел на заголовке группу не развернул");
        Assert.Contains(page.Cards[0], page.Rows);

        await Close(settings, shown);
        owner.Close();
    }

    /// <summary>Страница плагинов, открытая в окне.</summary>
    private static PluginsPage Page(SettingsWindow settings) =>
        Assert.IsType<PluginsPage>(((SettingsViewModel)settings.DataContext!).Page);

    /// <summary>Строка списка, в которой стоит плагин.</summary>
    private static AxListBoxItem Row(SettingsWindow settings, PluginCard card) =>
        settings.GetVisualDescendants().OfType<AxListBoxItem>().Single(row => ReferenceEquals(row.DataContext, card));

    /// <summary>Строки текста, которые окно показывает сейчас.</summary>
    private static List<string?> Texts(SettingsWindow settings) =>
        [.. settings.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text)];

    private static async Task Close(SettingsWindow settings, Task shown)
    {
        settings.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Same(shown, await Task.WhenAny(shown, Task.Delay(Patience)));
    }
}
