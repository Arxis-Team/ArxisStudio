using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using ArxisStudio.ViewModels;
using ArxisStudio.Welcome;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Открытие проектов с экрана Welcome: список недавних, поиск, отказы и меню строки.
/// </summary>
/// <remarks>
/// Первая проверка этого экрана вообще. До неё список недавних писался и никем не читался, а
/// единственной дорогой к проекту был аргумент запуска.
/// <para>
/// Очередь общая с остальными: всё здесь читает словари, а <c>Localizer</c> один на процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class WelcomeProjectsTests : IDisposable
{
    private readonly string _root = TempFolder.Create("welcome");
    private readonly StudioPluginsHarness _studio = new();

    private string StateFile => Path.Combine(_root, "recent.json");

    /// <summary>Убирает за собой папку и возвращает студии язык, на котором её застали.</summary>
    public void Dispose()
    {
        _studio.Dispose();
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);

        TempFolder.Erase(_root, strict: true);

        GC.SuppressFinalize(this);
    }

    // ===== Модель: список, поиск, приговор пути =====

    /// <summary>Список показывает то, что запомнил файл, — от свежего к давнему.</summary>
    [Fact]
    public void The_list_shows_what_the_file_remembers()
    {
        var model = Model(Solution("First.sln"), Solution("Second.sln"));

        Assert.Equal(["Second", "First"], model.RecentProjects.Select(project => project.Name));
        Assert.False(model.HasNoRecent, "список не пуст, а экран считает иначе");
    }

    /// <summary>Поиск сужает список и по имени, и по пути.</summary>
    [Fact]
    public void Search_narrows_the_list_by_name_and_by_path()
    {
        var model = Model(Solution("WaveChat.sln"), Solution("Ledger.sln"));

        model.ProjectFilter = "wave";
        Assert.Equal("WaveChat", Assert.Single(model.RecentProjects).Name);

        model.ProjectFilter = Path.GetFileName(_root);
        Assert.Equal(2, model.RecentProjects.Count);
    }

    /// <summary>Поиск, не нашедший ничего, говорит об этом, а не молчит пустым местом.</summary>
    [Fact]
    public void Search_that_matches_nothing_says_so()
    {
        var model = Model(Solution("WaveChat.sln"));

        model.ProjectFilter = "такого проекта нет";

        Assert.Empty(model.RecentProjects);
        Assert.True(model.HasNoRecent, "показывать нечего, а экран этого не сказал");
    }

    /// <summary>Убранная строка уходит и с экрана, и из файла, а проект на диске остаётся.</summary>
    [Fact]
    public void Removing_a_row_drops_it_from_the_list_and_from_the_file()
    {
        var path = Solution("WaveChat.sln");
        var model = Model(path);

        model.Remove(model.RecentProjects[0]);

        Assert.Empty(model.RecentProjects);
        Assert.Empty(new RecentProjects(StateFile).Items);
        Assert.True(File.Exists(path), "убрали из списка, а снесли с диска");
    }

    /// <summary>Пропавший с диска проект остаётся в списке и помечен как пропавший.</summary>
    [Fact]
    public void A_project_whose_file_is_gone_stays_in_the_list()
    {
        var path = Solution("WaveChat.sln");
        var model = Model(path);

        File.Delete(path);
        model.RefreshRecent();

        Assert.False(Assert.Single(model.RecentProjects).Exists, "файла нет, а строка считает иначе");
        Assert.False(model.HasNoRecent, "строка осталась, а экран считает список пустым");
    }

    /// <summary>Не решение и не проект — отказ со словом.</summary>
    [Theory]
    [InlineData("Волна.slnf")]
    [InlineData("Волна.txt")]
    public void Something_that_is_not_a_solution_or_a_project_is_refused(string fileName)
    {
        var model = Model();
        var path = Path.Combine(_root, fileName);

        File.WriteAllText(path, string.Empty);

        Assert.Contains(Localizer.Instance["projects.unsupported"], model.Complaint(path), StringComparison.Ordinal);
    }

    /// <summary>Путь без файла за ним — отказ, и сказано, что файла нет.</summary>
    [Fact]
    public void A_path_with_no_file_behind_it_is_refused()
    {
        var model = Model();
        var path = Path.Combine(_root, "Пропавший.sln");

        Assert.Contains(Localizer.Instance["projects.missing"], model.Complaint(path), StringComparison.Ordinal);
    }

    /// <summary>Настоящему решению возразить нечего.</summary>
    [Fact]
    public void A_real_solution_draws_no_complaint() =>
        Assert.Null(Model().Complaint(Solution("WaveChat.sln")));

    /// <summary>Фильтры диалога выбора проекта названы на языке студии.</summary>
    /// <remarks>
    /// Имя фильтра показывает диалог системы, а первое стояло в коде по-русски и оставалось русским
    /// при английской студии.
    /// </remarks>
    [Fact]
    public void The_picker_filters_are_named_in_the_studio_language()
    {
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);

        Assert.Equal(["Solutions and projects", "All files"], WelcomeWindow.ProjectTypes().Select(type => type.Name));

        Localizer.Instance.SetLanguage("ru");

        Assert.Equal(["Решения и проекты", "Все файлы"], WelcomeWindow.ProjectTypes().Select(type => type.Name));
    }

    // ===== Окно: строка, меню, клавиши =====

    /// <summary>Строка показывает имя, путь и время открытия.</summary>
    [AvaloniaFact]
    public void The_recent_row_shows_the_name_and_the_path()
    {
        var window = Window(out _, Solution("WaveChat.sln"));

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToList();

        Assert.Contains("WaveChat", texts);
        Assert.Contains(texts, text => text is not null && text.EndsWith("WaveChat.sln", StringComparison.Ordinal));
    }

    /// <summary>Пропавший проект носит чип, уцелевший — нет.</summary>
    [AvaloniaFact]
    public void A_missing_project_wears_the_chip()
    {
        var gone = Solution("Пропавший.sln");
        Solution("Живой.sln");
        File.Delete(gone);

        var window = Window(out _);

        var chips = window.GetVisualDescendants().OfType<AxChip>().Where(chip => chip.IsVisible).ToList();

        Assert.Single(chips);
    }

    /// <summary>В меню строки ровно четыре пункта — и те, о которых договорились.</summary>
    [AvaloniaFact]
    public void The_row_menu_has_exactly_four_items()
    {
        var window = Window(out _, Solution("WaveChat.sln"));

        var flyout = Assert.Single(Rows(window).Select(row => row.ContextFlyout).OfType<AxMenuFlyout>());
        var headers = flyout.Items.OfType<MenuItem>().Select(item => item.Header).ToList();

        Assert.Equal(
            [
                Localizer.Instance["projects.open"],
                Localizer.Instance["projects.reveal"],
                Localizer.Instance["projects.copypath"],
                Localizer.Instance["projects.remove"],
            ],
            headers);
    }

    /// <summary>
    /// Кнопка меню на строке прячется, пока строка не под курсором и не в фокусе.
    /// </summary>
    /// <remarks>
    /// Место под неё занято всегда — иначе дата прыгала бы под курсором, — а прячется сама кнопка,
    /// а не её прозрачность: прозрачная ловила бы щелчки, которых ей не адресовали.
    /// </remarks>
    [AvaloniaFact]
    public void The_row_menu_button_shows_itself_only_on_the_row_in_hand()
    {
        var window = Window(out _, Solution("WaveChat.sln"));
        var row = Assert.Single(Rows(window));
        var more = window.GetVisualDescendants().OfType<AxButton>()
            .Single(button => button.Classes.Contains("more"));

        Assert.False(more.IsVisible, "кнопка меню видна на строке, которой никто не касался");

        row.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.True(more.IsVisible, "строка в фокусе, а кнопки меню нет");
    }

    /// <summary>Кнопка меню раскрывает то же меню, что и правый щелчок.</summary>
    [AvaloniaFact]
    public void The_row_menu_button_opens_the_same_menu()
    {
        var window = Window(out _, Solution("WaveChat.sln"));
        var row = Assert.Single(Rows(window));
        var flyout = Assert.IsType<AxMenuFlyout>(row.ContextFlyout);

        row.Focus();
        Dispatcher.UIThread.RunJobs();

        var more = window.GetVisualDescendants().OfType<AxButton>()
            .Single(button => button.Classes.Contains("more"));

        Press(window, more, MouseButton.Left);

        Assert.True(flyout.IsOpen, "кнопка меню его не раскрыла");
    }

    /// <summary>Щелчок по строке просит студию открыть именно этот проект.</summary>
    [AvaloniaFact]
    public void Clicking_a_row_asks_the_studio_to_open_that_project()
    {
        var path = Solution("WaveChat.sln");
        var window = Window(out _, path);

        string? asked = null;
        window.ProjectRequested += (_, requested) => asked = requested;

        Press(window, Rows(window)[0], MouseButton.Left);

        Assert.Equal(path, asked);
    }

    // Проверки «правая кнопка не открывает проект» здесь нет намеренно. Обработчик строки кнопку
    // проверяет — на строке висит меню, и без проверки правый щелчок открывал бы проект вместе с
    // ним, — но безголовый ввод правое нажатие до строки не доводит вовсе: строка не получает даже
    // фокуса, который обработчик ставит обеими кнопками. Написанный тест прошёл бы и со снятой
    // проверкой — это проверено снятием, — а такой тест хуже, чем никакого. Дорога остаётся живой.

    /// <summary>Delete убирает строку, на которой стоит фокус.</summary>
    [AvaloniaFact]
    public void Delete_takes_the_focused_row_out_of_the_list()
    {
        var window = Window(out var model, Solution("WaveChat.sln"));
        var row = Assert.Single(Rows(window));

        Assert.Single(model.RecentProjects);

        Assert.True(row.Focus(), "строка не берёт фокус — Delete будет некуда нажимать");
        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, string.Empty);

        Assert.Empty(model.RecentProjects);
    }

    /// <summary>Enter открывает проект строки, на которой стоит фокус, — как щелчок.</summary>
    /// <remarks>
    /// Строка брала фокус ради Delete, а открыть проект с клавиатуры было нельзя: Enter не делал
    /// ничего, и оставалась дорога через меню строки.
    /// </remarks>
    [AvaloniaFact]
    public void Enter_on_the_focused_row_asks_the_studio_to_open_that_project()
    {
        var path = Solution("WaveChat.sln");
        var window = Window(out _, path);

        string? asked = null;
        window.ProjectRequested += (_, requested) => asked = requested;

        Assert.True(Assert.Single(Rows(window)).Focus(), "строка не берёт фокус — Enter будет некуда нажимать");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);

        Assert.Equal(path, asked);
    }

    /// <summary>
    /// Экранному диктору строка — элемент списка с именем проекта и путём подсказкой.
    /// </summary>
    /// <remarks>
    /// Строка — рамка с текстами внутри, и фокус на ней был для диктора нем: ни роли, ни имени.
    /// </remarks>
    [AvaloniaFact]
    public void A_recent_row_reads_as_its_project()
    {
        var path = Solution("WaveChat.sln");
        var window = Window(out _, path);
        var row = ControlAutomationPeer.CreatePeerForElement(Assert.Single(Rows(window)));

        Assert.Equal(AutomationControlType.ListItem, row.GetAutomationControlType());
        Assert.Equal("WaveChat", row.GetName());
        Assert.Equal(path, row.GetHelpText());
        Assert.True(row.IsKeyboardFocusable(), "строку нельзя выбрать с клавиатуры");
    }

    /// <summary>
    /// «Каркас студии» назван для диктора тем, что на нём написано, а пояснение — подсказкой.
    /// </summary>
    /// <remarks>
    /// Имя было пояснением, и человек, который говорит программе голосом «нажми Каркас студии»,
    /// кнопки с таким именем не находил: доступное имя обязано содержать видимую надпись.
    /// </remarks>
    [AvaloniaFact]
    public void The_skeleton_button_is_named_by_what_it_says()
    {
        var window = Window(out _);
        var button = ControlAutomationPeer.CreatePeerForElement(window.GetVisualDescendants().OfType<AxButton>()
            .Single(candidate => Equals(candidate.Content, Localizer.Instance["projects.stub"])));

        Assert.Equal(Localizer.Instance["projects.stub"], button.GetName());
        Assert.Equal(Localizer.Instance["projects.stub.hint"], button.GetHelpText());
    }

    /// <summary>Пропавший проект не открывают: окно остаётся и объясняет.</summary>
    [AvaloniaFact]
    public void Asking_for_a_file_that_is_gone_keeps_the_screen_open_and_says_why()
    {
        var path = Solution("Пропавший.sln");
        var window = Window(out var model, path);

        File.Delete(path);

        var asked = 0;
        window.ProjectRequested += (_, _) => asked++;

        Press(window, Rows(window)[0], MouseButton.Left);

        Assert.Equal(0, asked);
        Assert.True(model.HasStatus, "отказали молча");
        Assert.Contains(Localizer.Instance["projects.missing"], model.Status!, StringComparison.Ordinal);
    }

    /// <summary>Без службы проектов открывать не просят, а говорят почему.</summary>
    [AvaloniaFact]
    public void With_no_project_service_the_screen_says_there_is_nobody_to_open_with()
    {
        var window = Window(out var model, Solution("WaveChat.sln"), canOpen: false);

        var asked = 0;
        window.ProjectRequested += (_, _) => asked++;

        Press(window, Rows(window)[0], MouseButton.Left);

        Assert.Equal(0, asked);
        Assert.Equal(Localizer.Instance["projects.noservice"], model.Status);
    }

    /// <summary>
    /// Поиск не наезжает на кнопки, как бы узко ни было окно.
    /// </summary>
    /// <remarks>
    /// Поле было прибито к 340 точкам, и при ширине окна по умолчанию остатка на него не хватало:
    /// оно требовало своё и вылезало поверх первой кнопки. Проверяется поэтому не ширина поля, а
    /// то, что поле и кнопки не делят ни одной точки: в узком окне кнопки уходят строкой ниже, и
    /// сравнивать края по одной оси там уже нельзя.
    /// </remarks>
    [AvaloniaFact]
    public void The_search_field_never_runs_over_the_buttons()
    {
        var window = Window(out _, Solution("WaveChat.sln"));

        foreach (var width in new double[] { 900, 1100, 1600 })
        {
            window.Width = width;
            Dispatcher.UIThread.RunJobs();

            var search = Placed(window, window.GetVisualDescendants().OfType<AxSearchField>().Single());
            var button = Placed(window, window.GetVisualDescendants().OfType<AxButton>()
                .Single(candidate => Equals(candidate.Content, Localizer.Instance["projects.new"])));

            Assert.False(
                search.Intersects(button),
                $"при ширине {width} поиск стоит на {search}, а первая кнопка — на {button}");
        }
    }

    /// <summary>
    /// Кнопки, которым тесно рядом с поиском, уходят под него, а не за край окна.
    /// </summary>
    /// <remarks>
    /// Сетка с колонкой под кнопки их ширины держала шапку одной строкой любой ценой: при
    /// двойном кегле поле поиска сжималось в ноль, а «Каркас студии» уходил за край окна. Теперь
    /// тесно — значит, строкой ниже, и поле остаётся полем.
    /// </remarks>
    [AvaloniaFact]
    public void Buttons_short_of_room_go_under_the_search_field()
    {
        var window = Window(out _, Solution("WaveChat.sln"));

        TypeScale.Enlarge(window, 2);

        var search = Placed(window, window.GetVisualDescendants().OfType<AxSearchField>().Single());
        var stub = window.GetVisualDescendants().OfType<AxButton>()
            .Single(candidate => Equals(candidate.Content, Localizer.Instance["projects.stub"]));
        var button = Placed(window, stub);

        Assert.True(button.Top >= search.Bottom, $"поиск стоит на {search}, а «Каркас студии» — на {button}");
        Assert.True(search.Width >= 200, $"поле поиска сжалось до {search.Width}");
        Assert.True(button.Right <= window.ClientSize.Width, $"«Каркас студии» ушёл за край окна: {button}");

        window.Close();
    }

    /// <summary>
    /// Кнопки держатся правого края окна, а не середины.
    /// </summary>
    /// <remarks>
    /// Раздел был закрыт шириной в 980 и прижат влево: в развёрнутом окне кнопки оказывались
    /// посреди экрана, а справа зияла пустота. Проверяется поэтому не их место, а то, что оно
    /// уходит вправо ровно настолько, насколько прибавили окну.
    /// </remarks>
    [AvaloniaFact]
    public void The_buttons_keep_to_the_right_edge()
    {
        var window = Window(out _, Solution("WaveChat.sln"));

        window.Width = 1100;
        Dispatcher.UIThread.RunJobs();
        var narrow = ButtonsRight(window);

        window.Width = 1600;
        Dispatcher.UIThread.RunJobs();
        var wide = ButtonsRight(window);

        Assert.True(
            Math.Abs(wide - narrow - 500) < 2,
            $"окну прибавили 500, а кнопки уехали на {wide - narrow}");
    }

    /// <summary>
    /// Поиск держится левого края, как бы широко ни было окно.
    /// </summary>
    /// <remarks>
    /// Растянутое поле с пределом ширины Avalonia ставит по середине слота, а не слева: сняв
    /// ограничение с раздела, поиск уехал в середину экрана. Ширину теперь держит колонка, и
    /// проверяется именно это — левый край поля не двигается, а ширина упирается в 340.
    /// </remarks>
    [AvaloniaFact]
    public void The_search_field_keeps_to_the_left_edge()
    {
        var window = Window(out _, Solution("WaveChat.sln"));

        double? left = null;

        foreach (var width in new double[] { 1100, 1400, 1920 })
        {
            window.Width = width;
            Dispatcher.UIThread.RunJobs();

            var search = window.GetVisualDescendants().OfType<AxSearchField>().Single();
            var at = search.TranslatePoint(new Point(0, 0), window)!.Value.X;

            left ??= at;

            Assert.True(Math.Abs(at - left.Value) < 2, $"при ширине {width} поиск начинается на {at}, а не на {left}");
            Assert.True(search.Bounds.Width <= 340.5, $"при ширине {width} поиск шире предела: {search.Bounds.Width}");
        }
    }

    /// <summary>«Каркас студии» по-прежнему входит в студию без проекта.</summary>
    [AvaloniaFact]
    public void The_skeleton_button_still_opens_the_studio()
    {
        var window = Window(out _);

        var entered = 0;
        window.StudioRequested += (_, _) => entered++;

        var button = window.GetVisualDescendants().OfType<AxButton>()
            .Single(candidate => Equals(candidate.Content, Localizer.Instance["projects.stub"]));

        Press(window, button, MouseButton.Left);

        Assert.Equal(1, entered);
    }

    // ===== Оснастка =====

    /// <summary>Кладёт решение на диск и отмечает его недавним.</summary>
    private string Solution(string fileName)
    {
        var path = Path.Combine(_root, fileName);

        File.WriteAllText(path, string.Empty);
        new RecentProjects(StateFile).Touch(path);

        return path;
    }

    private WelcomeViewModel Model(params string[] _) =>
        new(new RecentProjects(StateFile), new PluginCatalog(Path.Combine(_root, "plugins")));

    /// <summary>
    /// Экран Welcome говорит то, чего не сделал запуск.
    /// </summary>
    /// <remarks>
    /// Своего места под это сообщение не заводится: оно появляется на запуске,
    /// который прошёл, и место под него стояло бы пустым всегда. Берётся та же
    /// полоса, которой экран отвечает на отказы открыть проект.
    /// </remarks>
    [AvaloniaFact]
    public void The_welcome_screen_says_what_the_startup_could_not_do()
    {
        var window = Window(out var model);

        Assert.False(model.HasStatus, "до слова полосе состояния показываться нечем");

        window.Say(Localizer.Instance["startup.degraded"]);
        Dispatcher.UIThread.RunJobs();

        Assert.True(model.HasStatus);
        Assert.Contains(
            window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text),
            text => text == Localizer.Instance["startup.degraded"]);

        window.Close();
    }

    /// <summary>
    /// Экран Welcome читается целиком и при двойной шкале кеглей.
    /// </summary>
    /// <remarks>
    /// Кнопки, строки навигации и недавние проекты стояли на прибитых высотах, и прогон
    /// студии с удвоенными кеглями срезал их все до одной. Здесь шкалу растят тем же путём —
    /// ключами темы — и смотрят на каждую показанную строку.
    /// </remarks>
    [AvaloniaFact]
    public void The_welcome_screen_reads_whole_at_twice_the_type_scale()
    {
        Localizer.Instance.SetLanguage("ru");
        Solution("Hello.slnx");
        Solution("TestApp.sln");

        var window = Window(out _);

        TypeScale.Enlarge(window, 2);

        var cut = TypeScale.Labels(window)
            .Select(label => TypeScale.Whole(label, window, out var why) ? null : $"«{label.Text}»: {why}")
            .OfType<string>()
            .ToList();

        Assert.True(cut.Count == 0, "срезано при двойном кегле:\n" + string.Join("\n", cut));

        window.Close();
    }

    /// <summary>Собирает окно над своим файлом недавних и показывает его.</summary>
    /// <param name="model">Модель показанного окна — её и проверяют.</param>
    /// <param name="_">Решения, положенные до сборки окна.</param>
    /// <param name="canOpen">Есть ли кому открывать.</param>
    private WelcomeWindow Window(out WelcomeViewModel model, string? _ = null, bool canOpen = true)
    {
        var window = new WelcomeWindow(
            new JsonSettingsStore(Path.Combine(_root, "settings.json")),
            new RecentProjects(StateFile),
            new PluginCatalog(Path.Combine(_root, "plugins")),
            Extensions())
        {
            CanOpenProjects = () => canOpen,
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        model = (WelcomeViewModel)window.DataContext!;
        return window;
    }

    /// <summary>Где контрол стоит в окне.</summary>
    private static Rect Placed(WelcomeWindow window, Visual visual) =>
        new Rect(visual.Bounds.Size).TransformToAABB(visual.TransformToVisual(window)!.Value);

    /// <summary>Правый край последней кнопки в окне.</summary>
    private static double ButtonsRight(WelcomeWindow window)
    {
        var button = window.GetVisualDescendants().OfType<AxButton>()
            .Single(candidate => Equals(candidate.Content, Localizer.Instance["projects.stub"]));

        return button.TranslatePoint(new Point(button.Bounds.Width, 0), window)!.Value.X;
    }

    /// <summary>Строки недавних на экране — по классу, которым их рисует разметка.</summary>
    private static IReadOnlyList<Border> Rows(WelcomeWindow window) =>
        [.. window.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("row"))];

    /// <summary>Нажимает кнопкой мыши в середину контрола.</summary>
    private static void Press(WelcomeWindow window, Visual target, MouseButton button)
    {
        var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window);

        window.MouseDown(at!.Value, button);
        window.MouseUp(at.Value, button);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Служба расширений, собранная, но не поднятая, — как в PluginsPageTests.</summary>
    private StudioPlugins Extensions() => _studio.Build();
}
