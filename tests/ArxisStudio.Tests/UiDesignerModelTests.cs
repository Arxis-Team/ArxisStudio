using System.Text;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Модель доски форм без окна: что считается формой, где встают карточки и как места живут в файле.
/// </summary>
public class UiDesignerModelTests
{
    /// <summary>
    /// Окно, пользовательский элемент и контрол узнаются по корню, а класс и размер читаются из него.
    /// </summary>
    [Fact]
    public void The_root_tells_the_kind_class_and_size_of_a_form()
    {
        var window = Root(UiDesignerStudio.WindowXaml("MainWindow"))!;
        var control = Root(UiDesignerStudio.ControlXaml("Card"))!;
        var styled = Root("<ax:AxWindow xmlns:ax='https://github.com/Arxis-Team/ArxisStudio' Width='Auto'/>")!;
        var templated = Root("<TemplatedControl xmlns='https://github.com/avaloniaui'/>")!;

        Assert.Equal((FormKind.Window, "App.Views.MainWindow", 800d, 450d), (window.Kind, window.ClassName, window.Width, window.Height));
        Assert.Equal((FormKind.UserControl, 320d, 200d), (control.Kind, control.Width, control.Height));
        Assert.Equal(FormKind.Window, styled.Kind);
        Assert.False(styled.HasSize, "Auto — не размер, а у формы без размера нет и строки размера");
        Assert.Equal(FormKind.Control, templated.Kind);
    }

    /// <summary>Приложение и словари стилей и ресурсов формами не бывают.</summary>
    [Theory]
    [InlineData(UiDesignerStudio.ApplicationXaml)]
    [InlineData("<Styles xmlns='https://github.com/avaloniaui'/>")]
    [InlineData("<ResourceDictionary xmlns='https://github.com/avaloniaui'/>")]
    public void Application_and_dictionaries_are_not_forms(string markup) => Assert.Null(Root(markup));

    /// <summary>
    /// Разметка, у которой не читается корень, остаётся на доске нечитаемой карточкой с причиной, а
    /// сущность DTD не раскрывается.
    /// </summary>
    /// <remarks>
    /// Читается один корень: ошибка глубже него — дело редактора разметки, и окно с незакрытым
    /// элементом внутри остаётся на доске окном.
    /// </remarks>
    [Fact]
    public void Markup_with_an_unreadable_root_is_unreadable_and_a_dtd_is_refused()
    {
        var broken = Root("<Window Width=");
        var text = Root("not markup at all");
        var deeper = Root("<Window><Grid></Window>");
        var dtd = Root("<!DOCTYPE Window [<!ENTITY a 'aaaa'>]><Window>&a;</Window>");

        Assert.Equal(FormKind.Unreadable, broken?.Kind);
        Assert.False(string.IsNullOrEmpty(broken?.Problem), "у нечитаемой карточки нет причины");
        Assert.Equal(FormKind.Unreadable, text?.Kind);
        Assert.Equal(FormKind.Window, deeper?.Kind);
        Assert.Equal(FormKind.Unreadable, dtd?.Kind);
    }

    /// <summary>Сетка почти квадратная: тридцать карточек — шесть столбцов, а не лента.</summary>
    [Fact]
    public void The_grid_is_nearly_square_and_goes_row_by_row()
    {
        var spots = BoardLayout.Grid(5, new Spot(20, 40), new Pitch(280, 160));

        Assert.Equal(6, BoardLayout.Columns(30));
        Assert.Equal(1, BoardLayout.Columns(0));
        Assert.Equal(
            [new Spot(20, 40), new Spot(300, 40), new Spot(580, 40), new Spot(20, 200), new Spot(300, 200)],
            spots);
    }

    /// <summary>Новые карточки встают рядами под самой нижней из поставленных, от левого края доски.</summary>
    [Fact]
    public void New_cards_go_below_the_placed_ones()
    {
        var pitch = new Pitch(280, 160);

        Assert.Equal([new Spot(0, 0), new Spot(280, 0)], BoardLayout.Below([], 2, pitch));
        Assert.Equal(
            [new Spot(-100, 460)],
            BoardLayout.Below([new Spot(-100, 0), new Spot(400, 300)], 1, pitch));
    }

    /// <summary>
    /// Места переживают запись и чтение: ключи — пути от папки решения через прямую черту, по порядку.
    /// </summary>
    [Fact]
    public void Places_survive_a_write_and_a_read()
    {
        var root = TempFolder.Create("ui-designer-file");

        try
        {
            var folder = CanonicalPath.Create(root);
            var file = Path.Combine(root, ".arxis", "ui-designer", "board.json");
            var main = folder.Combine("src/App/Views/MainWindow.axaml");
            var card = folder.Combine("src/App/Views/Card.axaml");

            BoardFile.Write(file, folder, new BoardData(
                new Dictionary<CanonicalPath, Spot>
                {
                    [main] = new Spot(280.004, -160),
                    [card] = new Spot(0, 0),
                },
                []));

            var text = File.ReadAllText(file);

            Assert.True(
                text.IndexOf("src/App/Views/Card.axaml", StringComparison.Ordinal)
                < text.IndexOf("src/App/Views/MainWindow.axaml", StringComparison.Ordinal),
                text);
            Assert.DoesNotContain("\\\\", text);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.tmp"));

            var read = BoardFile.Read(file, folder);

            Assert.Equal(new Spot(280, -160), read.Spots[main]);
            Assert.Equal(new Spot(0, 0), read.Spots[card]);
            Assert.Empty(read.Removed);
            Assert.DoesNotContain("removed", text);
        }
        finally
        {
            TempFolder.Erase(root);
        }
    }

    /// <summary>Испорченный файл не мешает открыть доску: мест просто нет, а чужие записи пропускаются.</summary>
    [Fact]
    public void A_spoiled_file_gives_no_places_instead_of_failing()
    {
        var root = TempFolder.Create("ui-designer-spoiled");

        try
        {
            var folder = CanonicalPath.Create(root);
            var file = Path.Combine(root, "board.json");

            File.WriteAllText(file, "{ \"forms\": { \"a.axaml\": { \"x\": 1 ");
            Assert.Empty(BoardFile.Read(file, folder).Spots);

            File.WriteAllText(file, "{ \"forms\": { \"a.axaml\": { \"x\": \"left\", \"y\": 0 }, \"b.axaml\": { \"x\": 4, \"y\": 8 } }, \"removed\": [ 7, \"c.axaml\" ] }");
            Assert.Equal([new Spot(4, 8)], BoardFile.Read(file, folder).Spots.Values);
            Assert.Equal([folder.Combine("c.axaml")], BoardFile.Read(file, folder).Removed);

            Assert.Empty(BoardFile.Read(Path.Combine(root, "missing.json"), folder).Spots);
        }
        finally
        {
            TempFolder.Erase(root);
        }
    }

    /// <summary>
    /// Формы — всё <c>.axaml</c>, что перечисляют проекты; общий файл двух проектов — одна карточка.
    /// </summary>
    [Fact]
    public void Forms_are_the_markup_the_projects_list_once_each()
    {
        var solution = new ProjectWindowSolution("Forms");
        var app = solution.Project("App");
        var lib = solution.Project("Lib");
        var shared = solution.File(app, "Views/Shared.AXAML", "AvaloniaXaml");

        solution.File(app, "Views/MainWindow.axaml", "AvaloniaXaml");
        solution.File(app, "Views/MainWindow.axaml.cs");
        solution.File(app, "Assets/logo.png", "AvaloniaResource");
        solution.Linked(lib, shared, "Shared.axaml");

        var forms = FormFiles.Of(solution.ToSnapshot());

        Assert.Equal(
            [("MainWindow.axaml", "App"), ("Shared.AXAML", "App")],
            forms.Select(form => (form.Path.FileName, form.Project)));
    }

    private static FormRoot? Root(string markup) => FormRoot.Read(new MemoryStream(Encoding.UTF8.GetBytes(markup)));
}
