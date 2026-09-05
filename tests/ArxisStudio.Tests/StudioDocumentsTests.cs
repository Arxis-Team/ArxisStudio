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
        Assert.Equal(@"C:\проект\Окно.axaml", status[^1]);
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
        Assert.Equal(Text("editor.noeditor"), status[^1]);
    }

    /// <summary>Редактор не справился — человек узнаёт причину, а не пустую вкладку.</summary>
    [AvaloniaFact]
    public async Task A_file_that_fails_to_load_says_why()
    {
        var editor = new ProbeEditor((null, "файл побит"));
        var (documents, _, status) = Studio(editorFor: _ => new EditorMatch(editor, "arxis.designer"));

        await documents.OpenAsync(@"C:\проект\Окно.axaml");

        Assert.Empty(documents.Opened);
        Assert.Contains("файл побит", status[^1]);
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
    private static string Text(string key) => Localizer.Instance[key];

    /// <summary>Представление документа за записью о нём.</summary>
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
    private static (StudioDocuments Documents, StudioDock Dock, IReadOnlyList<string> Status) Studio(
        Func<string, EditorMatch?>? editorFor = null)
    {
        var view = new DockView();
        var dock = new StudioDock(view);

        new Window { Content = view, Width = 1200, Height = 800 }.Show();
        Dispatcher.UIThread.RunJobs();

        var editor = new ProbeEditor();
        var sink = new Sink();

        return (new StudioDocuments(dock, editorFor ?? (_ => new EditorMatch(editor, "arxis.designer")), sink),
            dock, sink.Said);
    }

    /// <summary>Строка состояния, которая всё записывает.</summary>
    private sealed class Sink : IStudioStatus
    {
        /// <summary>Что студия сказала, по порядку.</summary>
        public List<string> Said { get; } = [];

        /// <inheritdoc/>
        public void Show(string message) => Said.Add(message);
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
