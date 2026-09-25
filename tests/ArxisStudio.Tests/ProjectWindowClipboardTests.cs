using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Dialogs;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Panels;
using ArxisStudio.Modules.Project.Tree;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;
using static ArxisStudio.Tests.ProjectWindowDialogs;

namespace ArxisStudio.Tests;

/// <summary>
/// Вырезать, копировать и вставить в окне проекта: свой буфер, буфер системы, конфликт имён.
/// </summary>
/// <remarks>
/// Буфер системы подделан (<see cref="SystemFilesProbe"/>): у безголового прогона его может не быть, а
/// проверять надо обе дороги вставки — своё вырезанное и файлы, положенные проводником.
/// </remarks>
public class ProjectWindowClipboardTests
{
    /// <summary>
    /// Вырезанное приглушено до вставки; вставка переносит его вместе с вложенным одним действием,
    /// выделение встаёт на вставленное, а буфер пустеет — вырезанное вставляется один раз.
    /// </summary>
    [AvaloniaFact]
    public async Task Cut_and_paste_moves_and_the_cut_is_dimmed_until_then()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var app = studio.Row("App.axaml");

        studio.Press(studio.Item(studio.Select("App.axaml")), Key.X, KeyModifiers.Control);

        Assert.True(app.IsCut, "вырезанное не приглушено");
        Assert.False(studio.Row("Program.cs").IsCut, "приглушено невырезанное");

        // Вложенный стоит под свёрнутым владельцем: строка появляется, когда ветку раскрыли, и
        // должна прийти приглушённой.
        studio.Model.Tree.Expand(app);
        Dispatcher.UIThread.RunJobs();

        Assert.True(studio.Row("App.axaml.cs").IsCut, "раскрытое после вырезания пришло не приглушённым");
        Assert.Contains(Format(studio, "project.clip.cut", "App.axaml"), studio.Status.Said);

        var held = Assert.IsType<FileClip>(studio.Clipboard.Held);

        Assert.Equal(ClipMode.Cut, held.Mode);
        Assert.Equal(["App.axaml", "App.axaml.cs"], held.Paths.Select(path => path.FileName));

        files.After = () => studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution(
            without: ["App.axaml", "App.axaml.cs"], more: ["Views/App.axaml", "Views/App.axaml.cs"])));

        var views = studio.Row("Views").Node.Path;

        studio.Press(studio.Item(studio.Select("Views")), Key.V, KeyModifiers.Control);

        await Settled(studio, () => studio.View.Tree.SelectedItem is Row { Name: "App.axaml" } row && row.Node.Path.Directory == views);

        var moves = Assert.Single(files.Moved);

        Assert.Equal([views.Combine("App.axaml"), views.Combine("App.axaml.cs")], moves.Select(move => move.To));
        Assert.Equal(Format(studio, "project.paste.moved.label", "App.axaml"), Assert.Single(files.Labels));
        Assert.Contains(Format(studio, "project.paste.moved", "App.axaml"), studio.Status.Said);
        Assert.Null(studio.Clipboard.Held);
        Assert.All(studio.Rows, row => Assert.False(row.IsCut, $"{row.Name} остался приглушённым"));
    }

    /// <summary>Esc снимает вырезанное: приглушение уходит, и буфер системы своё отпускает.</summary>
    [AvaloniaFact]
    public async Task Escape_takes_the_cut_back()
    {
        using var studio = await Opened();

        var item = studio.Item(studio.Select("Program.cs"));

        studio.Press(item, Key.X, KeyModifiers.Control);

        Assert.True(studio.Row("Program.cs").IsCut);

        studio.Press(item, Key.Escape);

        Assert.False(studio.Row("Program.cs").IsCut, "Esc не снял приглушение");
        Assert.Null(studio.Clipboard.Held);
        Assert.Equal(1, studio.Clipboard.Forgotten);
    }

    /// <summary>
    /// Копия, вставленная туда же, получает имя с номером сразу — без вопроса, как «Оставить оба» в
    /// проводнике, — и буфер остаётся: скопированное вставляют сколько угодно раз.
    /// </summary>
    [AvaloniaFact]
    public async Task A_copy_pasted_into_its_own_folder_gets_a_number()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var item = studio.Item(studio.Select("Program.cs"));

        studio.Press(item, Key.C, KeyModifiers.Control);

        Assert.False(studio.Row("Program.cs").IsCut, "скопированное приглушено");

        studio.Press(item, Key.V, KeyModifiers.Control);

        await Settled(studio, () => files.Copied.Count == 1);

        var copy = Assert.Single(files.Copied[0]);

        Assert.Equal("Program (2).cs", copy.To.FileName);
        Assert.Equal(copy.From.Directory, copy.To.Directory);
        Assert.Empty(studio.Window.OwnedWindows);
        Assert.NotNull(studio.Clipboard.Held);
    }

    /// <summary>
    /// Ctrl+Shift+C кладёт путь строки текстом, а файлов не трогает: сочетание сверяется целиком, и
    /// Ctrl+C им не притворяется.
    /// </summary>
    [AvaloniaFact]
    public async Task Ctrl_shift_c_copies_the_path_and_leaves_the_files_alone()
    {
        using var studio = await Opened();

        studio.Press(studio.Item(studio.Select("Program.cs")), Key.C, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.Null(studio.Clipboard.Held);
        Assert.DoesNotContain(Format(studio, "project.clip.copied", "Program.cs"), studio.Status.Said);
    }

    /// <summary>
    /// Чужие файлы из буфера системы берутся теми же единицами, что принесённые перетаскиванием:
    /// повтор — один раз, пропавшее с диска — никак.
    /// </summary>
    [Fact]
    public void Files_from_the_clipboard_are_taken_like_dropped_ones()
    {
        var folder = TempFolder.Create("foreign");

        try
        {
            var kept = Path.Combine(folder, "Kept.cs");

            File.WriteAllText(kept, "class Kept { }");

            var clip = SystemFiles.Foreign([kept, kept, Path.Combine(folder, "Gone.cs"), null]);

            Assert.NotNull(clip);
            Assert.Equal(ClipMode.Copy, clip.Mode);
            Assert.Equal([kept], clip.Paths.Select(path => path.Value));
        }
        finally
        {
            TempFolder.Erase(folder);
        }
    }

    /// <summary>Файлы, положенные в буфер проводником, вставляются копией в папку строки.</summary>
    [AvaloniaFact]
    public async Task Files_from_explorer_are_copied_in()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var outside = CanonicalPath.Create(Path.Combine(Path.GetTempPath(), $"arxis-explorer-{Guid.NewGuid():N}", "logo.png"));
        var views = studio.Row("Views").Node.Path;

        // Своё вырезанное кончается, когда буфер заняли чужим, — как в проводнике.
        studio.Press(studio.Item(studio.Select("Program.cs")), Key.X, KeyModifiers.Control);
        studio.Clipboard.Foreign = new FileClip(ClipMode.Copy, [new ClipItem(outside, IsFolder: false, [])]);
        studio.Press(studio.Item(studio.Select("Views")), Key.V, KeyModifiers.Control);

        await Settled(studio, () => files.Copied.Count == 1);

        Assert.Equal(new FileMove(outside, views.Combine("logo.png")), Assert.Single(files.Copied[0]));
        Assert.Empty(files.Moved);
        Assert.False(studio.Row("Program.cs").IsCut, "чужой буфер не снял своё вырезанное");
    }

    /// <summary>
    /// Занятое имя спрашивает: «Оставить оба» даёт номер, «Заменить» просит службу заменить,
    /// «Пропустить» и отмена не вставляют ничего.
    /// </summary>
    [AvaloniaFact]
    public async Task A_taken_name_asks_to_replace_keep_both_or_skip()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var views = studio.Row("Views").Node.Path;

        File.WriteAllText(views.Combine("Program.cs").Value, "занято");
        studio.Press(studio.Item(studio.Select("Program.cs")), Key.C, KeyModifiers.Control);

        ConflictDialog Paste()
        {
            studio.Press(studio.Item(studio.Select("Views")), Key.V, KeyModifiers.Control);

            return Dialog<ConflictDialog>(studio);
        }

        var dialog = Paste();

        Assert.Equal(Format(studio, "project.conflict.question", "Views", "Program.cs"), Part<TextBlock>(dialog, "Question").Text);
        Assert.True(Part<AxButton>(dialog, "Replace").IsEnabled, "файл нельзя заменить");
        Assert.False(Part<AxCheckBox>(dialog, "ForAll").IsVisible, "флажок «для всех» при одном конфликте");

        studio.Click(Part<AxButton>(dialog, "Both"));
        await Settled(studio, () => files.Copied.Count == 1);

        Assert.Equal(views.Combine("Program (2).cs"), Assert.Single(files.Copied[0]).To);

        studio.Click(Part<AxButton>(Paste(), "Replace"));
        await Settled(studio, () => files.Copied.Count == 2);

        var replaced = Assert.Single(files.Copied[1]);

        Assert.True(replaced.Replace, "замену не попросили у службы");
        Assert.Equal(views.Combine("Program.cs"), replaced.To);

        studio.Click(Part<AxButton>(Paste(), "Skip"));
        Paste().KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, files.Copied.Count);
        Assert.Empty(studio.Window.OwnedWindows);
    }

    /// <summary>
    /// Флажок отвечает так же на остальные конфликты вставки, а у папки заменить нельзя — «заменить
    /// все» оставляет её обе.
    /// </summary>
    [AvaloniaFact]
    public async Task One_answer_serves_all_conflicts_and_a_folder_is_never_replaced()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var models = studio.Row("Models").Node.Path;

        Directory.CreateDirectory(models.Combine("Assets").Value);
        File.WriteAllText(models.Combine("Program.cs").Value, "занято");

        studio.Select("Assets");
        studio.View.Tree.SelectedItems!.Add(studio.Row("Program.cs"));
        studio.Press(studio.Item(studio.Row("Program.cs")), Key.C, KeyModifiers.Control);
        studio.Press(studio.Item(studio.Select("Models")), Key.V, KeyModifiers.Control);

        var dialog = Dialog<ConflictDialog>(studio);

        Assert.False(Part<AxButton>(dialog, "Replace").IsEnabled, "папку можно заменить");
        Assert.True(Part<TextBlock>(dialog, "Folders").IsVisible, "не сказано, почему папку не заменить");
        Assert.True(Part<AxCheckBox>(dialog, "ForAll").IsVisible, "флажка «для всех» нет при двух конфликтах");

        Part<AxCheckBox>(dialog, "ForAll").IsChecked = true;
        studio.Click(Part<AxButton>(dialog, "Both"));

        await Settled(studio, () => files.Copied.Count == 1);

        Assert.Equal(
            [models.Combine("Assets (2)"), models.Combine("Program (2).cs")],
            files.Copied[0].Select(pair => pair.To).Order());
        Assert.Empty(studio.Window.OwnedWindows);
    }

    /// <summary>
    /// «Заменить» для всех заменяет файлы, а папку, до которой дошла очередь, оставляет обеими: слить
    /// папки служба не берётся, а потерять вставку человек не просил.
    /// </summary>
    [AvaloniaFact]
    public async Task Replace_for_all_keeps_both_of_a_folder()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var models = studio.Row("Models").Node.Path;

        Directory.CreateDirectory(models.Combine("Assets").Value);
        File.WriteAllText(models.Combine("App.axaml").Value, "занято");

        studio.Select("App.axaml");
        studio.View.Tree.SelectedItems!.Add(studio.Row("Assets"));
        studio.Press(studio.Item(studio.Row("App.axaml")), Key.C, KeyModifiers.Control);
        studio.Press(studio.Item(studio.Select("Models")), Key.V, KeyModifiers.Control);

        var dialog = Dialog<ConflictDialog>(studio);

        Assert.Equal(Format(studio, "project.conflict.question", "Models", "App.axaml"), Part<TextBlock>(dialog, "Question").Text);

        Part<AxCheckBox>(dialog, "ForAll").IsChecked = true;
        studio.Click(Part<AxButton>(dialog, "Replace"));

        await Settled(studio, () => files.Copied.Count == 1);

        var pairs = files.Copied[0];

        Assert.Contains(pairs, pair => pair.To == models.Combine("App.axaml") && pair.Replace);
        Assert.Contains(pairs, pair => pair.To == models.Combine("App.axaml.cs") && !pair.Replace);
        Assert.Contains(pairs, pair => pair.To == models.Combine("Assets (2)") && !pair.Replace);
        Assert.Empty(studio.Window.OwnedWindows);
    }

    /// <summary>Вырезанное, вставленное туда же, где лежит, остаётся на месте: переносить некуда.</summary>
    [AvaloniaFact]
    public async Task A_cut_pasted_where_it_lies_stays()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var item = studio.Item(studio.Select("Program.cs"));

        studio.Press(item, Key.X, KeyModifiers.Control);
        studio.Press(item, Key.V, KeyModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(files.Moved);
        Assert.Empty(files.Copied);
        Assert.True(studio.Row("Program.cs").IsCut, "вырезанное кончилось, так и не вставившись");
    }

    /// <summary>Папку в саму себя не вставить: окно говорит об этом, не зовя службу.</summary>
    [AvaloniaFact]
    public async Task A_folder_is_not_pasted_into_itself()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var views = studio.Item(studio.Select("Views"));

        studio.Press(views, Key.X, KeyModifiers.Control);
        studio.Press(views, Key.V, KeyModifiers.Control);

        var failure = Dialog<FailureDialog>(studio);

        Assert.Equal(Format(studio, "project.paste.intoItself", "Views"), Part<TextBlock>(failure, "Message").Text);
        Assert.Empty(files.Moved);

        failure.Close();
    }

    /// <summary>Пустой буфер — не отказ, а строка состояния: вставлять нечего.</summary>
    [AvaloniaFact]
    public async Task An_empty_clipboard_says_there_is_nothing_to_paste()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        studio.Press(studio.Item(studio.Select("Views")), Key.V, KeyModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(Format(studio, "project.paste.nothing"), studio.Status.Said);
        Assert.Empty(files.Copied);
        Assert.Empty(studio.Window.OwnedWindows);
    }

    /// <summary>Вставка с клавиатуры в правой колонке идёт в папку, которую колонка показывает.</summary>
    [AvaloniaFact]
    public async Task Paste_in_the_column_goes_into_the_folder_it_shows()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files, twoColumns: true);

        var outside = CanonicalPath.Create(Path.Combine(Path.GetTempPath(), $"arxis-explorer-{Guid.NewGuid():N}", "notes.md"));

        studio.Select("Views");
        studio.Clipboard.Foreign = new FileClip(ClipMode.Copy, [new ClipItem(outside, IsFolder: false, [])]);
        studio.Press(studio.Panel.Pane!.Shown, Key.V, KeyModifiers.Control);

        await Settled(studio, () => files.Copied.Count == 1);

        Assert.Equal(studio.Model.Browser.Current!.Path.Combine("notes.md"), Assert.Single(files.Copied[0]).To);
    }

    /// <summary>
    /// Меню пустого места колонки — меню её папки без выбора: из правки одна вставка, у решения, куда
    /// вставлять некуда, правки нет, а у найденного поиском меню пустого места нет вовсе.
    /// </summary>
    /// <remarks>
    /// Как у проводника и Unity: правый щелчок мимо плиток говорит о папке, которую колонка
    /// показывает. Вырезать, удалить и переименовать у пустого места нечего, и этих пунктов нет.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_empty_place_menu_is_the_menu_of_the_folder_without_the_choice()
    {
        using var studio = await Opened(new FilesProbe(), twoColumns: true);

        var pane = studio.Panel.Pane!;

        studio.Select("Models");

        Assert.Equal(
            [studio.Strings["project.edit"], studio.Strings[Reveal.Words], studio.Strings["project.menu.copyPath"], studio.Strings["project.menu.copyRelative"]],
            pane.FolderItems().Select(item => item.Header));
        Assert.Equal([studio.Strings["project.edit.paste"]], studio.EditMenu(pane.FolderItems())!.Items.OfType<AxMenuItem>().Select(item => item.Header));

        studio.Select("App");

        Assert.Equal(studio.Strings["project.menu.openProject"], pane.FolderItems()[0].Header);
        Assert.Equal([studio.Strings["project.edit.paste"]], studio.EditMenu(pane.FolderItems())!.Items.OfType<AxMenuItem>().Select(item => item.Header));

        studio.Select("Hello");

        Assert.Equal(studio.Strings["project.menu.openSolution"], pane.FolderItems()[0].Header);
        Assert.DoesNotContain(pane.FolderItems(), item => Equals(item.Header, studio.Strings["project.edit"]));

        studio.View.Query.Text = "axaml";
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(pane.FolderItems());
    }

    /// <summary>«Вставить» из меню пустого места идёт в папку, которую колонка показывает, — и в пустую.</summary>
    [AvaloniaFact]
    public async Task Paste_from_the_empty_place_menu_goes_into_the_folder_it_shows()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files, twoColumns: true);

        var outside = CanonicalPath.Create(Path.Combine(Path.GetTempPath(), $"arxis-explorer-{Guid.NewGuid():N}", "notes.md"));

        studio.Select("Models");
        studio.Clipboard.Foreign = new FileClip(ClipMode.Copy, [new ClipItem(outside, IsFolder: false, [])]);
        studio.Click(studio.EditMenu(studio.Panel.Pane!.FolderItems())!.Items.OfType<AxMenuItem>(), studio.Strings["project.edit.paste"]);

        await Settled(studio, () => files.Copied.Count == 1);

        Assert.Equal("Models", studio.Model.Browser.Current!.Name);
        Assert.Equal(studio.Model.Browser.Current.Path.Combine("notes.md"), Assert.Single(files.Copied[0]).To);
    }

    private static Task<ProjectWindowStudio> Opened(FilesProbe? files = null, bool twoColumns = false) =>
        ProjectWindowStudio.OpenedAsync(files, twoColumns);
}
