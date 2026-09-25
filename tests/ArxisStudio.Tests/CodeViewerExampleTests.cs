using System.Text.Json;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Пробный просмотрщик — Arxis.CodeViewer, единственный редактор документов в репозитории.
/// </summary>
/// <remarks>
/// Дорога «щёлкнули по файлу — открылась вкладка» до него проверялась одними
/// заглушками: своего редактора документов в репозитории не было, и весь
/// контракт <see cref="DocumentEditor"/> держался на подделках теста. Здесь он
/// проверяется целиком и настоящим плагином — тем самым, что лежит на диске:
/// манифест, пробуждение по типу файла, загрузка сборки со своей зависимостью,
/// отбор редактора, чтение, вкладка и текст в ней.
/// <para>
/// Плагин ставится из своей раскладки во временную папку, как его ставит
/// менеджер. Подделать здесь нечего: поддельный манифест доказывал бы согласие
/// студии с выдумкой теста.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class CodeViewerExampleTests : IDisposable
{
    private const string Id = "arxis.code-viewer";

    private readonly string _root = TempFolder.Create("viewer");
    private readonly string _files = TempFolder.Create("viewer-files");
    private readonly StudioPluginsHarness _studio = new();

    public CodeViewerExampleTests()
    {
        // Язык закрепляется: отказы просмотрщик говорит своими строками, а
        // взять их он может только из словаря текущего языка.
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);

        _studio.Show(1200, 800);
    }

    public void Dispose()
    {
        // Хост отпускается первым: пока жив контекст загрузки плагина, его
        // файлы держит процесс, и папку не убрать.
        _studio.Dispose();

        GC.Collect();
        GC.WaitForPendingFinalizers();

        TempFolder.Erase(_root);
        TempFolder.Erase(_files);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Файл, который прежде было открыть некому, открывается во вкладке.
    /// </summary>
    /// <remarks>
    /// Проверка конца в конец: до открытия плагин спит, и студия о его
    /// редакторе не знает вовсе — будит его тип файла из манифеста. После
    /// открытия во вкладке стоит текст файла, прочитанный самим плагином.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_file_nobody_could_open_opens_in_the_viewer()
    {
        var plugins = Start();
        var file = Write("Окно.axaml", "<Window xmlns=\"https://github.com/avaloniaui\"/>");

        Assert.Empty(plugins.Reloadable);

        await _studio.Documents.OpenAsync(file);

        Assert.Contains(plugins.Reloadable, plugin => plugin.Id == Id);

        var open = Assert.Single(_studio.Documents.Opened);

        Assert.Equal(Id, open.PluginId);
        Assert.Equal(StudioDocuments.Name(file), _studio.Dock.Showing);
        Assert.Equal("Окно.axaml", open.View.Title);
        Assert.Equal(File.ReadAllText(file), Shown());
    }

    /// <summary>
    /// Тип, которого просмотрщик не заявлял, его не будит.
    /// </summary>
    /// <remarks>
    /// Спящий плагин — такой же житель студии, как поднятый, и обходится он
    /// даром ровно до тех пор, пока его не будит что попало. Архив открывать
    /// по-прежнему некому, и студия говорит об этом теми же словами.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_kind_the_viewer_never_claimed_wakes_nobody()
    {
        var plugins = Start();

        await _studio.Documents.OpenAsync(Write("Архив.zip", "PK"));

        Assert.Empty(plugins.Reloadable);
        Assert.Empty(_studio.Documents.Opened);
        Assert.Equal(Localizer.Instance["editor.noeditor"], _studio.Status.Last);
    }

    /// <summary>
    /// Файл крупнее предела получает отказ с причиной, а не вкладку с ожиданием.
    /// </summary>
    /// <remarks>
    /// Причину говорит сам плагин: студия несёт её человеку, ничего о ней не
    /// зная. Это вторая половина контракта открытия — та, ради которой
    /// <c>OpenAsync</c> возвращает пару, а не одно представление.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_file_too_big_for_the_viewer_is_refused_with_a_reason()
    {
        Start();

        var file = Write("Толстый.txt", new string('т', 600 * 1024));

        await _studio.Documents.OpenAsync(file);

        Assert.Empty(_studio.Documents.Opened);
        Assert.Null(_studio.Documents.Shown);
        Assert.Contains("512", _studio.Status.Last, StringComparison.Ordinal);
    }

    /// <summary>
    /// Двоичный файл со знакомым расширением отказывается словами плагина.
    /// </summary>
    /// <remarks>
    /// Слова берутся из словаря плагина, а сверяются с тем, что лежит в его
    /// раскладке: так проверяется вся дорога строк расширения — папка
    /// <c>lang/</c> рядом с манифестом, язык студии и <c>Context.Strings</c>.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_file_that_is_not_text_is_refused_in_the_viewers_own_words()
    {
        Start();

        var file = Write("Битый.json", "{\"текст\": \"\0\"}");

        await _studio.Documents.OpenAsync(file);

        Assert.Empty(_studio.Documents.Opened);
        Assert.EndsWith(Say("viewer.binary"), _studio.Status.Last, StringComparison.Ordinal);
    }

    /// <summary>
    /// Подсветка раскрашена ролями темы, а не палитрой пакета.
    /// </summary>
    /// <remarks>
    /// Проверяется весь набор подсветки, а не видимая строка: цвет в нём бывает
    /// объявлен и внутри правила, мимо списка именованных, — так покрашены
    /// пометки <c>TODO</c> и текст XML-комментария. Файл взят разметочный:
    /// набор MarkDown везёт с собой и чужой кегль заголовка, и гарнитуру блока
    /// кода, и подложку переноса строки, и весь набор C# в придачу — всё, чего
    /// в студии быть не должно.
    /// <para>
    /// Тема переключается при открытой вкладке: палитра обязана пойти за ней, а
    /// не покрасить однажды при открытии. Тёмная здесь ещё и различает роли —
    /// в светлой цвет кода и цвет текста студии совпадают, и подмена одного
    /// другим прошла бы незамеченной.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task The_highlighting_is_painted_with_the_themes_own_roles()
    {
        Start();

        await _studio.Documents.OpenAsync(Write("Заметка.md", "# Заголовок\n\n    код\n\n`строка`\n"));

        var application = Assert.IsAssignableFrom<Application>(Application.Current);
        var was = application.RequestedThemeVariant;

        try
        {
            application.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();

            var theme = Palette();
            var colours = Painted();
            var used = new HashSet<Color>();

            Assert.NotEmpty(colours);

            foreach (var colour in colours)
            {
                var name = Read(colour, "Name") as string ?? "без имени";

                Assert.True(Read(colour, "Background") is null, $"у цвета {name} осталась чужая подложка");
                Assert.True(Read(colour, "FontSize") is null, $"у цвета {name} остался чужой кегль");
                Assert.True(Read(colour, "FontFamily") is null, $"у цвета {name} осталась чужая гарнитура");

                if (Foreground(colour) is not { } painted)
                    continue;

                Assert.True(theme.Contains(painted), $"цвет {name} покрашен мимо темы: {painted}");

                used.Add(painted);
            }

            // Обратная половина: не только чужого не осталось, но и своё в ходу —
            // иначе подсветка, покрашенная пустотой, прошла бы проверку выше.
            Assert.True(theme.SetEquals(used), $"роли темы не все в ходу: {string.Join(", ", theme.Except(used))}");

            // Непокрашенное берёт цвет редактора, и цвет этот — та же роль темы,
            // кодовая, а не общий текст студии.
            Assert.Equal(
                Colour("AxCodeTextColor"),
                Assert.IsAssignableFrom<ISolidColorBrush>(Read(Editor(), "Foreground")).Color);
        }
        finally
        {
            application.RequestedThemeVariant = was;
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Цвета ролей кода, как их объявила тема.</summary>
    private static HashSet<Color> Palette()
    {
        var colours = new[] { "AxCodeTagColor", "AxCodeAttributeColor", "AxCodeStringColor", "AxCodeCommentColor" }
            .Select(Colour)
            .ToHashSet();

        Assert.Equal(4, colours.Count);

        return colours;
    }

    /// <summary>Цвет роли, как его объявила тема текущего варианта.</summary>
    private static Color Colour(string key)
    {
        var application = Assert.IsAssignableFrom<Application>(Application.Current);

        Assert.True(
            application.TryGetResource(key, application.ActualThemeVariant, out var value),
            $"в теме нет ключа {key}");

        return Assert.IsType<Color>(value);
    }

    /// <summary>
    /// Все цвета набора подсветки открытого документа.
    /// </summary>
    /// <remarks>
    /// Через отражение: типы AvaloniaEdit живут в контексте загрузки плагина, и
    /// тесту они не видны — ровно так же, как студии.
    /// </remarks>
    private IReadOnlyList<object> Painted()
    {
        var definition = Read(Editor(), "SyntaxHighlighting");

        Assert.True(definition is not null, "у документа нет набора подсветки");

        var found = new List<object>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<object>();

        void Take(object? colour)
        {
            if (colour is not null && seen.Add(colour))
                found.Add(colour);
        }

        void Reach(object? set)
        {
            if (set is not null && seen.Add(set))
                queue.Enqueue(set);
        }

        foreach (var colour in Assert.IsAssignableFrom<System.Collections.IEnumerable>(Read(definition!, "NamedHighlightingColors")))
            Take(colour);

        Reach(Read(definition!, "MainRuleSet"));

        while (queue.Count > 0)
        {
            var set = queue.Dequeue();

            foreach (var rule in Assert.IsAssignableFrom<System.Collections.IEnumerable>(Read(set, "Rules")))
                Take(Read(rule, "Color"));

            foreach (var span in Assert.IsAssignableFrom<System.Collections.IEnumerable>(Read(set, "Spans")))
            {
                Take(Read(span, "StartColor"));
                Take(Read(span, "SpanColor"));
                Take(Read(span, "EndColor"));

                Reach(Read(span, "RuleSet"));
            }
        }

        return found;
    }

    /// <summary>Цвет, которым покрашен элемент подсветки; пусто — цвет редактора.</summary>
    private static Color? Foreground(object colour)
    {
        if (Read(colour, "Foreground") is not { } brush)
            return null;

        var got = brush.GetType().GetMethod("GetBrush")?.Invoke(brush, [null]);

        return Assert.IsAssignableFrom<ISolidColorBrush>(got).Color;
    }

    /// <summary>Значение открытого свойства по имени.</summary>
    private static object? Read(object owner, string property)
    {
        var found = owner.GetType().GetProperty(property);

        Assert.True(found is not null, $"у {owner.GetType().Name} нет свойства {property}");

        return found!.GetValue(owner);
    }

    /// <summary>Поднимает студию с одним установленным плагином — просмотрщиком.</summary>
    /// <remarks>
    /// Плагин ставится из архива, тем же каталогом, каким его ставит менеджер
    /// плагинов: так проверяется то, что получит человек, а не раскладка,
    /// которую он в руки не берёт.
    /// </remarks>
    private StudioPlugins Start()
    {
        Assert.Null(new PluginCatalog(_root).InstallFromArchive(Archive()).Error);

        // Папка плагинов — своя на тест: настоящая принадлежит человеку, и прогон,
        // читающий её, отвечал бы по-разному на разных машинах.
        var plugins = _studio.Build(catalog: () => new PluginCatalog(_root).Scan());

        plugins.Start();
        Dispatcher.UIThread.RunJobs();

        return plugins;
    }

    /// <summary>Текст, который стоит в показанной вкладке.</summary>
    /// <remarks>
    /// Редактор ищется по имени типа, а не приведением: сборка плагина живёт в
    /// своём контексте загрузки, и типы из неё тесту не видны — ни AvaloniaEdit,
    /// ни его собственные. Так же их видит и студия.
    /// </remarks>
    private string Shown() => Assert.IsType<string>(Read(Editor(), "Text"));

    /// <summary>Редактор AvaloniaEdit, стоящий в показанной вкладке.</summary>
    private Control Editor()
    {
        var content = Assert.IsAssignableFrom<Control>(_studio.Documents.Shown?.Content);

        Dispatcher.UIThread.RunJobs();

        var editor = content
            .GetLogicalDescendants()
            .OfType<Control>()
            .FirstOrDefault(child => child.GetType().Name == "TextEditor");

        Assert.True(editor is not null, "во вкладке нет редактора AvaloniaEdit");

        return editor!;
    }

    /// <summary>Строка из словаря плагина, лежащего в его раскладке.</summary>
    private static string Say(string key)
    {
        using var file = File.OpenRead(
            Path.Combine(Repository.Path("src", "Plugins", "Arxis.CodeViewer", "package"), "lang", $"{Localizer.FallbackLanguage}.json"));

        return JsonDocument.Parse(file).RootElement.GetProperty(key).GetString()!;
    }

    /// <summary>Кладёт файл во временную папку и отдаёт путь к нему.</summary>
    private string Write(string name, string text)
    {
        var path = Path.Combine(_files, name);

        File.WriteAllText(path, text);

        return path;
    }

    /// <summary>Архив просмотрщика — настоящий <c>.axplugin</c>, собранный сборкой решения.</summary>
    /// <remarks>Лежит рядом с раскладкой: оба оставляет таргет упаковки.</remarks>
    private static string Archive()
    {
        var archive = Path.Combine(Path.GetDirectoryName(Repository.Path("src", "Plugins", "Arxis.CodeViewer", "package"))!, $"{Id}.axplugin");

        Assert.True(File.Exists(archive), $"сборка не оставила архива {Id}.axplugin");

        return archive;
    }
}
