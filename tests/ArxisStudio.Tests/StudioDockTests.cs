using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Раскладка студии: как панели плагинов попадают в дерево доков.
/// </summary>
/// <remarks>
/// Очередь общая с остальными: заголовки панелей привязываются к словарям, а
/// <c>Localizer</c> один на процесс.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioDockTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"arxis-dock-{Guid.NewGuid():N}");

    public StudioDockTests() => Directory.CreateDirectory(_directory);

    private string File => Path.Combine(_directory, "layout.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Панель встаёт в объявленную сторону, вторая — вкладкой рядом.</summary>
    [AvaloniaFact]
    public void A_panel_takes_the_side_it_asked_for()
    {
        var (dock, view) = Dock();

        dock.Add("hello", "hello:tree", "left", "Проект", Strings, new Border());
        dock.Add("hello", "hello:outline", "left", "Структура", Strings, new Border());

        var left = DockTree.Group(view.Root!, "left");

        Assert.NotNull(left);
        Assert.Equal(["hello:tree", "hello:outline"], left.Items);
        Assert.Equal("hello:outline", left.Selected);
    }

    /// <summary>
    /// Пустая сторона места не занимает, но из дерева не уходит.
    /// </summary>
    /// <remarks>
    /// Стороны заведены заранее и с готовыми размерами. Показывать их пустыми
    /// незачем — студия без единого плагина показывает одну область
    /// документов, — но и сносить нельзя: пришедшая панель тогда делила бы
    /// пополам то, что подвернулось, вместо того чтобы встать на своё место.
    /// </remarks>
    [AvaloniaFact]
    public void An_empty_side_takes_no_room_but_stays_in_the_tree()
    {
        var (dock, view) = Dock();

        Assert.Equal([StudioDock.Documents], Shown(view));

        dock.Add("hello", "hello:tree", "left", "Проект", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["left", StudioDock.Documents], Shown(view));

        // Правая сторона и низ на экране не появились, а в дереве стоят.
        Assert.NotNull(DockTree.Group(view.Root!, "right"));
        Assert.NotNull(DockTree.Group(view.Root!, "bottom"));
    }

    /// <summary>Уход хозяина убирает его панели с экрана.</summary>
    [AvaloniaFact]
    public void The_owner_leaving_takes_its_panels_off_the_screen()
    {
        var (dock, view) = Dock();

        dock.Add("hello", "hello:tree", "left", "Проект", Strings, new Border());
        dock.Add("friend", "friend:tips", "right", "Советы", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        dock.RemoveOwnedBy("hello");
        Dispatcher.UIThread.RunJobs();

        Assert.Null(dock.Items.Find("hello:tree"));
        Assert.Equal(1, dock.Items.Count);
        Assert.Equal([StudioDock.Documents, "right"], Shown(view));
    }

    /// <summary>
    /// Панель возвращается ровно туда, где стояла.
    /// </summary>
    /// <remarks>
    /// Это и есть смысл того, что имена остаются в дереве. Выключил плагин и
    /// включил обратно — панель на своём месте, а не там, куда её отправил бы
    /// манифест; манифест спрашивают только про незнакомое имя.
    /// </remarks>
    [AvaloniaFact]
    public void A_panel_comes_back_exactly_where_it_stood()
    {
        var (dock, view) = Dock();

        dock.Add("hello", "hello:tree", "left", "Проект", Strings, new Border());
        dock.Add("hello", "hello:outline", "left", "Структура", Strings, new Border());
        dock.RemoveOwnedBy("hello");

        // Плагин подняли заново — и он снова просится влево, но его уже не спрашивают.
        dock.Add("hello", "hello:outline", "right", "Структура", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        var left = DockTree.Group(view.Root!, "left");

        Assert.NotNull(left);
        Assert.Equal(["hello:tree", "hello:outline"], left.Items);
        Assert.Equal(["left", StudioDock.Documents], Shown(view));
    }

    /// <summary>Документ открывается в области документов и становится выбранным.</summary>
    [AvaloniaFact]
    public void A_document_opens_where_documents_open()
    {
        var (dock, view) = Dock();

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        dock.Open("hello", "doc:b.axaml", "b.axaml", new Border());

        var documents = DockTree.Group(view.Root!, StudioDock.Documents);

        Assert.NotNull(documents);
        Assert.Equal(["doc:a.axaml", "doc:b.axaml"], documents.Items);
        Assert.Equal("doc:b.axaml", dock.Showing);

        dock.Show("doc:a.axaml");

        Assert.Equal("doc:a.axaml", dock.Showing);
    }

    /// <summary>
    /// Закрытый документ уходит совсем, а место для документов остаётся.
    /// </summary>
    /// <remarks>
    /// Закрытая вкладка — не выключенный плагин: возвращать её некуда и незачем,
    /// поэтому имя уходит из дерева. Область документов при этом не исчезает —
    /// иначе следующий документ появился бы неизвестно где.
    /// </remarks>
    [AvaloniaFact]
    public void A_closed_document_leaves_for_good_but_its_place_remains()
    {
        var (dock, view) = Dock();

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        dock.Remove("doc:a.axaml");
        Dispatcher.UIThread.RunJobs();

        var documents = DockTree.Group(view.Root!, StudioDock.Documents);

        Assert.NotNull(documents);
        Assert.Empty(documents.Items);
        Assert.Null(dock.Showing);
        Assert.Equal([StudioDock.Documents], Shown(view));
    }

    /// <summary>
    /// Незнакомая сторона всё равно даёт панели место — справа от документов.
    /// </summary>
    /// <remarks>
    /// Манифест пишет автор плагина, и слово в нём может быть любым. Отказать
    /// значило бы потерять панель молча; студия ставит её рядом с документами и
    /// оставляет человеку решать, где ей быть.
    /// </remarks>
    [AvaloniaFact]
    public void An_unknown_side_still_gets_a_place()
    {
        var (dock, view) = Dock();

        dock.Add("hello", "hello:odd", "нигде", "Странная", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("нигде", DockTree.Holder(view.Root!, "hello:odd")?.Id);
        Assert.Equal([StudioDock.Documents, "нигде"], Shown(view));
    }

    /// <summary>
    /// Ключ в заголовке переводится, обычный текст — нет.
    /// </summary>
    /// <remarks>
    /// Заголовок панели — единственный её текст, который показывает не автор, а
    /// студия: значит и переводить его при смене языка ей. Ключ узнаётся по
    /// процентам вокруг, всё остальное показывается как есть.
    /// </remarks>
    [AvaloniaFact]
    public void A_key_in_the_title_is_translated_and_plain_text_is_not()
    {
        var (dock, _) = Dock();

        dock.Add("hello", "hello:plain", "left", "Проект", Strings, new Border());
        dock.Add("hello", "hello:key", "left", "%panel.main%", Strings, new Border());

        Assert.Equal("Проект", dock.Items.Find("hello:plain")?.Title);

        var translated = dock.Items.Find("hello:key")?.Title;

        Assert.NotNull(translated);
        Assert.DoesNotContain("%", translated, StringComparison.Ordinal);
    }

    /// <summary>
    /// Раскладка переживает перезапуск студии.
    /// </summary>
    /// <remarks>
    /// И место панели помнится раньше, чем сама панель появится: плагин ещё не
    /// поднят, экран пуст, но группа за ним числится — и поднятый плагин встаёт
    /// туда, а не туда, куда просится его манифест.
    /// </remarks>
    [AvaloniaFact]
    public void The_layout_survives_a_restart()
    {
        var (dock, _) = Dock(new DockLayoutStore(File));

        dock.Add("hello", "hello:tree", "left", "Проект", Strings, new Border());
        dock.Flush();

        // Студию закрыли и открыли заново: тот же файл, новое окно.
        var (again, view) = Dock(new DockLayoutStore(File));

        again.Restore();

        var left = DockTree.Group(view.Root!, "left");

        Assert.NotNull(left);
        Assert.Equal(["hello:tree"], left.Items);
        Assert.Equal([StudioDock.Documents], Shown(view));

        // Плагин просится вправо — его не спрашивают.
        again.Add("hello", "hello:tree", "right", "Проект", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["left", StudioDock.Documents], Shown(view));
    }

    /// <summary>Нетронутая раскладка в файл не едет.</summary>
    /// <remarks>
    /// Иначе первый же запуск студии записывал бы скелет, ничего человеку не
    /// обещающий, — и потом объяснял бы, почему стандартная раскладка «уже
    /// сохранена».
    /// </remarks>
    [AvaloniaFact]
    public void An_untouched_layout_is_not_written()
    {
        var (dock, _) = Dock(new DockLayoutStore(File));

        dock.Flush();

        Assert.False(System.IO.File.Exists(File));
    }

    /// <summary>
    /// Место для документов берётся из файла, а не из имени по умолчанию.
    /// </summary>
    /// <remarks>
    /// Документы не выделены типом — выделен указатель, и хранится он вместе с
    /// раскладкой. Человек мог увести документы в другую группу, и следующий
    /// открытый файл обязан появиться там же.
    /// </remarks>
    [AvaloniaFact]
    public void The_place_for_documents_comes_from_the_file()
    {
        new DockLayoutStore(File).Save(new DockLayout
        {
            Active = DockLayout.DefaultName,
            Layouts = new Dictionary<string, DockWorkspace>(StringComparer.Ordinal)
            {
                [DockLayout.DefaultName] = new()
                {
                    DocumentHome = "centre",
                    Root = new DockGroup { Id = "centre" },
                },
            },
        });

        var (dock, view) = Dock(new DockLayoutStore(File));

        dock.Restore();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["centre"], Shown(view));

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());

        Assert.Equal(["doc:a.axaml"], ((DockGroup)view.Root!).Items);
        Assert.Equal("doc:a.axaml", dock.Showing);
    }

    private static PluginStrings Strings => PluginStrings.Studio;

    /// <summary>Имена групп, которые сейчас на экране, слева направо.</summary>
    private static IReadOnlyList<string> Shown(DockView view) =>
        [.. view.GetVisualDescendants().OfType<DockGroupView>().Select(group => group.Id)];

    private static (StudioDock Dock, DockView View) Dock(DockLayoutStore? store = null)
    {
        var view = new DockView();
        var dock = new StudioDock(view, store);

        new Window { Content = view, Width = 1200, Height = 800 }.Show();
        Dispatcher.UIThread.RunJobs();

        return (dock, view);
    }
}
