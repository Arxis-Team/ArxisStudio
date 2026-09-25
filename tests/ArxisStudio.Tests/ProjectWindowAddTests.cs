using System.Text;
using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Icons;
using ArxisStudio.Modules.Project.Dialogs;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Panels;
using ArxisStudio.Modules.Project.Tree;
using ArxisStudio.Projects;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;
using static ArxisStudio.Tests.ProjectWindowDialogs;

namespace ArxisStudio.Tests;

/// <summary>
/// «Добавить ▸» в окне проекта: где стоит меню, что в нём, диалог имени и дорога к службе файлов.
/// </summary>
/// <remarks>
/// Пункты приходят от службы создания студии — настоящей, над манифестами модулей, там, где
/// проверяется меню, и подделанной там, где тесту нужен свой пункт: с разновидностями, с отказом, с
/// файлами, которые надо открыть. Служба файлов подделана: что стало на диске и в модели, тест
/// говорит сам — новым снимком службы проектов, как настоящая служба перечитывает модель раньше, чем
/// вернуться.
/// </remarks>
public class ProjectWindowAddTests
{
    /// <summary>
    /// «Добавить ▸» стоит первым у проекта, каталога и файла; у решения, папки решения и зависимостей
    /// создавать некуда, и пункта нет.
    /// </summary>
    [AvaloniaFact]
    public async Task Add_stands_first_where_there_is_a_directory_to_add_into()
    {
        using var studio = await Opened(Real());

        studio.Expand("Views");

        foreach (var name in new[] { "App", "Views", "MainWindow.axaml", "Program.cs" })
            Assert.Equal(studio.Strings["project.add"], studio.Panel.Items(studio.Select(name))[0].Header);

        Assert.Equal(new KeyGesture(Key.Insert, KeyModifiers.Alt), Add(studio, "App")!.InputGesture);

        foreach (var kind in new[] { NodeKind.Solution, NodeKind.SolutionFolder, NodeKind.Dependencies })
            Assert.Null(Add(studio, studio.Rows.First(row => row.Node.Kind == kind)));
    }

    /// <summary>
    /// Без службы создания или без службы файлов меню не обещает того, чего окно не сделает.
    /// </summary>
    [AvaloniaFact]
    public async Task Without_either_service_there_is_no_add()
    {
        using (var studio = await Opened(newItems: null))
            Assert.Null(Add(studio, "App"));

        using (var bare = new ProjectWindowStudio(newItems: Real()))
        {
            await bare.Open();

            Assert.Null(Add(bare, "App"));
        }
    }

    /// <summary>
    /// Свои пункты окна — «Каталог» и «Файл» — идут первыми, чужие — за чертой, а ветки расширений
    /// сходятся по тексту.
    /// </summary>
    /// <remarks>
    /// Свои пункты окно объявляет манифестом, как любой плагин, и получает их от той же службы, что и
    /// чужие: меню собрано настоящей службой над настоящими манифестами модулей студии.
    /// </remarks>
    [AvaloniaFact]
    public async Task Own_items_come_first_and_extensions_follow_a_line()
    {
        using var studio = await Opened(Real());

        var samples = StudioModules.Describe().Single(module => module.Id == "arxis.sample").Strings;

        Assert.Equal(
            $"{studio.Strings["add.directory"]} | {studio.Strings["add.file"]} | — | {samples["add.samples"]} ▸ [{samples["add.note"]}]",
            Describe(Rows(Add(studio, "App")!)));

        var own = Rows(Add(studio, "App")!).OfType<AxMenuItem>().Take(2).ToList();

        Assert.Same(AxIcons.Folder, Assert.IsType<AxIcon>(own[0].Icon).Data);
        Assert.Same(AxIcons.Document, Assert.IsType<AxIcon>(own[1].Icon).Data);
    }

    /// <summary>
    /// Ветка, которую назвали два расширения, одна; внутри неё их пункты разделены чертой.
    /// </summary>
    /// <remarks>
    /// Стоит сошедшаяся ветка там, где её завёл первый, — в его группе, без черты перед собой; черта
    /// отделяет группу второго, когда очередь доходит до его собственных пунктов.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_branch_named_by_two_extensions_is_one_with_a_line_inside()
    {
        var probe = new NewItemsProbe();

        probe.Declared.AddRange(
        [
            Item("a.one", "Один", owner: "a"),
            Item("a.note", "Заметка", owner: "a", menu: ["Образцы"]),
            Item("b.greeting", "Приветствие", owner: "b", menu: ["Образцы"]),
            Item("b.two", "Два", owner: "b"),
        ]);

        using var studio = await Opened(probe);

        Assert.Equal("Один | Образцы ▸ [Заметка | — | Приветствие] | — | Два", Describe(Rows(Add(studio, "App")!)));
    }

    /// <summary>
    /// Условие <c>when</c> держит пункт в тех проектах, что ему подходят: по языку и по пакету, на
    /// который проект ссылается сам.
    /// </summary>
    [AvaloniaFact]
    public async Task The_when_rule_keeps_items_to_the_projects_they_fit()
    {
        var probe = new NewItemsProbe();

        probe.Declared.AddRange(
        [
            Item("p.any", "Любой"),
            Item("p.avalonia", "Контрол", languages: ["C#"], packages: ["Avalonia"]),
            Item("p.serilog", "Журнал", packages: ["Serilog"]),
            Item("p.fsharp", "Модуль F#", languages: ["F#"]),
        ]);

        using var studio = await Opened(probe);

        Assert.Equal("Любой | Контрол", Describe(Rows(Add(studio, "App")!)));
        Assert.Equal("Любой", Describe(Rows(Add(studio, "Lib")!)));
    }

    /// <summary>
    /// «Файл» спрашивает имя с предложенным свободным, отдаёт службе файлов пустой файл в каталоге
    /// узла и встаёт на созданное.
    /// </summary>
    [AvaloniaFact]
    public async Task A_file_is_created_where_the_menu_was_asked_and_the_tree_stands_on_it()
    {
        var files = new FilesProbe();

        using var studio = await Opened(Real(), files);

        studio.Expand("Views");

        var views = studio.Row("Views").Node.Path;

        File.WriteAllText(views.Combine("NewFile1.txt").Value, string.Empty);
        files.After = () =>
        {
            File.WriteAllText(views.Combine("Notes.txt").Value, string.Empty);
            studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution(more: ["Views/NewFile1.txt", "Views/Notes.txt"])));
        };

        studio.Click(Rows(Add(studio, "Views")!).OfType<AxMenuItem>(), studio.Strings["add.file"]);

        var dialog = Dialog<CreateDialog>(studio);
        var field = Part<AxTextBox>(dialog, "Chosen");

        Assert.Equal(Format(studio, "project.add.title", studio.Strings["add.file"]), dialog.Title);
        Assert.Equal("NewFile2.txt", field.Text);
        Assert.Equal((0, "NewFile2".Length), (field.SelectionStart, field.SelectionEnd));
        Assert.True(Part<TextBlock>(dialog, "Hint").IsVisible, "пункт разрешает путь, а подсказки о «/» нет");
        Assert.False(Part<AxListBox>(dialog, "Variants").IsVisible);

        field.Text = "Notes.txt";
        Enter(dialog);

        await Settled(studio, () => studio.View.Tree.SelectedItem is Row { Name: "Notes.txt" });

        var created = Assert.Single(Assert.Single(files.Created));

        Assert.Equal(views.Combine("Notes.txt"), created.Path);
        Assert.False(created.IsDirectory);
        Assert.True(created.Content.IsEmpty);
        Assert.Equal(Format(studio, "project.add.label", "Notes.txt"), Assert.Single(files.Labels));
        Assert.Contains(Format(studio, "project.added", "Notes.txt"), studio.Status.Said);
        Assert.True(studio.Item(studio.Selected).IsKeyboardFocusWithin, "клавиатура не встала на созданное");
    }

    /// <summary>
    /// Ctrl+Z уносит созданное одним действием, а выделение уходит к каталогу, где оно лежало.
    /// </summary>
    /// <remarks>
    /// Отмена создания ничего не возвращает — созданное просто пропадает, как у отмены копии, — и
    /// встать ей не на что. Выделение, чья строка пропала, дерево само ставит на ближайшего предка.
    /// </remarks>
    [AvaloniaFact]
    public async Task Undo_takes_the_creation_away_and_the_selection_goes_to_its_directory()
    {
        var files = new FilesProbe();
        var history = new HistoryProbe();

        using var studio = new ProjectWindowStudio(files: files, history: history, newItems: Real());

        await studio.Open();
        studio.Expand("Views");

        var notes = studio.Row("Views").Node.Path.Combine("Notes.txt");

        files.After = () =>
        {
            File.WriteAllText(notes.Value, string.Empty);
            studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution(more: ["Views/Notes.txt"])));
        };

        studio.Click(Rows(Add(studio, "Views")!).OfType<AxMenuItem>(), studio.Strings["add.file"]);

        var dialog = Dialog<CreateDialog>(studio);

        Part<AxTextBox>(dialog, "Chosen").Text = "Notes.txt";
        Enter(dialog);

        await Settled(studio, () => studio.View.Tree.SelectedItem is Row { Name: "Notes.txt" });

        history.LastStudioAction = new LocalHistoryAction
        {
            Id = 9,
            Time = DateTimeOffset.UnixEpoch,
            Label = Assert.Single(files.Labels),
            Origin = LocalHistoryOrigin.Studio,
            Changes = [new LocalHistoryChange { Kind = LocalHistoryChangeKind.Created, Path = notes }],
        };

        history.After = () =>
        {
            File.Delete(notes.Value);
            studio.Projects.Publish(ProjectWindowStudio.Ready(3, studio.Solution()));
        };

        studio.Press(studio.Item(studio.Selected), Key.Z, KeyModifiers.Control);
        Dialog<UndoDialog>(studio).KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);

        await Settled(studio, () => studio.View.Tree.SelectedItem is Row { Name: "Views" });

        Assert.Equal([9L], history.Undone);
        Assert.DoesNotContain(studio.Rows, row => row.Name == "Notes.txt");
    }

    /// <summary>
    /// Путь в имени кладёт каталоги по дороге: «Каталог» с <c>Models/Dto</c> создаёт <c>Dto</c> в
    /// <c>Models</c>, и дерево встаёт на него, хотя в проекте он пуст.
    /// </summary>
    [AvaloniaFact]
    public async Task A_path_in_the_name_lays_the_directories_down()
    {
        var files = new FilesProbe();

        using var studio = await Opened(Real(), files);

        var app = studio.Row("App").Node.Path.Directory;

        files.After = () =>
        {
            Directory.CreateDirectory(app.Combine(Path.Combine("Models", "Dto")).Value);
            studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution()));
        };

        studio.Click(Rows(Add(studio, "App")!).OfType<AxMenuItem>(), studio.Strings["add.directory"]);

        var dialog = Dialog<CreateDialog>(studio);
        var field = Part<AxTextBox>(dialog, "Chosen");

        Assert.Equal("NewDirectory1", field.Text);
        Assert.Equal((0, "NewDirectory1".Length), (field.SelectionStart, field.SelectionEnd));

        field.Text = "Models/Dto";
        Enter(dialog);

        await Settled(studio, () => studio.View.Tree.SelectedItem is Row { Name: "Dto" });

        var created = Assert.Single(Assert.Single(files.Created));

        Assert.Equal(app.Combine(Path.Combine("Models", "Dto")), created.Path);
        Assert.True(created.IsDirectory);
        Assert.Contains(Format(studio, "project.added", "Dto"), studio.Status.Said);
    }

    /// <summary>
    /// Запрос называет имя без пути, каталог, куда ляжет пункт, проект и пространство имён этого
    /// каталога; «передумал» в окне расширения не создаёт ничего и ничего не говорит.
    /// </summary>
    [AvaloniaFact]
    public async Task The_request_names_the_place_and_a_declined_item_creates_nothing()
    {
        var probe = new NewItemsProbe();
        var files = new FilesProbe();

        probe.Declared.Add(Item("p.code", "Код", kind: NewItemKind.Code, name: "Card$n$", nested: true));

        using var studio = await Opened(probe, files);

        var views = studio.Row("Views").Node.Path;
        var said = studio.Status.Said.Count;

        studio.Click(Rows(Add(studio, "Views")!).OfType<AxMenuItem>(), "Код");

        var dialog = Dialog<CreateDialog>(studio);

        Assert.Equal("Card1", Part<AxTextBox>(dialog, "Chosen").Text);

        Part<AxTextBox>(dialog, "Chosen").Text = "Parts/Card";
        Enter(dialog);

        await Settled(studio, () => probe.Requests.Count == 1);

        var request = Assert.Single(probe.Requests);

        Assert.Equal("Card", request.Name);
        Assert.Equal(views.Combine("Parts").Value, request.Directory);
        Assert.Equal("App", request.Project);
        Assert.Equal(studio.Row("App").Node.Path.Value, request.ProjectFile);
        Assert.Equal("App", request.RootNamespace);
        Assert.Equal("App.Views.Parts", request.Namespace);
        Assert.Null(request.Variant);

        Assert.Empty(files.Created);
        Assert.Equal(said, studio.Status.Said.Count);
        Assert.Empty(studio.Window.OwnedWindows.OfType<FailureDialog>());
    }

    /// <summary>
    /// Пока имя печатают, диалог говорит, что с ним не так, и кнопка выключена: правила имени файла,
    /// имя типа, путь там, где его нельзя, и занятость всего, что пункт положит.
    /// </summary>
    [AvaloniaFact]
    public async Task The_dialog_says_what_is_wrong_with_the_name()
    {
        var probe = new NewItemsProbe
        {
            Outputs = (_, name, _) => [name + ".cs", name + ".Designer.cs"],
        };

        probe.Declared.Add(Item("p.type", "Тип", name: "Card$n$", identifier: true));

        using var studio = await Opened(probe);

        var views = studio.Row("Views").Node.Path;

        File.WriteAllText(views.Combine("Taken.Designer.cs").Value, string.Empty);
        studio.Click(Rows(Add(studio, "Views")!).OfType<AxMenuItem>(), "Тип");

        var dialog = Dialog<CreateDialog>(studio);
        var field = Part<AxTextBox>(dialog, "Chosen");
        var problem = Part<TextBlock>(dialog, "Problem");
        var confirm = Part<AxButton>(dialog, "Confirm");

        Assert.False(Part<TextBlock>(dialog, "Hint").IsVisible, "пункт пути не разрешает, а подсказка о «/» есть");

        (string Typed, string Key, string? Subject)[] wrong =
        [
            (string.Empty, "project.rename.empty", null),
            ("Ca:rd", "project.rename.invalid", ":"),
            ("Card.", "project.rename.trailing", null),
            ("nul", "project.rename.reserved", "nul"),
            ("Parts/Card", "project.rename.invalid", "/"),
            ("1Card", "project.add.identifier", null),
            ("Ca-rd", "project.add.identifier", null),
            ("class", "project.add.keyword", "class"),
            ("Taken", "project.rename.taken", "Taken.Designer.cs"),
        ];

        foreach (var (typed, key, subject) in wrong)
        {
            field.Text = typed;

            Assert.False(confirm.IsEnabled, $"«{typed}» не годится, а кнопка включена");
            Assert.Equal(Format(studio, key, subject ?? string.Empty), problem.Text);
            Assert.True(problem.IsVisible);
        }

        field.Text = "Card";

        Assert.True(confirm.IsEnabled);
        Assert.False(problem.IsVisible);

        studio.Click(Part<AxButton>(dialog, "Cancel"));
    }

    /// <summary>
    /// У пункта с путём пустой сегмент — лишняя косая, а файл на месте каталога по дороге — занято.
    /// </summary>
    [AvaloniaFact]
    public async Task A_path_is_checked_segment_by_segment()
    {
        using var studio = await Opened(Real());

        studio.Click(Rows(Add(studio, "App")!).OfType<AxMenuItem>(), studio.Strings["add.directory"]);

        var dialog = Dialog<CreateDialog>(studio);
        var field = Part<AxTextBox>(dialog, "Chosen");
        var problem = Part<TextBlock>(dialog, "Problem");

        foreach (var (typed, key, subject) in new[]
                 {
                     ("Models//Dto", "project.rename.invalid", "/"),
                     ("/Dto", "project.rename.invalid", "/"),
                     ("Models/", "project.rename.invalid", "/"),
                     ("Program.cs/Dto", "project.rename.taken", "Program.cs"),
                     ("Models", "project.rename.taken", "Models"),
                 })
        {
            field.Text = typed;

            Assert.Equal(Format(studio, key, subject), problem.Text);
        }

        field.Text = "Models/Dto/Deep";

        Assert.True(Part<AxButton>(dialog, "Confirm").IsEnabled, "путь через существующий каталог годится");

        studio.Click(Part<AxButton>(dialog, "Cancel"));
    }

    /// <summary>
    /// Разновидности идут списком под полем; стрелки в поле ходят по нему, проверка следует за
    /// выбором, а запрос несёт выбранную.
    /// </summary>
    [AvaloniaFact]
    public async Task Variants_are_picked_with_arrows_from_the_name_field()
    {
        var probe = new NewItemsProbe
        {
            Outputs = (_, name, variant) => [variant == "interface" ? $"I{name}.cs" : $"{name}.cs"],
        };

        probe.Declared.Add(Item("p.type", "Тип", name: "Card$n$", variants: [("class", "Класс"), ("interface", "Интерфейс")]));

        using var studio = await Opened(probe);

        File.WriteAllText(studio.Row("Views").Node.Path.Combine("ICard.cs").Value, string.Empty);
        studio.Click(Rows(Add(studio, "Views")!).OfType<AxMenuItem>(), "Тип");

        var dialog = Dialog<CreateDialog>(studio);
        var field = Part<AxTextBox>(dialog, "Chosen");
        var variants = Part<AxListBox>(dialog, "Variants");

        Assert.True(variants.IsVisible);
        Assert.Equal(["Класс", "Интерфейс"], variants.Items.OfType<CreateVariant>().Select(variant => variant.Title));
        Assert.Equal(0, variants.SelectedIndex);

        field.Text = "Card";
        Arrow(field, Key.Down);

        Assert.Equal(1, variants.SelectedIndex);
        Assert.Equal(Format(studio, "project.rename.taken", "ICard.cs"), Part<TextBlock>(dialog, "Problem").Text);

        Arrow(field, Key.Up);
        Arrow(field, Key.Up);

        Assert.Equal(0, variants.SelectedIndex);
        Assert.True(Part<AxButton>(dialog, "Confirm").IsEnabled);

        Enter(dialog);

        await Settled(studio, () => probe.Requests.Count == 1);

        Assert.Equal("class", Assert.Single(probe.Requests).Variant);
    }

    /// <summary>Отказ пункта показан диалогом с причиной, и служба файлов не слышит ничего.</summary>
    [AvaloniaFact]
    public async Task A_refusal_is_shown_and_nothing_is_created()
    {
        var probe = new NewItemsProbe { Answer = _ => NewItemResult.Failed("Шаблон сломан") };
        var files = new FilesProbe();

        probe.Declared.Add(Item("p.broken", "Сломанный", name: "Broken"));

        using var studio = await Opened(probe, files);

        studio.Click(Rows(Add(studio, "Views")!).OfType<AxMenuItem>(), "Сломанный");
        Enter(Dialog<CreateDialog>(studio));

        await Settled(studio, () => studio.Window.OwnedWindows.OfType<FailureDialog>().Any());

        var failure = Dialog<FailureDialog>(studio);

        Assert.Equal("Шаблон сломан", Part<TextBlock>(failure, "Message").Text);
        Assert.Empty(files.Created);

        studio.Click(Part<AxButton>(failure, "Dismiss"));
    }

    /// <summary>
    /// Файл, которого проект не включает, окно называет строкой и не ждёт его в дереве; то, что пункт
    /// просил открыть, открывается.
    /// </summary>
    [AvaloniaFact]
    public async Task A_file_the_project_leaves_out_is_named_and_opened_without_waiting()
    {
        var probe = new NewItemsProbe
        {
            Answer = request => NewItemResult.Made([new NewItemFile(request.Name + ".md") { Content = Encoding.UTF8.GetBytes("# Заметка"), Open = true }]),
        };

        var files = new FilesProbe();

        probe.Declared.Add(Item("p.note", "Заметка", name: "Note"));

        using var studio = await Opened(probe, files);

        var views = studio.Row("Views").Node.Path;
        var started = DateTime.UtcNow;

        // Студия, которой открыть нечем, говорит об этом строкой, — и строка о созданном идёт после.
        studio.Documents.Opening = _ => studio.Status.Show("нечем открыть");

        studio.Click(Rows(Add(studio, "Views")!).OfType<AxMenuItem>(), "Заметка");
        Enter(Dialog<CreateDialog>(studio));

        await Settled(studio, () => studio.Status.Said.Contains(Format(studio, "project.added.outside", "Note")));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5), "окно ждало в дереве файл, которого проект не включает");
        Assert.Equal(views.Combine("Note.md").Value, Assert.Single(studio.Documents.Opened));
        Assert.Equal(Format(studio, "project.added.outside", "Note"), studio.Status.Said.Last());
        Assert.Equal("# Заметка", Encoding.UTF8.GetString(Assert.Single(Assert.Single(files.Created)).Content.Span));
    }

    /// <summary>
    /// Alt+Insert показывает те же пункты отдельным меню у строки дерева и в правой колонке; где
    /// создавать некуда, клавиша остаётся ничьей.
    /// </summary>
    [AvaloniaFact]
    public async Task Alt_Insert_offers_the_add_items_in_the_tree_and_in_the_pane()
    {
        using var studio = await Opened(Real());

        Assert.True(AltInsert(studio, studio.Item(studio.Select("Views"))), "Alt+Insert на каталоге не показал меню");
        var dependencies = studio.Rows.First(row => row.Node.Kind == NodeKind.Dependencies);

        studio.View.Tree.SelectedItem = dependencies;
        studio.Item(dependencies).Focus();

        Assert.False(AltInsert(studio, studio.Item(dependencies)), "Alt+Insert на зависимостях что-то показал");

        Assert.Equal(
            Describe(Rows(Add(studio, "Views")!)),
            Describe(studio.Panel.Menu!.AddItems(studio.Row("Views").Node, EditOrigin.Tree)));
    }

    /// <summary>В две колонки Alt+Insert на пустом месте создаёт в каталоге колонки, а на плитке — в её.</summary>
    [AvaloniaFact]
    public async Task Alt_Insert_in_the_pane_speaks_of_the_tile_or_of_the_column()
    {
        using var studio = await Opened(Real(), twoColumns: true);

        studio.Select("Views");
        Dispatcher.UIThread.RunJobs();

        var pane = studio.Panel.Pane!;
        var list = pane.Shown;
        var tile = studio.Model.Browser.Items.Single(tile => tile.Name == "MainWindow.axaml");

        list.SelectedItem = null;

        Assert.Equal("Views", pane.AddTarget(list)?.Name);
        Assert.True(pane.ShowAdd(list), "на пустом месте колонки Alt+Insert не показал меню");

        list.SelectedItem = tile;

        Assert.Same(tile.Node, pane.AddTarget(list));
        Assert.True(pane.ShowAdd(list), "на плитке Alt+Insert не показал меню");
    }

    private static StudioNewItems Real() =>
        new(new StudioLog(), new PluginGuard(), new PluginContributionRegistry()) { Contributing = () => StudioModules.Describe() };

    private static Task<ProjectWindowStudio> Opened(IStudioNewItems? newItems, FilesProbe? files = null, bool twoColumns = false) =>
        ProjectWindowStudio.OpenedAsync(files, twoColumns, newItems: newItems);

    /// <summary>«Добавить ▸» в меню строки, выбранной одной; пусто — его нет.</summary>
    private static AxMenuItem? Add(ProjectWindowStudio studio, string name) => Add(studio, studio.Row(name));

    private static AxMenuItem? Add(ProjectWindowStudio studio, Row row)
    {
        studio.View.Tree.SelectedItem = row;

        return studio.Panel.Items(row).FirstOrDefault(item => Equals(item.Header, studio.Strings["project.add"]));
    }

    private static List<Control> Rows(AxMenuItem add) => [.. add.Items.OfType<Control>()];

    /// <summary>Меню строкой: пункты через черту, черта — тире, ветка — со своим содержимым.</summary>
    private static string Describe(IEnumerable<Control> rows) => string.Join(" | ", rows.Select(row => row switch
    {
        AxSeparator => "—",
        AxMenuItem { ItemCount: > 0 } branch => $"{branch.Header} ▸ [{Describe(branch.Items.OfType<Control>())}]",
        AxMenuItem item => item.Header?.ToString() ?? string.Empty,
        _ => row.GetType().Name,
    }));

    /// <summary>Стрелка в поле имени — туннелем, как её получает поле.</summary>
    private static void Arrow(AxTextBox field, Key key)
    {
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = field });
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Alt+Insert там, где стоит клавиатура; ответ — взял ли его кто-нибудь.</summary>
    private static bool AltInsert(ProjectWindowStudio studio, Control where)
    {
        var key = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Insert,
            KeyModifiers = KeyModifiers.Alt,
            Source = where,
        };

        where.RaiseEvent(key);
        Dispatcher.UIThread.RunJobs();

        foreach (var window in studio.Window.OwnedWindows.ToList())
            window.Close();

        return key.Handled;
    }

    private static StudioNewItem Item(
        string id,
        string title,
        string owner = "probe",
        NewItemKind kind = NewItemKind.File,
        string name = "",
        bool nested = false,
        bool identifier = false,
        string[]? menu = null,
        string[]? languages = null,
        string[]? packages = null,
        (string Id, string Title)[]? variants = null) => new()
        {
            Id = id,
            Owner = owner,
            Kind = kind,
            Title = title,
            Name = name,
            Nested = nested,
            NameRule = identifier ? NewItemNameRule.Identifier : NewItemNameRule.File,
            Menu = menu ?? [],
            Languages = languages ?? [],
            Packages = packages ?? [],
            Variants = [.. (variants ?? []).Select(variant => new StudioNewItemVariant { Id = variant.Id, Title = variant.Title })],
        };

    /// <summary>Служба создания, которую тест наполняет сам: пункты, пути и ответ.</summary>
    private sealed class NewItemsProbe : IStudioNewItems
    {
        /// <summary>Пункты в порядке показа.</summary>
        public List<StudioNewItem> Declared { get; } = [];

        /// <summary>Что пункт положит под именем при разновидности.</summary>
        public Func<StudioNewItem, string, string?, IReadOnlyList<string>> Outputs { get; init; } = (_, _, _) => [];

        /// <summary>Что ответить на сборку.</summary>
        public Func<NewItemRequest, NewItemResult> Answer { get; init; } = _ => NewItemResult.Declined;

        /// <summary>Запросы, по порядку.</summary>
        public List<NewItemRequest> Requests { get; } = [];

        public IReadOnlyList<StudioNewItem> Items => Declared;

        public IReadOnlyList<string> Paths(StudioNewItem item, string name, string? variant = null) =>
            string.IsNullOrEmpty(name) ? [] : Outputs(item, name, variant);

        public string Suggest(StudioNewItem item, string directory, string? variant = null) =>
            item.Name.Replace("$n$", "1", StringComparison.Ordinal);

        public Task<NewItemResult> MakeAsync(StudioNewItem item, NewItemRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return Task.FromResult(Answer(request));
        }
    }
}
