using System.Text;
using ArxisStudio.LocalHistory;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Запись файлов службой: так сохраняет документ редактор — с историей, сверкой с диском и
/// узнаванием своего сохранения в слежении.
/// </summary>
/// <remarks>
/// Диск настоящий, история — во временной папке набора. В общей очереди: модуль поднимается хостом, а
/// контракты модулей живут на процесс.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public sealed class ProjectsWriteTests() : ProjectsOnDisk("projects-write")
{
    /// <summary>
    /// Запись меняет содержимое, а история хранит прежнее одним действием, помеченным сохранением.
    /// </summary>
    [Fact]
    public async Task A_write_replaces_the_content_and_history_keeps_it_as_a_save()
    {
        using var studio = await OpenAsync();
        var form = Canon("Views/MainWindow.axaml");

        var result = await studio.Files.WriteAsync(
            [new FileWrite(form, Bytes("<Window Title=\"Новое\" />")) { Expected = Bytes("<Window />") }],
            "Сохранение MainWindow.axaml",
            Token);

        Assert.False(result.HasErrors, Said(result));
        Assert.Equal("<Window Title=\"Новое\" />", File.ReadAllText(form.Value));

        await studio.SettleHistoryAsync();

        var action = studio.HistoryStore.Actions[^1];
        var change = Assert.Single(action.Changes);

        Assert.True(action.IsSave, "запись легла в историю обычным действием");
        Assert.Equal("Сохранение MainWindow.axaml", action.Label);
        Assert.Equal(HistoryChangeKind.Modified, change.Kind);
        Assert.Equal("<Window />", Encoding.UTF8.GetString(studio.HistoryStore.Read(Assert.NotNull(change.Before))!));
    }

    /// <summary>
    /// Общая отмена окна проекта сохранение проходит мимо: у редактора своя история.
    /// </summary>
    /// <remarks>
    /// Иначе Ctrl+Z в окне проекта откатил бы на диске то, что дизайнер держит у себя, и его следующее
    /// сохранение записало бы откаченное снова.
    /// </remarks>
    [Fact]
    public async Task The_window_undo_passes_a_save_by()
    {
        using var studio = await OpenAsync();

        await studio.Files.MoveAsync([Pair("Views/Readme.txt", "Views/Notes.txt")], "Переименование Readme.txt", Token);
        await studio.Files.WriteAsync([new FileWrite(Canon("Views/MainWindow.axaml"), Bytes("<Window Title=\"A\" />"))], "Сохранение MainWindow.axaml", Token);
        await studio.SettleHistoryAsync();

        Assert.Equal("Переименование Readme.txt", studio.History.LastStudioAction?.Label);

        var revisions = await studio.History.RevisionsAsync(Canon("Views/MainWindow.axaml"), Token);

        Assert.True(revisions[0].Action.IsSave, "окно истории не узнаёт сохранение");
    }

    /// <summary>Файл переписали мимо редактора — запись отказывает и чужого не затирает.</summary>
    [Fact]
    public async Task A_write_over_content_changed_behind_its_back_is_refused()
    {
        using var studio = await OpenAsync();
        var form = Canon("Views/MainWindow.axaml");

        File.WriteAllText(form.Value, "<Window Title=\"Rider\" />");

        var result = await studio.Files.WriteAsync(
            [new FileWrite(form, Bytes("<Window Title=\"Мой\" />")) { Expected = Bytes("<Window />") }],
            "Сохранение MainWindow.axaml",
            Token);

        Assert.Equal(ProjectsDiagnosticCodes.ContentChanged, Code(result));
        Assert.Equal("<Window Title=\"Rider\" />", File.ReadAllText(form.Value));
    }

    /// <summary>Записать можно только то, что лежит в папке проекта и само не проект.</summary>
    [Fact]
    public async Task A_write_names_only_existing_files_inside_the_projects()
    {
        using var studio = await OpenAsync();
        var project = File.ReadAllText(At("Lib.csproj"));

        var protectedFile = await studio.Files.WriteAsync([new FileWrite(Canon("Lib.csproj"), Bytes("<Project />"))], "Сохранение", Token);
        var missing = await studio.Files.WriteAsync([new FileWrite(Canon("Views/Nope.axaml"), Bytes("<Window />"))], "Сохранение", Token);
        var outside = await studio.Files.WriteAsync(
            [new FileWrite(CanonicalPath.Create(Path.Combine(Root, "elsewhere.txt")), Bytes("чужое"))], "Сохранение", Token);

        Assert.Equal(ProjectsDiagnosticCodes.OutsideProjects, Code(protectedFile));
        Assert.Equal(ProjectsDiagnosticCodes.Missing, Code(missing));
        Assert.Equal(ProjectsDiagnosticCodes.OutsideProjects, Code(outside));
        Assert.Equal(project, File.ReadAllText(At("Lib.csproj")));
        Assert.False(File.Exists(At("Views/Nope.axaml")), "запись завела файл, которого не было");
    }

    /// <summary>Диск отказал посередине пачки — записанное до отказа возвращается прежним.</summary>
    [Fact]
    public async Task A_batch_that_fails_midway_puts_back_what_it_wrote()
    {
        using var studio = await OpenAsync();
        var form = Canon("Views/MainWindow.axaml");
        var code = Canon("Greeter.cs");

        File.SetAttributes(code.Value, FileAttributes.ReadOnly);

        try
        {
            var result = await studio.Files.WriteAsync(
                [new FileWrite(form, Bytes("<Window Title=\"A\" />")), new FileWrite(code, Bytes("class Greeter2 { }"))],
                "Сохранение",
                Token);

            Assert.Equal(ProjectsDiagnosticCodes.FileOperationFailed, Code(result));
            Assert.Equal("<Window />", File.ReadAllText(form.Value));
            Assert.Equal("class Greeter { }", File.ReadAllText(code.Value));
        }
        finally
        {
            File.SetAttributes(code.Value, FileAttributes.Normal);
        }

        await studio.SettleHistoryAsync();

        Assert.DoesNotContain(studio.HistoryStore.Actions, action => action.IsSave);
    }

    /// <summary>Запись модель не перечитывает: содержимое документа модели не меняет.</summary>
    [Fact]
    public async Task A_write_does_not_read_the_model_again()
    {
        using var studio = await OpenAsync();
        var loads = studio.Provider.Loads;

        await studio.Files.WriteAsync([new FileWrite(Canon("Views/MainWindow.axaml"), Bytes("<Window Title=\"A\" />"))], "Сохранение", Token);

        Assert.Equal(loads, studio.Provider.Loads);
    }

    /// <summary>
    /// Своё сохранение слежение узнаёт по отпечатку и метит студийным, а чужое — внешним.
    /// </summary>
    /// <remarks>
    /// Так редактор не принимает своё же сохранение за правку Rider, а правку Rider — замечает.
    /// </remarks>
    [Fact]
    public async Task The_own_write_comes_back_as_the_studios_and_a_foreign_one_as_external()
    {
        using var studio = await OpenAsync();
        var form = Canon("Views/MainWindow.axaml");
        var own = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var foreign = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        studio.Files.ContentChanged += (_, e) =>
        {
            foreach (var change in e.Changes.Where(change => change.Change.Path == form))
                (change.Origin == FileChangeOrigin.Studio ? own : foreign).TrySetResult();
        };

        await studio.Files.WriteAsync([new FileWrite(form, Bytes("<Window Title=\"Мой\" />"))], "Сохранение", Token);
        await own.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        Assert.False(foreign.Task.IsCompleted, "своё сохранение пришло внешним");

        File.WriteAllText(form.Value, "<Window Title=\"Rider\" />");

        await foreign.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
    }

    /// <summary>За содержимым следят, только пока кто-то слушает.</summary>
    [Fact]
    public async Task Content_is_watched_only_while_someone_listens()
    {
        using var studio = await OpenAsync();
        var session = studio.Service.Session!;
        EventHandler<FileContentChangedEventArgs> listener = (_, _) => { };

        Assert.False(session.IsWatchingContent, "слежение завелось без слушателя");

        studio.Files.ContentChanged += listener;

        Assert.True(session.IsWatchingContent, "слушатель есть, а слежения нет");

        studio.Files.ContentChanged -= listener;

        Assert.False(session.IsWatchingContent, "слушатель ушёл, а слежение осталось");
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
}
