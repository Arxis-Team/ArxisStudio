using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Открытые документы студии.
/// </summary>
/// <remarks>
/// Правил здесь больше, чем видно с первого взгляда: «файл уже открыт — не
/// открывать второй раз», «показан ровно один», «место закрытого занимает
/// сосед», «документы выгружаемого плагина уходят вместе с ним». Раньше всё это
/// жило внутри главного окна и не проверялось ничем: чтобы дойти до кода, надо
/// было поднять студию со всеми плагинами.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioDocumentsTests
{
    /// <summary>Открытый файл становится вкладкой и показывается.</summary>
    [AvaloniaFact]
    public async Task An_opened_file_becomes_a_tab_and_is_shown()
    {
        var (documents, _, status) = Studio();

        await documents.OpenAsync(@"C:\проект\Окно.axaml");

        var open = Assert.Single(documents.Opened);

        Assert.Equal(@"doc:C:\проект\Окно.axaml", open.Id);
        Assert.Equal("arxis.designer", open.PluginId);
        Assert.Same(open.View, documents.Shown);
        Assert.Equal(1, Probe(open).Activated);
        Assert.Equal(@"C:\проект\Окно.axaml", status.Last());
    }

    /// <summary>
    /// Тот же файл второй раз не открывается — показывается открытый.
    /// </summary>
    /// <remarks>
    /// Два документа одного файла — это две правки одного текста, которые ничего
    /// друг о друге не знают; чья запись переживёт другую, решил бы порядок
    /// сохранения.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_same_file_opens_once()
    {
        var (documents, dock, _) = Studio();

        await documents.OpenAsync(@"C:\проект\Окно.axaml");
        await documents.OpenAsync(@"C:\проект\Схема.axaml");
        await documents.OpenAsync(@"C:\проект\Окно.axaml");

        Assert.Equal(2, documents.Opened.Count);
        Assert.Equal(@"doc:C:\проект\Окно.axaml", dock.Showing);
    }

    /// <summary>Открывать нечем — студия говорит об этом и вкладки не заводит.</summary>
    [AvaloniaFact]
    public async Task A_file_nobody_opens_leaves_no_tab()
    {
        var (documents, _, status) = Studio(editorFor: _ => null);

        await documents.OpenAsync(@"C:\проект\Загадка.zip");

        Assert.Empty(documents.Opened);
        Assert.Null(documents.Shown);
        Assert.Equal(Text("editor.noeditor"), status.Last());
    }

    /// <summary>Редактор не справился — человек узнаёт причину, а не пустую вкладку.</summary>
    [AvaloniaFact]
    public async Task A_file_that_fails_to_load_says_why()
    {
        var editor = new ProbeEditor((null, "файл побит"));
        var (documents, _, status) = Studio(editorFor: _ => new EditorMatch(editor, "arxis.designer"));

        await documents.OpenAsync(@"C:\проект\Окно.axaml");

        Assert.Empty(documents.Opened);
        Assert.Contains("файл побит", status.Last());
    }

    /// <summary>
    /// Показан ровно один документ: прежний узнаёт, что его сменили.
    /// </summary>
    /// <remarks>
    /// Не бухгалтерия: за <c>OnDeactivated</c> у редактора стоит остановка
    /// работы, которую видно только на экране, — подсветка, слежение за файлом,
    /// перерисовка. Не сказать о смене значит оставить их работать на невидимом.
    /// </remarks>
    [AvaloniaFact]
    public async Task Only_one_document_is_shown_at_a_time()
    {
        var (documents, _, _) = Studio();

        await documents.OpenAsync(@"C:\проект\Окно.axaml");
        var first = Probe(documents.Opened[0]);

        await documents.OpenAsync(@"C:\проект\Схема.axaml");
        var second = Probe(documents.Opened[1]);

        Assert.Equal(1, first.Deactivated);
        Assert.Equal(1, second.Activated);
        Assert.Same(documents.Opened[1].View, documents.Shown);
    }

    /// <summary>Выбор вкладки в раскладке показывает её документ.</summary>
    /// <remarks>
    /// Связь «вкладка — документ» служба держит сама. Прежде её держало окно, и
    /// проверить, что щелчок по вкладке доходит до документа, было нечем.
    /// </remarks>
    [AvaloniaFact]
    public async Task Choosing_a_tab_shows_its_document()
    {
        var (documents, dock, _) = Studio();

        await documents.OpenAsync(@"C:\проект\Окно.axaml");
        await documents.OpenAsync(@"C:\проект\Схема.axaml");

        dock.Show(@"doc:C:\проект\Окно.axaml");
        Dispatcher.UIThread.RunJobs();

        Assert.Same(documents.Opened[0].View, documents.Shown);
    }

    /// <summary>Щелчок по чужой вкладке документ не меняет.</summary>
    /// <remarks>
    /// Выбор приходит на любую вкладку, а не только на документную: панель внизу
    /// документ не меняет и не обязана менять.
    /// </remarks>
    [AvaloniaFact]
    public async Task Choosing_a_panel_leaves_the_document_alone()
    {
        var (documents, dock, _) = Studio();

        await documents.OpenAsync(@"C:\проект\Окно.axaml");

        var shown = documents.Shown;

        dock.Add("hello", "hello:tree", new PluginPlacement { Side = "left" },
            "Проект", PluginStrings.Studio, new Border());

        dock.Show("hello:tree");
        Dispatcher.UIThread.RunJobs();

        Assert.Same(shown, documents.Shown);
    }

    /// <summary>Закрытый документ уходит из списка, из раскладки и из памяти.</summary>
    [AvaloniaFact]
    public async Task A_closed_document_leaves_everywhere_at_once()
    {
        var (documents, dock, _) = Studio();

        await documents.OpenAsync(@"C:\проект\Окно.axaml");

        var probe = Probe(documents.Opened[0]);

        await documents.CloseAsync(@"doc:C:\проект\Окно.axaml");
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(documents.Opened);
        Assert.Null(documents.Shown);
        Assert.Equal(1, probe.Deactivated);
        Assert.True(probe.Disposed);
        Assert.Null(dock.Items.Find(@"doc:C:\проект\Окно.axaml"));
    }

    /// <summary>Место закрытого документа занимает сосед.</summary>
    /// <remarks>
    /// Иначе центр студии остаётся пустым при живых вкладках рядом: раскладка
    /// сама выбирает соседа, а служба обязана согласиться с её выбором.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_neighbour_takes_the_place_of_a_closed_document()
    {
        var (documents, _, _) = Studio();

        await documents.OpenAsync(@"C:\проект\Окно.axaml");
        await documents.OpenAsync(@"C:\проект\Схема.axaml");

        await documents.CloseAsync(@"doc:C:\проект\Схема.axaml");
        Dispatcher.UIThread.RunJobs();

        var left = Assert.Single(documents.Opened);

        Assert.Same(left.View, documents.Shown);
    }

    /// <summary>Закрывать нечего — просьба ничего не ломает.</summary>
    [AvaloniaFact]
    public async Task Closing_a_stranger_changes_nothing()
    {
        var (documents, _, _) = Studio();

        await documents.OpenAsync(@"C:\проект\Окно.axaml");
        await documents.CloseAsync("hello:tree");

        Assert.Single(documents.Opened);
    }

    /// <summary>
    /// Документы выгружаемого плагина уходят вместе с ним, чужие остаются.
    /// </summary>
    /// <remarks>
    /// Представление документа построил плагин, и живёт оно в его контексте
    /// загрузки: оставить вкладку значит и держать контекст, и показывать
    /// человеку окно, за которым уже ничего нет.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_documents_of_an_unloading_plugin_leave_with_it()
    {
        var mine = new ProbeEditor();
        var theirs = new ProbeEditor();

        var (documents, _, _) = Studio(editorFor: path => path.EndsWith(".axaml", StringComparison.Ordinal)
            ? new EditorMatch(mine, "arxis.designer")
            : new EditorMatch(theirs, "arxis.notes"));

        await documents.OpenAsync(@"C:\проект\Окно.axaml");
        await documents.OpenAsync(@"C:\проект\Заметка.note");

        var released = Probe(documents.Opened[0]);

        await documents.CloseOwnedByAsync("arxis.designer");
        Dispatcher.UIThread.RunJobs();

        var left = Assert.Single(documents.Opened);

        Assert.Equal("arxis.notes", left.PluginId);
        Assert.True(released.Disposed);
        Assert.Same(left.View, documents.Shown);
    }

    /// <summary>Студию закрывают — отпускаются все документы.</summary>
    [AvaloniaFact]
    public async Task Closing_the_studio_releases_every_document()
    {
        var (documents, _, _) = Studio();

        await documents.OpenAsync(@"C:\проект\Окно.axaml");
        await documents.OpenAsync(@"C:\проект\Схема.axaml");

        var probes = documents.Opened.Select(Probe).ToList();

        await documents.CloseAllAsync();

        Assert.Empty(documents.Opened);
        Assert.Null(documents.Shown);
        Assert.All(probes, probe => Assert.True(probe.Disposed));
    }

    /// <summary>
    /// Об открытии файла объявляется до того, как ищется редактор.
    /// </summary>
    /// <remarks>
    /// Редактора может ещё и не быть: плагин, объявивший этот тип файла, спит и
    /// ждёт как раз такого события, чтобы подняться. Объяви студия позже —
    /// разбуженный плагин опоздал бы ровно на тот файл, ради которого его и
    /// будили.
    /// </remarks>
    [AvaloniaFact]
    public async Task Opening_a_file_is_announced_before_the_editor_is_looked_for()
    {
        var order = new List<string>();

        var (documents, _, _) = Studio(editorFor: _ =>
        {
            order.Add("поиск редактора");
            return null;
        });

        StudioDocuments? sender = null;

        documents.Opening += (source, path) =>
        {
            sender = source as StudioDocuments;
            order.Add($"объявление: {path}");
        };

        await documents.OpenAsync(@"C:\проект\Загадка.zip");

        Assert.Equal([@"объявление: C:\проект\Загадка.zip", "поиск редактора"], order);
        Assert.Same(documents, sender);
    }

    /// <summary>Уже открытый файл о себе не объявляет — плагин будить не за чем.</summary>
    [AvaloniaFact]
    public async Task An_already_open_file_announces_nothing()
    {
        var (documents, _, _) = Studio();
        var announced = 0;

        await documents.OpenAsync(@"C:\проект\Окно.axaml");

        documents.Opening += (_, _) => announced++;

        await documents.OpenAsync(@"C:\проект\Окно.axaml");

        Assert.Equal(0, announced);
    }

    /// <summary>Строка студии на её же языке — как её увидит человек.</summary>
    private const string Form = "C:/проект/Окно.axaml";
    private const string Other = "C:/проект/Другое.axaml";

    /// <summary>
    /// Редактор, упавший на открытии, не оставляет вкладки, и сбой записан на его плагин.
    /// </summary>
    /// <remarks>
    /// Открытие — чужой код и асинхронный: падает он и до первого ожидания, и после. Мимо шва сбой
    /// уходил зовущему — в панель проекта, в команду соседа, — и приписывался тому, кто просил.
    /// </remarks>
    [AvaloniaFact]
    public async Task An_editor_that_falls_on_opening_leaves_no_tab_and_is_charged()
    {
        var guard = new PluginGuard();
        var failures = new List<PluginFailure>();

        guard.Failed += (_, failure) => failures.Add(failure);

        var (documents, dock, status) = Studio(_ => new EditorMatch(new FallingEditor(), "arxis.designer"), guard);

        await documents.OpenAsync(Form);

        Assert.Empty(documents.Opened);
        Assert.DoesNotContain(StudioDocuments.Name(Form), dock.Items.Known());
        Assert.Contains(status, said => said.StartsWith(Text("editor.loadfailed"), StringComparison.Ordinal));

        var failure = Assert.Single(failures);

        Assert.Equal("arxis.designer", failure.PluginId);
        Assert.Equal("редактор упал после ожидания", failure.Message);
    }

    /// <summary>
    /// Представление, упавшее на закрытии, не останавливает закрытие остальных.
    /// </summary>
    /// <remarks>
    /// На этой дороге стоит каскад перезагрузки: документы плагина закрываются перед его выгрузкой,
    /// и брошенное оттуда исключение обрывало каскад на середине — задачи уже остановлены, хост
    /// никого не поднял.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_view_that_falls_on_closing_does_not_stop_the_others()
    {
        var guard = new PluginGuard();
        var failures = new List<PluginFailure>();

        guard.Failed += (_, failure) => failures.Add(failure);

        var stubborn = new StubbornView();
        var first = true;

        var (documents, _, _) = Studio(
            _ => new EditorMatch(new ProbeEditor(first ? (stubborn, null) : null), "arxis.designer"), guard);

        await documents.OpenAsync(Form);

        first = false;

        await documents.OpenAsync(Other);

        var second = Probe(documents.Opened[1]);

        await documents.CloseOwnedByAsync("arxis.designer");

        Assert.Empty(documents.Opened);
        Assert.True(second.Disposed, "второй документ обязан закрыться, хотя первый упал");

        Assert.Contains(failures, failure =>
            failure.PluginId == "arxis.designer" && failure.What.StartsWith("закрытие", StringComparison.Ordinal));
    }

    /// <summary>Документ отключённого за сбои плагина всё равно отпускают: его ждёт выгрузка.</summary>
    [AvaloniaFact]
    public async Task A_document_of_a_disabled_plugin_is_still_released()
    {
        var guard = new PluginGuard();
        var (documents, _, _) = Studio(guard: guard);

        await documents.OpenAsync(Form);

        var view = Probe(documents.Opened[0]);

        for (var failure = 0; failure < PluginGuard.FailureLimit; failure++)
            guard.Report("arxis.designer", "проба", new InvalidOperationException("сломалось"));

        await documents.CloseOwnedByAsync("arxis.designer");

        Assert.True(view.Disposed, "прощание обязано дойти и до отключённого");
    }

    private static string Text(string key) => Localizer.Instance[key];

    /// <summary>Представление документа за записью о нём.</summary>
    /// <summary>
    /// Редактор, показавший файл у себя, вкладки не получает: так дизайнер в режиме доски показывает форму
    /// на своём холсте.
    /// </summary>
    [AvaloniaFact]
    public async Task An_editor_that_shows_the_file_in_its_own_panel_opens_no_tab()
    {
        var editor = new RevealingEditor(reveals: true);
        var (documents, dock, _) = Studio(_ => new EditorMatch(editor, "arxis.designer"));

        await documents.OpenAsync(Form);

        Assert.Empty(documents.Opened);
        Assert.DoesNotContain(StudioDocuments.Name(Form), dock.Items.Known());
        Assert.Equal([Form], editor.Revealed);
        Assert.Equal(0, editor.Opened);
    }

    /// <summary>
    /// Редактор, отказавшийся показывать у себя, открывает файл вкладкой, как раньше.
    /// </summary>
    [AvaloniaFact]
    public async Task An_editor_that_declines_to_show_the_file_itself_opens_a_tab()
    {
        var editor = new RevealingEditor(reveals: false);
        var (documents, _, _) = Studio(_ => new EditorMatch(editor, "arxis.designer"));

        await documents.OpenAsync(Form);

        Assert.Single(documents.Opened);
        Assert.Equal([Form], editor.Revealed);
        Assert.Equal(1, editor.Opened);
    }

    /// <summary>
    /// Показ, упавший у редактора, записывается на его плагин, а файл открывается вкладкой: человек просил
    /// открыть файл, и сбой показа не оставляет его ни с чем.
    /// </summary>
    [AvaloniaFact]
    public async Task A_reveal_that_falls_is_charged_and_the_file_opens_as_a_tab()
    {
        var guard = new PluginGuard();
        var failures = new List<PluginFailure>();

        guard.Failed += (_, failure) => failures.Add(failure);

        var (documents, _, _) = Studio(_ => new EditorMatch(new RevealingEditor(falls: true), "arxis.designer"), guard);

        await documents.OpenAsync(Form);

        Assert.Single(documents.Opened);

        var failure = Assert.Single(failures);

        Assert.Equal("arxis.designer", failure.PluginId);
        Assert.Equal("показ упал после ожидания", failure.Message);
    }

    /// <summary>
    /// Документ, открытый на месте панели с кареткой, получает каретку — туда, где в нём работают.
    /// </summary>
    /// <remarks>
    /// Так открывают форму с доски дизайнера: доска стоит в области документов, и вкладка встаёт поверх
    /// неё. Каретка была на доске, доска ушла за вкладку, и клавиатура оставалась ни у кого, пока человек
    /// не щёлкнет мышью. Цель документа при этом обязана быть известна раньше, чем встанет вкладка: иначе
    /// каретка уходит первому внутри.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_document_opened_in_place_of_the_panel_with_the_caret_takes_it()
    {
        var (documents, dock, _) = Studio(_ => new EditorMatch(new AimedEditor(), "arxis.designer"));

        dock.Add("arxis.ui-designer", Board, new PluginPlacement { Side = "center" }, "Дизайнер", PluginStrings.Studio, Focusable());
        Dispatcher.UIThread.RunJobs();

        Assert.True(dock.Focus(Board), "на доске некому взять каретку");

        await documents.OpenAsync(Form);

        Assert.True(Aimed(documents.Opened[0]).Aim.IsFocused, "вкладка, вставшая на место доски с кареткой, осталась без неё");
    }

    /// <summary>
    /// Открытый документ, выведенный вперёд на место панели с кареткой, получает её там, где её оставили.
    /// </summary>
    /// <remarks>
    /// Вкладка формы уже открыта за доской, и Enter на доске выводит её вперёд: та же просьба «открой», и
    /// клавиатура терялась так же.
    /// </remarks>
    [AvaloniaFact]
    public async Task An_open_document_brought_forward_in_place_of_the_panel_with_the_caret_takes_it_back()
    {
        var (documents, dock, _) = Studio(_ => new EditorMatch(new AimedEditor(), "arxis.designer"));

        dock.Add("arxis.ui-designer", Board, new PluginPlacement { Side = "center" }, "Дизайнер", PluginStrings.Studio, Focusable());
        Dispatcher.UIThread.RunJobs();

        await documents.OpenAsync(Form);
        Dispatcher.UIThread.RunJobs();

        var form = Aimed(documents.Opened[0]);

        // В документе работали не с цели, а с первого места — туда каретка и вернётся.
        Assert.True(form.First.Focus(), "в документе некому взять каретку");
        Assert.True(dock.Focus(Board), "на доске некому взять каретку");

        await documents.OpenAsync(Form);

        Assert.True(form.First.IsFocused, "документ, выведенный вперёд на место доски с кареткой, остался без неё");
    }

    /// <summary>Документ, открытый вдали от каретки, её не трогает: она остаётся в панели, где работали.</summary>
    /// <remarks>
    /// Так открывают файл из окна проекта: окно стоит сбоку, и вкладка встаёт в области документов, ничего в
    /// нём не закрыв. Каретка, уведённая оттуда, — потерянное нажатие человека, листавшего дерево.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_document_opened_away_from_the_caret_leaves_it_where_it_was()
    {
        var (documents, dock, _) = Studio(_ => new EditorMatch(new AimedEditor(), "arxis.designer"));
        var tree = Focusable();

        dock.Add("arxis.project", "arxis.project:tree", new PluginPlacement { Side = "left" }, "Проект", PluginStrings.Studio, tree);
        Dispatcher.UIThread.RunJobs();

        Assert.True(dock.Focus("arxis.project:tree"), "в окне проекта некому взять каретку");

        await documents.OpenAsync(Form);

        Assert.True(DockFocus.Holds(tree), "открытый документ увёл каретку из окна проекта");
    }

    /// <summary>Доска дизайнера — панель в области документов.</summary>
    private const string Board = "arxis.ui-designer:board";

    /// <summary>Панель, внутри которой есть куда встать каретке.</summary>
    private static Control Focusable() =>
        new StackPanel { Children = { new Border { Focusable = true, Height = 20 } } };

    private static AimedView Aimed(OpenDocument document) => Assert.IsType<AimedView>(document.View);

    private static ProbeView Probe(OpenDocument document) => Assert.IsType<ProbeView>(document.View);

    /// <summary>
    /// Служба над живой раскладкой в показанном окне.
    /// </summary>
    /// <param name="editorFor">
    /// Кто берётся за файл; по умолчанию — один редактор на всё, от плагина
    /// «arxis.designer».
    /// </param>
    /// <remarks>
    /// Раскладка настоящая, а не заглушка: половина проверяемых правил — про
    /// согласие с ней («сосед занял место», «выбор вкладки показал документ»),
    /// и с заглушкой они доказывали бы согласие с самими собой.
    /// </remarks>
    private static (StudioDocuments Documents, StudioDock Dock, IReadOnlyCollection<string> Status) Studio(
        Func<string, EditorMatch?>? editorFor = null,
        PluginGuard? guard = null)
    {
        var view = new DockView();
        var dock = new StudioDock(view);

        new Window { Content = view, Width = 1200, Height = 800 }.Show();
        Dispatcher.UIThread.RunJobs();

        var editor = new ProbeEditor();
        var sink = new StatusProbe();

        return (new StudioDocuments(dock, editorFor ?? (_ => new EditorMatch(editor, "arxis.designer")), sink, guard),
            dock, sink.Said);
    }

    /// <summary>Редактор, падающий на открытии — уже после первого ожидания.</summary>
    private sealed class FallingEditor : DocumentEditor
    {
        /// <inheritdoc/>
        public override bool CanOpen(string filePath) => true;

        /// <inheritdoc/>
        public override async Task<(DocumentView? View, string? Error)> OpenAsync(string filePath)
        {
            await Task.Yield();

            throw new InvalidOperationException("редактор упал после ожидания");
        }
    }

    /// <summary>Редактор, умеющий показать файл у себя: показывает, отказывается или падает.</summary>
    /// <param name="reveals">Показывать ли у себя.</param>
    /// <param name="falls">Падать на показе — уже после первого ожидания.</param>
    private sealed class RevealingEditor(bool reveals = false, bool falls = false) : DocumentEditor
    {
        /// <summary>Какие файлы просили показать.</summary>
        public List<string> Revealed { get; } = [];

        /// <summary>Сколько раз открывал вкладкой.</summary>
        public int Opened { get; private set; }

        /// <inheritdoc/>
        public override bool CanOpen(string filePath) => true;

        /// <inheritdoc/>
        public override Task<(DocumentView? View, string? Error)> OpenAsync(string filePath)
        {
            Opened++;

            return Task.FromResult<(DocumentView?, string?)>((new ProbeView(filePath), null));
        }

        /// <inheritdoc/>
        public override async Task<bool> RevealAsync(string filePath)
        {
            await Task.Yield();

            if (falls)
                throw new InvalidOperationException("показ упал после ожидания");

            Revealed.Add(filePath);

            return reveals;
        }
    }

    /// <summary>Редактор документов с целью каретки.</summary>
    private sealed class AimedEditor : DocumentEditor
    {
        /// <inheritdoc/>
        public override bool CanOpen(string filePath) => true;

        /// <inheritdoc/>
        public override Task<(DocumentView? View, string? Error)> OpenAsync(string filePath) =>
            Task.FromResult<(DocumentView?, string?)>((new AimedView(filePath), null));
    }

    /// <summary>Документ, в котором работают не с первого места, а с названного — как с холста формы.</summary>
    private sealed class AimedView : DocumentView
    {
        public AimedView(string filePath)
        {
            Title = Path.GetFileName(filePath);
            Content = new StackPanel { Children = { First, Aim } };
        }

        /// <summary>Первое место, где может встать каретка.</summary>
        public Border First { get; } = new() { Focusable = true, Height = 20 };

        /// <summary>Место, с которого в документе работают, — его цель каретки.</summary>
        public Border Aim { get; } = new() { Focusable = true, Height = 20 };

        /// <inheritdoc/>
        public override Control Content { get; }

        /// <inheritdoc/>
        public override string Title { get; }

        /// <inheritdoc/>
        public override Control? FocusTarget => Aim;
    }

    /// <summary>Представление, падающее на закрытии.</summary>
    private sealed class StubbornView : DocumentView
    {
        /// <inheritdoc/>
        public override Control Content { get; } = new Border();

        /// <inheritdoc/>
        public override string Title => "Упрямый";

        /// <inheritdoc/>
        public override ValueTask DisposeAsync() => throw new InvalidOperationException("не закроюсь");
    }

    /// <summary>Редактор-пустышка: открывает всё, чем его попросят.</summary>
    /// <param name="answer">Что отвечать на открытие; по умолчанию — свежее представление.</param>
    private sealed class ProbeEditor((DocumentView? View, string? Error)? answer = null) : DocumentEditor
    {
        /// <inheritdoc/>
        public override bool CanOpen(string filePath) => true;

        /// <inheritdoc/>
        public override Task<(DocumentView? View, string? Error)> OpenAsync(string filePath) =>
            Task.FromResult(answer ?? (new ProbeView(filePath), null));
    }

    /// <summary>Представление-пустышка: считает, что с ним делали.</summary>
    private sealed class ProbeView(string filePath) : DocumentView
    {
        /// <inheritdoc/>
        public override Control Content { get; } = new Border();

        /// <inheritdoc/>
        public override string Title { get; } = Path.GetFileName(filePath);

        /// <summary>Сколько раз документ становился показанным.</summary>
        public int Activated { get; private set; }

        /// <summary>Сколько раз его сменяли другим.</summary>
        public int Deactivated { get; private set; }

        /// <summary>Отпустили ли его.</summary>
        public bool Disposed { get; private set; }

        /// <inheritdoc/>
        public override void OnActivated() => Activated++;

        /// <inheritdoc/>
        public override void OnDeactivated() => Deactivated++;

        /// <inheritdoc/>
        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
