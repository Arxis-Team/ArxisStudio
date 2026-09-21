using System.Text;
using ArxisStudio.LocalHistory;
using ArxisStudio.Modules.Projects;
using ArxisStudio.Modules.Projects.Files;
using ArxisStudio.Modules.Projects.History;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Служба файлов на решении, лежащем на диске: перенос, копия, удаление, отказы, откат и история.
/// </summary>
/// <remarks>
/// Провайдер теста MSBuild не зовёт — снимок он собирает сам, но папки проектов в нём настоящие, и
/// диск правится настоящий. Историю служба ведёт во временной папке теста.
/// </remarks>
public sealed class ProjectsFilesTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"arxis-projects-files-{Guid.NewGuid():N}")).FullName;

    private string Solution => Path.Combine(_root, "solution");

    private string Lib => Path.Combine(Solution, "Lib");

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
    /// Файл вместе с вложенным переименовывается одним действием истории, и модель перечитана.
    /// </summary>
    [Fact]
    public async Task A_file_and_its_companion_are_renamed_as_one_action()
    {
        using var studio = await OpenAsync();
        var changed = Record(studio);

        var result = await studio.Files.MoveAsync(
        [
            Pair("Views/MainWindow.axaml", "Views/Main.axaml"),
            Pair("Views/MainWindow.axaml.cs", "Views/Main.axaml.cs"),
        ], "Переименование MainWindow.axaml", Token);

        await studio.Thread.IdleAsync();

        Assert.False(result.HasErrors, Said(result));
        Assert.True(File.Exists(At("Views/Main.axaml")) && File.Exists(At("Views/Main.axaml.cs")), "файлы не переехали");
        Assert.False(File.Exists(At("Views/MainWindow.axaml")), "под старым именем файл остался");

        var action = Store(studio).Actions[^1];

        Assert.Equal("Переименование MainWindow.axaml", action.Label);
        Assert.Equal(HistoryOrigin.Studio, action.Origin);
        Assert.All(action.Changes, change => Assert.Equal(HistoryChangeKind.Moved, change.Kind));
        Assert.Equal(2, action.Changes.Length);
        Assert.NotNull(Store(studio).Known(At("Views/Main.axaml.cs")));
        Assert.Null(Store(studio).Known(At("Views/MainWindow.axaml.cs")));

        Assert.Equal(2, Assert.Single(changed).Moved.Length);
        Assert.Equal(ProjectsLoadReason.Files, studio.Projects.Status.LastLoad?.Reason);
    }

    /// <summary>
    /// Смена одного регистра доходит до диска — у файла и у папки: для Windows это то же имя, и
    /// перенос папки прямо на него отказывает.
    /// </summary>
    [Fact]
    public async Task A_change_of_case_alone_reaches_the_disk()
    {
        using var studio = await OpenAsync();

        var result = await studio.Files.MoveAsync([Pair("Greeter.cs", "greeter.cs"), Pair("Views", "views")], "Переименование", Token);

        Assert.False(result.HasErrors, Said(result));
        Assert.Equal(["appsettings.json", "greeter.cs", "Lib.csproj"], Directory.GetFiles(Lib).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(["views"], Directory.GetDirectories(Lib).Select(Path.GetFileName));
    }

    /// <summary>
    /// Переехавшая папка уносит ссылки файла проекта, а сам файл проекта остаётся байт в байт — кроме
    /// переписанного.
    /// </summary>
    /// <remarks>
    /// Отметка порядка байт и переводы строк — то, что пишет редактор человека; файл, у которого их
    /// поменяли, весь светится в разнице. Правка файла проекта — такая же правка, и она в том же
    /// действии истории.
    /// </remarks>
    [Fact]
    public async Task A_moved_folder_takes_the_project_file_references_along()
    {
        using var studio = await OpenAsync();

        var result = await studio.Files.MoveAsync([Pair("Views", "Screens")], "Перенос Views", Token);

        Assert.False(result.HasErrors, Said(result));

        var bytes = File.ReadAllBytes(At("Lib.csproj"));
        var text = Encoding.UTF8.GetString(bytes);

        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), "у файла проекта пропала отметка порядка байт");
        Assert.Contains("Update=\"Screens\\Readme.txt\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Views\\Readme.txt", text, StringComparison.Ordinal);
        Assert.Equal(text.Split('\n').Length - 1, text.Split("\r\n").Length - 1);

        var action = Store(studio).Actions[^1];

        Assert.Contains(action.Changes, change => change is { Kind: HistoryChangeKind.Moved, IsDirectory: true });
        Assert.Contains(action.Changes, change => change.Kind == HistoryChangeKind.Modified && change.Path == At("Lib.csproj"));
    }

    /// <summary>
    /// Файл проекта, в котором правка ничего не задела, записывается байт в байт — с отметкой порядка
    /// байт, объявлением и переводами строк вперемешку.
    /// </summary>
    /// <remarks>
    /// Читатель XML по спецификации приводит CRLF к LF, и без выключенного приведения правка одной
    /// ссылки переписывала бы каждую строку файла.
    /// </remarks>
    [Fact]
    public void An_untouched_project_file_goes_back_byte_for_byte()
    {
        Directory.CreateDirectory(Lib);

        var path = At("Mixed.csproj");
        byte[] original =
        [
            .. Encoding.UTF8.Preamble,
            .. Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Project>\n  <ItemGroup>\r\n    <None Update=\"a.txt\" Pack=\"true\" />\n  </ItemGroup>\r\n</Project>\r\n"),
        ];

        File.WriteAllBytes(path, original);

        var edit = ProjectFileEdit.Open(path) ?? throw new InvalidOperationException("файл проекта не открылся");

        edit.Save();

        Assert.Equal(original, File.ReadAllBytes(path));
    }

    /// <summary>
    /// Копия — файла и папки целиком — пишется появлением с содержимым, и её событие называет, что
    /// откуда.
    /// </summary>
    [Fact]
    public async Task A_copy_is_written_as_created_with_its_content()
    {
        using var studio = await OpenAsync();
        var changed = Record(studio);

        var result = await studio.Files.CopyAsync([Pair("Greeter.cs", "Greeter2.cs"), Pair("Views", "Views2")], "Копирование", Token);

        await studio.Thread.IdleAsync();

        Assert.False(result.HasErrors, Said(result));
        Assert.Equal(File.ReadAllText(At("Greeter.cs")), File.ReadAllText(At("Greeter2.cs")));
        Assert.True(File.Exists(At("Views2/MainWindow.axaml.cs")), "папка скопировалась не вся");

        var changes = Store(studio).Actions[^1].Changes;
        var copy = Assert.Single(changes, change => change.Path == At("Greeter2.cs"));

        Assert.Equal(HistoryChangeKind.Created, copy.Kind);
        Assert.Equal(Store(studio).Known(At("Greeter.cs"))?.Content, copy.After);
        Assert.Contains(changes, change => change is { Kind: HistoryChangeKind.Created, IsDirectory: true } && change.Path == At("Views2"));
        Assert.Equal(2, Assert.Single(changed).Copied.Length);
    }

    /// <summary>Копия, которой диск отказал посередине, убирает уже созданное.</summary>
    [Fact]
    public async Task A_copy_the_disk_refused_halfway_takes_back_what_it_made()
    {
        using var studio = await OpenAsync();

        Result result;

        using (new FileStream(At("appsettings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = new Result(await studio.Files.CopyAsync(
                [Pair("Views", "Views2"), Pair("appsettings.json", "settings.json")], "Копирование", Token));
        }

        Assert.Equal(ProjectsDiagnosticCodes.FileOperationFailed, result.Code);
        Assert.False(Directory.Exists(At("Views2")), "созданное до отказа осталось");
        Assert.False(File.Exists(At("settings.json")), "файл, которому отказали, появился");
        Assert.Empty(Store(studio).Actions);
    }

    /// <summary>
    /// Удалённое уходит с диска насовсем, а его содержимое остаётся в истории — оттуда его и вернут.
    /// </summary>
    /// <remarks>
    /// Файл, выбранный вместе со своей папкой, уходит с ней и записывается один раз: выбор в окне
    /// бывает любым, а у истории одна запись на одну пропажу.
    /// </remarks>
    [Fact]
    public async Task Deleted_content_stays_in_the_history()
    {
        using var studio = await OpenAsync();
        var greeter = File.ReadAllBytes(At("Greeter.cs"));

        var result = await studio.Files.DeleteAsync([Canon("Greeter.cs"), Canon("Views"), Canon("Views/Readme.txt")], "Удаление", Token);

        Assert.False(result.HasErrors, Said(result));
        Assert.False(File.Exists(At("Greeter.cs")) || Directory.Exists(At("Views")), "удалённое осталось на диске");

        var store = Store(studio);
        var changes = store.Actions[^1].Changes;
        var gone = changes.Single(change => change.Path == At("Greeter.cs"));

        Assert.Equal(HistoryChangeKind.Deleted, gone.Kind);
        Assert.Equal(greeter, store.Read(gone.Before!.Value));
        Assert.Contains(changes, change => change.Path == At("Views/MainWindow.axaml") && change.Before is not null);
        Assert.Single(changes, change => change.Path == At("Views/Readme.txt"));
        Assert.Contains(changes, change => change is { IsDirectory: true, Kind: HistoryChangeKind.Deleted });
        Assert.DoesNotContain("Readme.txt", File.ReadAllText(At("Lib.csproj")), StringComparison.Ordinal);
    }

    /// <summary>
    /// Всё, что проверяемо заранее, отказывает до первого байта — и диск остаётся как был.
    /// </summary>
    /// <remarks>
    /// Файл проекта, выход сборки и папка проекта недоступны и как источник, и как назначение:
    /// перенос на место файла проекта — не «имя занято», а правка, которой здесь не бывает.
    /// </remarks>
    [Fact]
    public async Task Refusals_come_before_the_first_byte()
    {
        using (var closed = new ProjectsStudio())
        {
            Assert.Equal(ProjectsDiagnosticCodes.NothingOpen, Code(await closed.Files.DeleteAsync([Canon("Greeter.cs")], "Удаление", Token)));
        }

        using var studio = await OpenAsync();

        Directory.CreateDirectory(At("bin/Debug"));
        File.WriteAllText(At("bin/Debug/Lib.dll"), "собранное");

        Assert.Equal(ProjectsDiagnosticCodes.OutsideProjects,
            Code(await studio.Files.DeleteAsync([CanonicalPath.Create(Path.Combine(Solution, "Notes.md"))], "Удаление", Token)));
        Assert.Equal(ProjectsDiagnosticCodes.OutsideProjects, Code(await studio.Files.DeleteAsync([Canon("Lib.csproj")], "Удаление", Token)));
        Assert.Equal(ProjectsDiagnosticCodes.OutsideProjects, Code(await studio.Files.DeleteAsync([CanonicalPath.Create(Lib)], "Удаление", Token)));
        Assert.Equal(ProjectsDiagnosticCodes.OutsideProjects,
            Code(await studio.Files.MoveAsync([Pair("Greeter.cs", "Lib.csproj")], "Переименование", Token)));
        Assert.Equal(ProjectsDiagnosticCodes.OutsideProjects, Code(await studio.Files.DeleteAsync([Canon("bin/Debug/Lib.dll")], "Удаление", Token)));
        Assert.Equal(ProjectsDiagnosticCodes.OutsideProjects,
            Code(await studio.Files.CopyAsync([Pair("Greeter.cs", "bin/Debug/Greeter.cs")], "Копирование", Token)));
        Assert.Equal(ProjectsDiagnosticCodes.TargetExists,
            Code(await studio.Files.MoveAsync([Pair("Greeter.cs", "appsettings.json")], "Переименование", Token)));
        Assert.Equal(ProjectsDiagnosticCodes.IntoItself,
            Code(await studio.Files.MoveAsync([Pair("Views", "Views/Inner")], "Перенос", Token)));
        Assert.Equal(ProjectsDiagnosticCodes.Missing,
            Code(await studio.Files.MoveAsync([Pair("Missing.cs", "Found.cs")], "Переименование", Token)));
        Assert.Equal(ProjectsDiagnosticCodes.Missing,
            Code(await studio.Files.MoveAsync([Pair("Greeter.cs", "Nowhere/Greeter.cs")], "Перенос", Token)));

        Assert.True(File.Exists(At("Greeter.cs")) && File.Exists(At("appsettings.json")) && Directory.Exists(At("Views"))
            && File.Exists(At("bin/Debug/Lib.dll")) && !File.Exists(At("bin/Debug/Greeter.cs")), "отказ тронул диск");
        Assert.Empty(Store(studio).Actions);
    }

    /// <summary>
    /// Решение, лежащее в папке своего проекта, правкой не трогается — как и сам файл проекта.
    /// </summary>
    /// <remarks>
    /// У решения из одного проекта <c>.sln</c> обычно лежит рядом с <c>.csproj</c>, и по месту оно —
    /// файл проекта, а удалить или переименовать его значило бы закрыть решение из-под студии.
    /// </remarks>
    [Fact]
    public async Task A_solution_beside_its_project_stays_out_of_reach()
    {
        var single = Directory.CreateDirectory(Path.Combine(_root, "single")).FullName;
        var entry = CanonicalPath.Create(Path.Combine(single, "App.slnx"));

        File.WriteAllText(entry.Value, "<Solution />");
        File.WriteAllText(Path.Combine(single, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(single, "Program.cs"), "class Program { }");

        using var studio = new ProjectsStudio();

        studio.Provider.Answer = request => Solutions.Beside(request, "App", "Program.cs");

        Assert.True((await studio.Projects.OpenAsync(entry, Token)).HasSnapshot, "решение не открылось");

        Assert.Equal(ProjectsDiagnosticCodes.OutsideProjects, Code(await studio.Files.DeleteAsync([entry], "Удаление", Token)));
        Assert.Equal(ProjectsDiagnosticCodes.OutsideProjects,
            Code(await studio.Files.MoveAsync([new FileMove(entry, entry.Directory.Combine("Renamed.slnx"))], "Переименование", Token)));
        Assert.True(File.Exists(entry.Value), "решение тронуто");
    }

    /// <summary>Перенос, которому диск отказал посередине, возвращает уже перенесённое.</summary>
    [Fact]
    public async Task A_move_the_disk_refused_halfway_puts_back_what_it_moved()
    {
        using var studio = await OpenAsync();

        Result result;

        using (new FileStream(At("appsettings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = new Result(await studio.Files.MoveAsync(
            [
                Pair("Greeter.cs", "Hello.cs"),
                Pair("appsettings.json", "settings.json"),
            ], "Перенос", Token));
        }

        Assert.Equal(ProjectsDiagnosticCodes.FileOperationFailed, result.Code);
        Assert.True(File.Exists(At("Greeter.cs")), "перенесённое до отказа не вернулось");
        Assert.False(File.Exists(At("Hello.cs")), "перенесённое до отказа осталось на новом месте");
        Assert.Empty(Store(studio).Actions);
    }

    /// <summary>
    /// Удаление, которому диск отказал посередине, записывает в историю то, что успело уйти.
    /// </summary>
    /// <remarks>Удалённого не вернуть откатом — только историей, и она обязана его знать.</remarks>
    [Fact]
    public async Task A_delete_the_disk_refused_halfway_keeps_what_went_in_the_history()
    {
        using var studio = await OpenAsync();
        var changed = Record(studio);

        Result result;

        using (new FileStream(At("appsettings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = new Result(await studio.Files.DeleteAsync([Canon("Greeter.cs"), Canon("appsettings.json")], "Удаление", Token));

        await studio.Thread.IdleAsync();

        Assert.Equal(ProjectsDiagnosticCodes.FileOperationFailed, result.Code);
        Assert.False(File.Exists(At("Greeter.cs")));
        Assert.True(File.Exists(At("appsettings.json")));

        var gone = Assert.Single(Store(studio).Actions[^1].Changes, change => change.Kind == HistoryChangeKind.Deleted);

        Assert.Equal(At("Greeter.cs"), gone.Path);
        Assert.NotNull(gone.Before);
        Assert.Equal([Canon("Greeter.cs")], Assert.Single(changed).Deleted);
    }

    /// <summary>
    /// Наблюдатель истории, увидевший правку службы на диске, повтором её не пишет.
    /// </summary>
    [Fact]
    public async Task The_history_watcher_does_not_write_the_studio_action_again()
    {
        using var studio = await OpenAsync();

        await studio.Files.MoveAsync([Pair("Greeter.cs", "Hello.cs")], "Переименование Greeter.cs", Token);

        var host = Host(studio);
        var watcher = host.Session?.History ?? throw new InvalidOperationException("история сессии не ведётся");

        watcher.Report(At("Greeter.cs"));
        watcher.Report(At("Hello.cs"));
        watcher.Flush();
        await Settle(host.History);

        Assert.Equal(["Переименование Greeter.cs"], Store(studio).Actions.Select(action => action.Label));
    }

    /// <summary>
    /// Файл, которого история ещё не видела, переезжает без того, чтобы наблюдатель записал его
    /// появившимся на новом месте.
    /// </summary>
    /// <remarks>
    /// Файл создан мимо студии, и пачка наблюдателя о нём ещё не пришла: служба снимает его на новом
    /// месте сама. Если пачка успела раньше переноса, она записала появление под прежним именем, и
    /// это тоже правда, — поэтому проверяется только новое имя.
    /// </remarks>
    [Fact]
    public async Task A_file_the_history_has_not_seen_moves_without_becoming_a_newcomer()
    {
        using var studio = await OpenAsync();

        File.WriteAllText(At("Fresh.cs"), "class Fresh { }");

        await studio.Files.MoveAsync([Pair("Fresh.cs", "Moved.cs")], "Переименование Fresh.cs", Token);

        var host = Host(studio);
        var watcher = host.Session?.History ?? throw new InvalidOperationException("история сессии не ведётся");

        watcher.Report(At("Fresh.cs"));
        watcher.Report(At("Moved.cs"));
        watcher.Flush();
        await Settle(host.History);

        Assert.DoesNotContain(
            Store(studio).Actions.Where(action => action.Origin == HistoryOrigin.External).SelectMany(action => action.Changes),
            change => change.Path == At("Moved.cs"));
        Assert.NotNull(Store(studio).Known(At("Moved.cs")));
    }

    /// <summary>
    /// Файл, переехавший в другой проект, для прежнего удалён: его запись снимается, а не
    /// переписывается путём в чужую папку — иначе прежний проект взял бы чужой файл к себе.
    /// </summary>
    /// <remarks>Метаданные с файлом не едут — так же поступает Visual Studio.</remarks>
    [Fact]
    public async Task A_file_moved_to_another_project_leaves_its_entry_behind()
    {
        using var studio = await OpenAsync();
        var readme = Path.Combine(App, "Readme.txt");

        var result = await studio.Files.MoveAsync(
            [new FileMove(Canon("Views/Readme.txt"), CanonicalPath.Create(readme))], "Перенос Readme.txt", Token);

        Assert.False(result.HasErrors, Said(result));
        Assert.True(File.Exists(readme), "файл не переехал");
        Assert.DoesNotContain("Readme", File.ReadAllText(At("Lib.csproj")), StringComparison.Ordinal);
        Assert.DoesNotContain("Readme", File.ReadAllText(Path.Combine(App, "App.csproj")), StringComparison.Ordinal);
    }

    private string History => Path.Combine(_root, "history");

    private string App => Path.Combine(Solution, "App");

    /// <summary>Решение на диске, открытое службой с историей, — и опорный снимок истории снят.</summary>
    private async Task<ProjectsStudio> OpenAsync()
    {
        Directory.CreateDirectory(Path.Combine(Lib, "Views"));
        File.WriteAllText(Path.Combine(Solution, "Hello.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(Solution, "Notes.md"), "# рядом с решением, но не в проекте");
        File.WriteAllText(At("Lib.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <None Update="Views\Readme.txt" CopyToOutputDirectory="Always" />
              </ItemGroup>
            </Project>
            """.ReplaceLineEndings("\r\n"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(At("Greeter.cs"), "class Greeter { }");
        File.WriteAllText(At("appsettings.json"), "{ }");
        File.WriteAllText(At("Views/MainWindow.axaml"), "<Window />");
        File.WriteAllText(At("Views/MainWindow.axaml.cs"), "partial class MainWindow { }");
        File.WriteAllText(At("Views/Readme.txt"), "прочти");
        Directory.CreateDirectory(App);
        File.WriteAllText(Path.Combine(App, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(App, "Program.cs"), "class Program { }");

        var studio = new ProjectsStudio(historyRoot: History);

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

    /// <summary>Итог с кодом первой диагностики — чтобы ассерты читались.</summary>
    private sealed record Result(ProjectOperationResult Operation)
    {
        public string? Code => Operation.Diagnostics.FirstOrDefault()?.Code;
    }
}
