using System.Text;
using ArxisStudio.LocalHistory;
using ArxisStudio.Modules.Projects;
using ArxisStudio.Modules.Projects.History;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Служба локальной истории на решении, лежащем на диске: отмена, возврат, метки и история пути.
/// </summary>
/// <remarks>
/// Решение то же, что у тестов службы файлов, — проект с вложенным файлом, папкой и ссылкой
/// <c>Update</c> в файле проекта, — и правится оно настоящим диском. Историю служба ведёт во
/// временной папке теста.
/// </remarks>
public sealed class ProjectsHistoryTests : IDisposable
{
    private const string Project = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <None Update="Views\Readme.txt" CopyToOutputDirectory="Always" />
          </ItemGroup>
        </Project>
        """;

    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"arxis-projects-history-{Guid.NewGuid():N}")).FullName;

    private string Solution => Path.Combine(_root, "solution");

    private string Lib => Path.Combine(Solution, "Lib");

    private string HistoryRoot => Path.Combine(_root, "history");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Отмена переименования возвращает оба файла под прежние имена одним действием «Отмена: …»,
    /// перечитывает модель и говорит о переехавшем.
    /// </summary>
    [Fact]
    public async Task An_undone_rename_brings_both_files_back()
    {
        using var studio = await OpenAsync();

        await Succeeds(studio.Files.MoveAsync(
            [Pair("Views/MainWindow.axaml", "Views/Main.axaml"), Pair("Views/MainWindow.axaml.cs", "Views/Main.axaml.cs")],
            "Переименование MainWindow.axaml",
            Token));

        var last = studio.History.LastStudioAction;

        Assert.Equal("Переименование MainWindow.axaml", last?.Label);

        var moved = Record(studio);

        await Succeeds(studio.History.UndoAsync(last!.Id, Token));
        await studio.Thread.IdleAsync();

        Assert.True(File.Exists(At("Views/MainWindow.axaml")) && File.Exists(At("Views/MainWindow.axaml.cs")), "файлы не вернулись");
        Assert.False(File.Exists(At("Views/Main.axaml")), "под новым именем файл остался");

        var undo = Store(studio).Actions[^1];

        // Метку отмены пишет служба словами своего словаря; язык процесса тестов — английский.
        Assert.EndsWith(": Переименование MainWindow.axaml", undo.Label, StringComparison.Ordinal);
        Assert.Equal(last.Id, undo.Undoes);
        Assert.Equal(2, Assert.Single(moved).Moved.Length);
        Assert.Equal(ProjectsLoadReason.Files, studio.Projects.Status.LastLoad?.Reason);
        Assert.NotNull(Store(studio).Known(At("Views/MainWindow.axaml.cs")));
        Assert.Null(Store(studio).Known(At("Views/Main.axaml.cs")));
        Assert.Null(studio.History.LastStudioAction);
    }

    /// <summary>
    /// Отмена переноса папки возвращает файлу проекта прежние байты — с отметкой порядка байт и
    /// ссылкой на прежнее имя.
    /// </summary>
    [Fact]
    public async Task An_undone_folder_move_gives_the_project_file_its_bytes_back()
    {
        using var studio = await OpenAsync();
        var original = File.ReadAllBytes(At("Lib.csproj"));

        await Succeeds(studio.Files.MoveAsync([Pair("Views", "Screens")], "Перенос Views", Token));
        await Succeeds(studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token));

        Assert.True(Directory.Exists(At("Views")) && !Directory.Exists(At("Screens")), "папка не вернулась");
        Assert.Equal(original, File.ReadAllBytes(At("Lib.csproj")));
    }

    /// <summary>
    /// Файл проекта, который человек правил после переноса, байтами не возвращается: ссылки
    /// переписываются обратно, а его правка остаётся.
    /// </summary>
    /// <remarks>
    /// Иначе любой поставленный после переименования пакет запрещал бы его отменить — а в Rider
    /// отмена переименования работает и после.
    /// </remarks>
    [Fact]
    public async Task An_undone_move_rewrites_references_back_in_a_project_file_edited_since()
    {
        using var studio = await OpenAsync();

        await Succeeds(studio.Files.MoveAsync([Pair("Views", "Screens")], "Перенос Views", Token));

        var edited = File.ReadAllText(At("Lib.csproj")).Replace("</Project>", "  <!-- пакет -->\r\n</Project>", StringComparison.Ordinal);

        File.WriteAllText(At("Lib.csproj"), edited, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = await studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token);

        Assert.False(result.HasErrors, Said(result));
        Assert.Empty(result.Diagnostics);

        var text = File.ReadAllText(At("Lib.csproj"));

        Assert.Contains("Update=\"Views\\Readme.txt\"", text, StringComparison.Ordinal);
        Assert.Contains("<!-- пакет -->", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Отмена удаления заводит папку и файлы с прежним содержимым и возвращает ссылку в файл проекта.
    /// </summary>
    [Fact]
    public async Task An_undone_delete_brings_the_folder_back_with_its_content()
    {
        using var studio = await OpenAsync();
        var original = File.ReadAllBytes(At("Lib.csproj"));
        var deleted = Record(studio);

        await Succeeds(studio.Files.DeleteAsync([Canon("Views")], "Удаление Views", Token));

        Assert.False(Directory.Exists(At("Views")), "папка не удалилась");
        Assert.DoesNotContain("Readme.txt", File.ReadAllText(At("Lib.csproj")), StringComparison.Ordinal);

        await Succeeds(studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token));
        await studio.Thread.IdleAsync();

        Assert.Equal("прочти", File.ReadAllText(At("Views/Readme.txt")));
        Assert.Equal("partial class MainWindow { }", File.ReadAllText(At("Views/MainWindow.axaml.cs")));
        Assert.Equal(original, File.ReadAllBytes(At("Lib.csproj")));
        Assert.Single(deleted);
    }

    /// <summary>
    /// Удаление, после которого файл проекта правили, возвращает файлы, а о снятых ссылках
    /// предупреждает: вернуть их нечем.
    /// </summary>
    [Fact]
    public async Task An_undone_delete_warns_of_references_it_cannot_bring_back()
    {
        using var studio = await OpenAsync();

        await Succeeds(studio.Files.DeleteAsync([Canon("Views")], "Удаление Views", Token));
        File.AppendAllText(At("Lib.csproj"), "\r\n<!-- правка -->");

        var result = await studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token);

        Assert.False(result.HasErrors, Said(result));
        Assert.Equal(ProjectsDiagnosticCodes.NotStored, Assert.Single(result.Diagnostics).Code);
        Assert.True(File.Exists(At("Views/Readme.txt")), "файлы не вернулись");
        Assert.EndsWith("<!-- правка -->", File.ReadAllText(At("Lib.csproj")), StringComparison.Ordinal);
    }

    /// <summary>Отмена копии убирает копии — и папку, которую копия завела.</summary>
    [Fact]
    public async Task An_undone_copy_takes_the_copies_away()
    {
        using var studio = await OpenAsync();

        await Succeeds(studio.Files.CopyAsync(
            [Pair("Greeter.cs", "Greeter (2).cs"), Pair("Views", "Views (2)")], "Вставка Greeter.cs", Token));

        Assert.True(File.Exists(At("Views (2)/Readme.txt")), "копия не легла");

        await Succeeds(studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token));

        Assert.False(File.Exists(At("Greeter (2).cs")), "копия файла осталась");
        Assert.False(Directory.Exists(At("Views (2)")), "копия папки осталась");
        Assert.True(File.Exists(At("Greeter.cs")) && File.Exists(At("Views/Readme.txt")), "отмена тронула источник");
    }

    /// <summary>Отмена замены возвращает заменённый файл с его прежним содержимым.</summary>
    [Fact]
    public async Task An_undone_replace_puts_the_replaced_file_back()
    {
        using var studio = await OpenAsync();

        File.WriteAllText(At("Fresh.cs"), "class Fresh { }");

        await Succeeds(studio.Files.MoveAsync(
            [new FileMove(Canon("Fresh.cs"), Canon("Greeter.cs")) { Replace = true }], "Перенос Fresh.cs", Token));
        await Succeeds(studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token));

        Assert.Equal("class Greeter { }", File.ReadAllText(At("Greeter.cs")));
        Assert.Equal("class Fresh { }", File.ReadAllText(At("Fresh.cs")));
    }

    /// <summary>
    /// Файл, изменившийся с тех пор, отмена не затирает — и не трогает ничего: ни его, ни соседей.
    /// </summary>
    [Fact]
    public async Task An_undo_over_a_file_changed_since_refuses_and_touches_nothing()
    {
        using var studio = await OpenAsync();

        await Succeeds(studio.Files.CopyAsync(
            [Pair("Greeter.cs", "Greeter (2).cs"), Pair("appsettings.json", "appsettings (2).json")], "Вставка", Token));
        File.WriteAllText(At("appsettings (2).json"), "{ \"новое\": true }");

        var result = await studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token);

        Assert.Equal(ProjectsDiagnosticCodes.ChangedSince, Code(result));
        Assert.True(File.Exists(At("Greeter (2).cs")), "отказ тронул соседа");
        Assert.Equal("{ \"новое\": true }", File.ReadAllText(At("appsettings (2).json")));
    }

    /// <summary>
    /// Прежнее имя занял новый файл — переименование не отменяется, и новый файл цел.
    /// </summary>
    [Fact]
    public async Task An_undo_does_not_move_back_onto_a_new_file()
    {
        using var studio = await OpenAsync();

        await Succeeds(studio.Files.MoveAsync([Pair("Greeter.cs", "Hello.cs")], "Переименование Greeter.cs", Token));
        File.WriteAllText(At("Greeter.cs"), "class Newcomer { }");

        var result = await studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token);

        Assert.Equal(ProjectsDiagnosticCodes.ChangedSince, Code(result));
        Assert.Equal("class Newcomer { }", File.ReadAllText(At("Greeter.cs")));
        Assert.Equal("class Greeter { }", File.ReadAllText(At("Hello.cs")));
    }

    /// <summary>
    /// На месте удалённого появился новый файл — удаление не отменяется, и новый файл цел.
    /// </summary>
    [Fact]
    public async Task An_undo_does_not_restore_over_a_new_file()
    {
        using var studio = await OpenAsync();

        await Succeeds(studio.Files.DeleteAsync([Canon("Greeter.cs"), Canon("appsettings.json")], "Удаление", Token));
        File.WriteAllText(At("Greeter.cs"), "class Newcomer { }");

        var result = await studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token);

        Assert.Equal(ProjectsDiagnosticCodes.ChangedSince, Code(result));
        Assert.Equal("class Newcomer { }", File.ReadAllText(At("Greeter.cs")));
        Assert.False(File.Exists(At("appsettings.json")), "отказ вернул соседа");
    }

    /// <summary>
    /// Диск отказал посреди отмены — сделанное ею возвращается, и диск такой, каким был до неё.
    /// </summary>
    /// <remarks>
    /// Переезды отменяются в обратном порядке, поэтому держат первый из переименованных: второй к тому
    /// времени уже вернулся под прежнее имя, и откату есть что возвращать.
    /// </remarks>
    [Fact]
    public async Task A_failed_undo_puts_back_what_it_had_done()
    {
        using var studio = await OpenAsync();

        await Succeeds(studio.Files.MoveAsync(
            [Pair("Greeter.cs", "Hello.cs"), Pair("appsettings.json", "settings.json")], "Переименование", Token));

        ProjectOperationResult result;

        using (new FileStream(At("Hello.cs"), FileMode.Open, FileAccess.Read, FileShare.None))
            result = await studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token);

        Assert.Equal(ProjectsDiagnosticCodes.FileOperationFailed, Code(result));
        Assert.True(File.Exists(At("settings.json")) && !File.Exists(At("appsettings.json")), "откат не вернул сделанное");
        Assert.True(File.Exists(At("Hello.cs")) && !File.Exists(At("Greeter.cs")), "держанный файл сдвинулся");
        Assert.NotNull(studio.History.LastStudioAction);
    }

    /// <summary>
    /// Ctrl+Z не дотягивается до действий над чужим решением: отменить их из этого нельзя.
    /// </summary>
    [Fact]
    public async Task The_last_studio_action_belongs_to_the_open_solution()
    {
        using var studio = await OpenAsync();

        Store(studio).Record("Чужое решение", HistoryOrigin.Studio,
        [
            new HistoryChange { Kind = HistoryChangeKind.Created, Path = Path.Combine(_root, "other", "Other.cs") },
        ]);

        Assert.Null(studio.History.LastStudioAction);
        Assert.Equal(ProjectsDiagnosticCodes.OutsideProjects, Code(await studio.History.UndoAsync(Store(studio).Actions[^1].Id, Token)));
    }

    /// <summary>
    /// Отменённое руками отменять нечего: служба так и говорит, не называя это переменой.
    /// </summary>
    [Fact]
    public async Task What_was_undone_by_hand_leaves_nothing_to_undo()
    {
        using var studio = await OpenAsync();

        await Edit(studio, "Greeter.cs", "class Greeter { int X; }");

        var edit = Store(studio).Actions[^1];

        await Edit(studio, "Greeter.cs", "class Greeter { }");
        await Succeeds(studio.Files.MoveAsync([Pair("appsettings.json", "settings.json")], "Переименование appsettings.json", Token));

        var rename = studio.History.LastStudioAction!.Id;

        await Succeeds(studio.Files.MoveAsync([Pair("settings.json", "appsettings.json")], "Обратно", Token));

        foreach (var id in new[] { edit.Id, rename })
        {
            var result = await studio.History.UndoAsync(id, Token);

            Assert.Equal(ProjectsDiagnosticCodes.ChangedSince, Code(result));
            Assert.Contains(Store(studio).Find(id)!.Label, Assert.Single(result.Diagnostics).Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Заменённый переносом файл удалён, но место его занял приехавший, и ссылок удаление не снимало:
    /// отмена после правки файла проекта ни о чём не предупреждает.
    /// </summary>
    [Fact]
    public async Task An_undone_replace_warns_of_no_references()
    {
        using var studio = await OpenAsync();

        File.WriteAllText(At("Fresh.cs"), "class Fresh { }");

        // Переименование Readme.txt переписывает файл проекта — и его правку потом будет что
        // переписывать обратно.
        await Succeeds(studio.Files.MoveAsync(
        [
            new FileMove(Canon("Fresh.cs"), Canon("Greeter.cs")) { Replace = true },
            Pair("Views/Readme.txt", "Views/Notes.txt"),
        ], "Перенос", Token));
        File.AppendAllText(At("Lib.csproj"), "\r\n<!-- правка -->");

        var result = await studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token);

        Assert.False(result.HasErrors, Said(result));
        Assert.Empty(result.Diagnostics);
        Assert.Equal("class Greeter { }", File.ReadAllText(At("Greeter.cs")));
        Assert.Contains("Update=\"Views\\Readme.txt\"", File.ReadAllText(At("Lib.csproj")), StringComparison.Ordinal);
    }

    /// <summary>Отменённое второй раз не отменяется, а отмена отмены возвращает действие.</summary>
    [Fact]
    public async Task An_undone_action_is_refused_and_undoing_the_undo_brings_it_back()
    {
        using var studio = await OpenAsync();

        await Succeeds(studio.Files.MoveAsync([Pair("Greeter.cs", "Hello.cs")], "Переименование Greeter.cs", Token));

        var rename = studio.History.LastStudioAction!;

        await Succeeds(studio.History.UndoAsync(rename.Id, Token));

        Assert.Equal(ProjectsDiagnosticCodes.ChangedSince, Code(await studio.History.UndoAsync(rename.Id, Token)));

        await Succeeds(studio.History.UndoAsync(Store(studio).Actions[^1].Id, Token));

        Assert.True(File.Exists(At("Hello.cs")) && !File.Exists(At("Greeter.cs")), "отмена отмены не вернула переименование");
        Assert.Equal(rename.Id, studio.History.LastStudioAction?.Id);
    }

    /// <summary>
    /// Отменённое остаётся отменённым, даже когда его итог снова на диске, — сделанный другим
    /// действием: отменить его ещё раз значило бы отменить чужое.
    /// </summary>
    [Fact]
    public async Task An_undone_action_stays_undone_even_when_its_result_is_back()
    {
        using var studio = await OpenAsync();

        await Succeeds(studio.Files.MoveAsync([Pair("Greeter.cs", "Hello.cs")], "Переименование Greeter.cs", Token));

        var rename = studio.History.LastStudioAction!.Id;

        await Succeeds(studio.History.UndoAsync(rename, Token));
        await Succeeds(studio.Files.MoveAsync([Pair("Greeter.cs", "Hello.cs")], "Ещё раз", Token));

        Assert.Equal(ProjectsDiagnosticCodes.ChangedSince, Code(await studio.History.UndoAsync(rename, Token)));
        Assert.True(File.Exists(At("Hello.cs")) && !File.Exists(At("Greeter.cs")), "отменили чужое переименование");
    }

    /// <summary>
    /// Копию больше предела отмена не убирает: проверить, что её не меняли, нечем, — и говорит об этом.
    /// </summary>
    [Fact]
    public async Task A_copy_too_large_to_check_is_left_with_a_warning()
    {
        using var studio = await OpenAsync();

        studio.Settings.Set(ProjectsSettings.HistoryMaxFileMbKey, 1d);
        await Settle(Host(studio).History);
        File.WriteAllBytes(At("Assets.bin"), new byte[(1024 * 1024) + 1]);

        await Succeeds(studio.Files.CopyAsync(
            [Pair("Assets.bin", "Assets (2).bin"), Pair("Greeter.cs", "Greeter (2).cs")], "Вставка", Token));

        var result = await studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token);

        Assert.False(result.HasErrors, Said(result));
        Assert.Equal(ProjectsDiagnosticCodes.NotStored, Assert.Single(result.Diagnostics).Code);
        Assert.True(File.Exists(At("Assets (2).bin")), "непроверенную копию убрали");
        Assert.False(File.Exists(At("Greeter (2).cs")), "проверяемую копию оставили");
    }

    /// <summary>
    /// Ctrl+Z идёт назад по сделанному студией: чужие правки, метки и отменённое он проходит мимо.
    /// </summary>
    [Fact]
    public async Task The_last_studio_action_skips_external_changes_labels_and_undone_actions()
    {
        using var studio = await OpenAsync();

        await Succeeds(studio.Files.MoveAsync([Pair("Greeter.cs", "Hello.cs")], "Первое", Token));
        await Succeeds(studio.Files.MoveAsync([Pair("appsettings.json", "settings.json")], "Второе", Token));
        await Succeeds(studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token));
        await Succeeds(studio.History.PutLabelAsync("метка", Token));
        await Edit(studio, "Hello.cs", "class Hello { }");

        Assert.Equal(HistoryOrigin.External, Store(studio).Actions[^1].Origin);
        Assert.Equal("Первое", studio.History.LastStudioAction?.Label);
    }

    /// <summary>
    /// Ctrl+Z отменяет сделанное в этом запуске: действие прежнего запуска остаётся в истории, но им
    /// не становится.
    /// </summary>
    [Fact]
    public async Task The_last_studio_action_comes_from_this_run_only()
    {
        HistoryRecorder earlier;

        using (var before = await OpenAsync())
        {
            await Succeeds(before.Files.MoveAsync([Pair("Greeter.cs", "Hello.cs")], "Вчерашнее", Token));
            earlier = Host(before).History;
        }

        // Хранилище закрывается последним делом очереди, а папку истории держит, пока открыто.
        await earlier.Completion.WaitAsync(TimeSpan.FromSeconds(30), Token);

        using var studio = await OpenAsync(write: false);

        Assert.Contains(Store(studio).Actions, action => action.Label == "Вчерашнее");
        Assert.Null(studio.History.LastStudioAction);

        await Succeeds(studio.Files.MoveAsync([Pair("Hello.cs", "Greeter.cs")], "Сегодняшнее", Token));

        Assert.Equal("Сегодняшнее", studio.History.LastStudioAction?.Label);
    }

    /// <summary>
    /// Файл больше предела отмена пропускает с предупреждением и возвращает остальное.
    /// </summary>
    [Fact]
    public async Task A_file_the_history_does_not_keep_is_skipped_with_a_warning()
    {
        using var studio = await OpenAsync();

        studio.Settings.Set(ProjectsSettings.HistoryMaxFileMbKey, 1d);
        await Settle(Host(studio).History);
        File.WriteAllBytes(At("Assets.bin"), new byte[(1024 * 1024) + 1]);

        await Succeeds(studio.Files.DeleteAsync([Canon("Assets.bin"), Canon("Greeter.cs")], "Удаление", Token));

        var result = await studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token);

        Assert.False(result.HasErrors, Said(result));
        Assert.Equal(ProjectsDiagnosticCodes.NotStored, Assert.Single(result.Diagnostics).Code);
        Assert.True(File.Exists(At("Greeter.cs")), "маленький файл не вернулся");
        Assert.False(File.Exists(At("Assets.bin")), "большой файл взялся ниоткуда");
    }

    /// <summary>
    /// Возврат переписывает файл прежним содержимым новым действием — и сам отменяется, как всякое.
    /// </summary>
    [Fact]
    public async Task A_revert_writes_the_old_content_and_is_itself_undoable()
    {
        using var studio = await OpenAsync();

        await Edit(studio, "Greeter.cs", "class Greeter { void Hello() { } }");

        var revisions = await studio.History.RevisionsAsync(Canon("Greeter.cs"), Token);
        var edit = revisions.SelectMany(revision => revision.Changes).First(change => change.Kind == LocalHistoryChangeKind.Modified);

        Assert.Equal("class Greeter { }", Encoding.UTF8.GetString((await studio.History.ReadAsync(edit.Before, Token))!));

        var sequence = studio.Projects.Status.Sequence;

        await Succeeds(studio.History.RevertAsync(Canon("Greeter.cs"), edit.Before, "Возврат Greeter.cs", Token));

        Assert.Equal("class Greeter { }", File.ReadAllText(At("Greeter.cs")));
        Assert.Equal("Возврат Greeter.cs", studio.History.LastStudioAction?.Label);

        // Правка содержимого исходника модель не меняет — и не перечитывает.
        Assert.Equal(sequence, studio.Projects.Status.Sequence);

        // Файл уже такой: второй возврат ничего не пишет.
        var recorded = Store(studio).Last;

        await Succeeds(studio.History.RevertAsync(Canon("Greeter.cs"), edit.Before, "Возврат Greeter.cs", Token));

        Assert.Equal(recorded, Store(studio).Last);

        await Succeeds(studio.History.UndoAsync(studio.History.LastStudioAction!.Id, Token));

        Assert.Equal("class Greeter { void Hello() { } }", File.ReadAllText(At("Greeter.cs")));
    }

    /// <summary>Возврат заводит заново удалённый файл: историю пути видно и у того, чего нет.</summary>
    [Fact]
    public async Task A_revert_brings_a_deleted_file_back()
    {
        using var studio = await OpenAsync();

        await Succeeds(studio.Files.DeleteAsync([Canon("Greeter.cs")], "Удаление Greeter.cs", Token));

        var revisions = await studio.History.RevisionsAsync(Canon("Greeter.cs"), Token);
        var gone = revisions.SelectMany(revision => revision.Changes).First(change => change.Kind == LocalHistoryChangeKind.Deleted);
        var sequence = studio.Projects.Status.Sequence;

        await Succeeds(studio.History.RevertAsync(Canon("Greeter.cs"), gone.Before, "Возврат Greeter.cs", Token));

        Assert.Equal("class Greeter { }", File.ReadAllText(At("Greeter.cs")));
        Assert.Equal(HistoryChangeKind.Created, Assert.Single(Store(studio).Actions[^1].Changes).Kind);

        // Появившийся файл — перемена состава: модель перечитана раньше, чем возврат вернулся.
        Assert.True(studio.Projects.Status.Sequence > sequence, "модель не перечитана");
    }

    /// <summary>
    /// Файл больше предела возврат не переписывает: его нынешнего содержимого история не сохранит.
    /// </summary>
    [Fact]
    public async Task A_revert_does_not_overwrite_a_file_the_history_cannot_keep()
    {
        using var studio = await OpenAsync();

        await Edit(studio, "Greeter.cs", "class Greeter { int X; }");

        var edit = (await studio.History.RevisionsAsync(Canon("Greeter.cs"), Token))
            .SelectMany(revision => revision.Changes)
            .First(change => change.Kind == LocalHistoryChangeKind.Modified);

        studio.Settings.Set(ProjectsSettings.HistoryMaxFileMbKey, 1d);
        await Settle(Host(studio).History);

        var big = new string('x', (1024 * 1024) + 1);

        File.WriteAllText(At("Greeter.cs"), big);

        var result = await studio.History.RevertAsync(Canon("Greeter.cs"), edit.Before, "Возврат Greeter.cs", Token);

        Assert.Equal(ProjectsDiagnosticCodes.NotStored, Code(result));
        Assert.Equal(big, File.ReadAllText(At("Greeter.cs")));
    }

    /// <summary>
    /// История файла идёт сквозь переименование, а метку решения видно у каждого его файла.
    /// </summary>
    [Fact]
    public async Task The_history_of_a_file_goes_through_its_rename_and_shows_the_solution_label()
    {
        using var studio = await OpenAsync();

        await Edit(studio, "Greeter.cs", "class Greeter { int X; }");
        await Succeeds(studio.History.PutLabelAsync("до переделки", Token));
        await Succeeds(studio.Files.MoveAsync([Pair("Greeter.cs", "Hello.cs")], "Переименование Greeter.cs", Token));

        var rows = await studio.History.RevisionsAsync(Canon("Hello.cs"), Token);

        Assert.Equal(["Переименование Greeter.cs", "до переделки"], rows.Take(2).Select(row => row.Action.Label));
        Assert.True(rows[1].Action.IsLabel, "метка пришла правкой");
        Assert.Equal(LocalHistoryOrigin.External, rows[2].Action.Origin);
        Assert.Equal(3, rows.Count);

        var app = await studio.History.RevisionsAsync(CanonicalPath.Create(Path.Combine(Solution, "App", "Program.cs")), Token);

        Assert.Contains(app, row => row.Action.Label == "до переделки");
    }

    /// <summary>
    /// История папки — то, что было внутри, и её видно, когда самой папки уже нет.
    /// </summary>
    [Fact]
    public async Task The_history_of_a_folder_holds_what_happened_inside_even_after_it_is_gone()
    {
        using var studio = await OpenAsync();

        await Succeeds(studio.Files.DeleteAsync([Canon("Views/Readme.txt")], "Удаление Readme.txt", Token));
        await Succeeds(studio.Files.DeleteAsync([Canon("Views")], "Удаление Views", Token));

        var rows = await studio.History.RevisionsAsync(Canon("Views"), Token);

        Assert.Equal(["Удаление Views", "Удаление Readme.txt"], rows.Select(row => row.Action.Label));
        Assert.Contains(rows[0].Changes, change => change.Path == Canon("Views/MainWindow.axaml.cs"));
    }

    /// <summary>Записанное действие доходит до подписчика событием в потоке интерфейса.</summary>
    [Fact]
    public async Task A_recorded_action_raises_changed()
    {
        using var studio = await OpenAsync();
        var raised = 0;

        studio.History.Changed += (_, _) =>
        {
            Assert.True(studio.Thread.CheckAccess(), "событие пришло не в поток интерфейса");
            raised++;
        };

        await Succeeds(studio.History.PutLabelAsync("метка", Token));
        await studio.Thread.IdleAsync();

        Assert.True(raised > 0, "о метке не сказали");
    }

    /// <summary>Без истории служба отвечает честно: не ведётся, отменять нечем, строк нет.</summary>
    [Fact]
    public async Task Without_a_history_the_service_says_so()
    {
        using var studio = await OpenAsync(history: false);

        Assert.False(studio.History.IsOn);
        Assert.Null(studio.History.LastStudioAction);
        Assert.Empty(await studio.History.RevisionsAsync(Canon("Greeter.cs"), Token));
        Assert.Equal(ProjectsDiagnosticCodes.HistoryUnavailable, Code(await studio.History.UndoAsync(1, Token)));
        Assert.Equal(ProjectsDiagnosticCodes.HistoryUnavailable, Code(await studio.History.PutLabelAsync("метка", Token)));
    }

    /// <summary>Решение на диске, открытое службой с историей, — и опорный снимок снят.</summary>
    /// <param name="history">Вести ли историю.</param>
    /// <param name="write">Раскладывать ли решение заново: второй запуск открывает то, что оставил первый.</param>
    private async Task<ProjectsStudio> OpenAsync(bool history = true, bool write = true)
    {
        if (write)
        {
            Directory.CreateDirectory(Path.Combine(Lib, "Views"));
            File.WriteAllText(Path.Combine(Solution, "Hello.slnx"), "<Solution />");
            File.WriteAllText(At("Lib.csproj"), Project.ReplaceLineEndings("\r\n"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            File.WriteAllText(At("Greeter.cs"), "class Greeter { }");
            File.WriteAllText(At("appsettings.json"), "{ }");
            File.WriteAllText(At("Views/MainWindow.axaml"), "<Window />");
            File.WriteAllText(At("Views/MainWindow.axaml.cs"), "partial class MainWindow { }");
            File.WriteAllText(At("Views/Readme.txt"), "прочти");
            Directory.CreateDirectory(Path.Combine(Solution, "App"));
            File.WriteAllText(Path.Combine(Solution, "App", "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(Solution, "App", "Program.cs"), "class Program { }");
        }

        var studio = new ProjectsStudio(historyRoot: history ? HistoryRoot : null);

        studio.Provider.Projects =
        [
            ("Lib", ["Greeter.cs", "appsettings.json", "Views/MainWindow.axaml", "Views/MainWindow.axaml.cs"]),
            ("App", ["Program.cs"]),
        ];

        var opened = await studio.Projects.OpenAsync(CanonicalPath.Create(Path.Combine(Solution, "Hello.slnx")), Token);

        Assert.True(opened.HasSnapshot, "решение не открылось");

        await Settle(Host(studio).History);

        return studio;
    }

    /// <summary>Правит файл мимо студии и ждёт, пока история запишет правку внешней.</summary>
    private async Task Edit(ProjectsStudio studio, string relative, string text)
    {
        File.WriteAllText(At(relative), text);

        var host = Host(studio);
        var watcher = host.Session?.History ?? throw new InvalidOperationException("история сессии не ведётся");

        watcher.Report(At(relative));
        watcher.Flush();
        await Settle(host.History);
    }

    private static async Task Succeeds(Task<ProjectOperationResult> operation)
    {
        var result = await operation;

        Assert.False(result.HasErrors, Said(result));
    }

    private static ProjectsHost Host(ProjectsStudio studio) =>
        ((ProjectsModule)Assert.Single(studio.Module.Entries)).Host ?? throw new InvalidOperationException("служба не поднята");

    private static LocalHistoryStore Store(ProjectsStudio studio) =>
        Host(studio).History.Store ?? throw new InvalidOperationException("история не открылась");

    private static List<FilesChangedEventArgs> Record(ProjectsStudio studio)
    {
        var seen = new List<FilesChangedEventArgs>();

        studio.Files.Changed += (_, change) => seen.Add(change);

        return seen;
    }

    private string At(string relative) =>
        Path.GetFullPath(Path.Combine(Lib, relative.Replace('/', Path.DirectorySeparatorChar)));

    private CanonicalPath Canon(string relative) => CanonicalPath.Create(At(relative));

    private FileMove Pair(string from, string to) => new(Canon(from), Canon(to));

    private static string? Code(ProjectOperationResult result) => result.Diagnostics.FirstOrDefault()?.Code;

    private static string Said(ProjectOperationResult result) =>
        string.Join("; ", result.Diagnostics.Select(diagnostic => $"{diagnostic.Code} {diagnostic.Message}"));

    private static async Task Settle(HistoryRecorder recorder)
    {
        for (var round = 0; round < 4; round++)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Assert.True(recorder.Enqueue(() =>
            {
                done.TrySetResult();
                return Task.CompletedTask;
            }), "очередь истории закрыта");

            await done.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        }
    }
}
