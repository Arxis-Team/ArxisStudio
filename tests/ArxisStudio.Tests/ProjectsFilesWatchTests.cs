using System.Collections.Immutable;
using ArxisStudio.Modules.Projects.Watching;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Служба файлов и слежение за диском вместе: одна правка — одно перечитывание модели.
/// </summary>
/// <remarks>
/// <para>
/// Слежение настоящее, а перечитывание после правки остановлено на входе в движок: пачка правки
/// приходит, пока оно идёт, — как в студии, где склейка ждёт четверть секунды, а загрузка решения
/// идёт дольше. Так каждое переименование и перечитывало модель дважды: пачка сверялась со
/// снимком, в котором файл ещё лежал под старым именем.
/// </para>
/// <para>
/// Пачку правки тест подаёт сам и сразу, а не ждёт настоящих событий. Те приходят когда придут и
/// сверяются с тем же диском, поэтому исход от их прихода не зависит.
/// </para>
/// </remarks>
public sealed class ProjectsFilesWatchTests() : ProjectsOnDisk("projects-files-watch")
{
    private static readonly FileChangeCoalescingOptions Quick = new()
    {
        QuietPeriod = TimeSpan.FromMilliseconds(50),
        MaximumDelay = TimeSpan.FromMilliseconds(500),
    };

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private ProjectsWatch? _watch;

    /// <summary>Переименование перечитывает модель один раз — и пачка правки второго не просит.</summary>
    [Fact]
    public async Task A_rename_rereads_the_model_once()
    {
        using var studio = await OpenAsync(history: false, watch: Watch);

        var status = await EditWhileRereadingAsync(
            studio,
            () => studio.Files.MoveAsync([Pair("Greeter.cs", "Hello.cs")], "Переименование Greeter.cs", Token),
            "Greeter.cs",
            "Hello.cs");

        Assert.Equal(ProjectsLoadReason.Files, status.LastLoad?.Reason);
        Assert.Equal(2, studio.Provider.Loads);
        Assert.Contains(LibOf(status).Items, item => item.FullPath == Canon("Hello.cs"));
    }

    /// <summary>
    /// Переименование, переписавшее файл проекта, перечитывает модель один раз.
    /// </summary>
    /// <remarks>
    /// Ссылку <c>None Update</c> служба переписывает в файле проекта, а файл проекта — вход оценки:
    /// снимок сверяет его по пути, и без отпечатка содержимого его эхо просило бы загрузку и после
    /// перечитывания, которое его уже прочло.
    /// </remarks>
    [Fact]
    public async Task A_rename_that_rewrites_the_project_file_rereads_the_model_once()
    {
        using var studio = await OpenAsync(history: false, watch: Watch);

        var status = await EditWhileRereadingAsync(
            studio,
            () => studio.Files.MoveAsync([Pair("Views/Readme.txt", "Views/Notes.txt")], "Переименование Readme.txt", Token),
            "Lib.csproj",
            "Views/Readme.txt",
            "Views/Notes.txt");

        Assert.Contains("Views\\Notes.txt", File.ReadAllText(At("Lib.csproj")), StringComparison.Ordinal);
        Assert.Equal(ProjectsLoadReason.Files, status.LastLoad?.Reason);
        Assert.Equal(2, studio.Provider.Loads);
    }

    /// <summary>
    /// Файл, появившийся снаружи после того, как перечитывание прочло папку, перечитывает модель ещё
    /// раз — и называет причиной его, а не правку службы.
    /// </summary>
    [Fact]
    public async Task An_outside_file_the_reread_did_not_see_rereads_the_model_again()
    {
        using var studio = await OpenAsync(history: false, watch: Watch);

        var extra = At("Extra.cs");
        var listed = studio.Provider.Answer!;

        studio.Provider.Answer = request =>
        {
            var result = listed(request);

            if (!File.Exists(extra))
            {
                File.WriteAllText(extra, "class Extra { }");
                _watch!.Report(extra);
                _watch.Flush();
            }

            return result;
        };

        var reloaded = studio.WhenAsync(status => status.LastLoad?.Reason == ProjectsLoadReason.FileSystem, Patience);
        var moved = await studio.Files.MoveAsync([Pair("Greeter.cs", "Hello.cs")], "Переименование Greeter.cs", Token);

        Assert.False(moved.HasErrors, Said(moved));

        var status = await reloaded;

        Assert.Equal([CanonicalPath.Create(extra)], status.LastLoad!.Causes);
        Assert.Contains(LibOf(status).Items, item => item.FullPath == CanonicalPath.Create(extra));
    }

    /// <summary>
    /// Неудачное перечитывание не выдаёт правку за прочитанную: её перемены — и переписанный файл
    /// проекта среди них — перечитывают модель снова.
    /// </summary>
    /// <remarks>
    /// Отпечаток файла проекта говорит «модель его прочла», только если перечитывание опубликовало
    /// снимок. Без этого провал выглядел бы прочтением, и файл проекта выпал бы из причин.
    /// </remarks>
    [Fact]
    public async Task A_failed_reread_leaves_the_edit_to_the_watch()
    {
        using var studio = await OpenAsync(history: false, watch: Watch);

        var listed = studio.Provider.Answer!;
        var failed = 0;

        studio.Provider.Answer = request => Interlocked.Exchange(ref failed, 1) == 0
            ? Solutions.Failure("APS2001", request.EntryPointPath)
            : listed(request);

        var status = await EditWhileRereadingAsync(
            studio,
            () => studio.Files.MoveAsync([Pair("Views/Readme.txt", "Views/Notes.txt")], "Переименование Readme.txt", Token),
            "Lib.csproj",
            "Views/Readme.txt",
            "Views/Notes.txt");

        Assert.Equal(ProjectsLoadReason.FileSystem, status.LastLoad?.Reason);
        Assert.Contains(Canon("Lib.csproj"), status.LastLoad!.Causes);
        Assert.Contains(LibOf(status).Items, item => item.FullPath == Canon("Views/Notes.txt"));
    }

    private IProjectsWatch Watch(Action<ImmutableArray<CanonicalPath>> stale) =>
        _watch = new ProjectsWatch(stale, Quick);

    /// <summary>
    /// Правка, чья пачка приходит, пока модель перечитывается, — и состояние службы, когда всё
    /// успокоилось.
    /// </summary>
    /// <param name="studio">Студия.</param>
    /// <param name="start">
    /// Как начать правку службы файлов: начинается она, когда остановка перечитывания уже стоит.
    /// </param>
    /// <param name="echoes">Что правка тронула на диске: пути от папки проекта Lib.</param>
    private async Task<ProjectsStatus> EditWhileRereadingAsync(
        ProjectsStudio studio,
        Func<Task<ProjectOperationResult>> start,
        params string[] echoes)
    {
        var gate = new LoadGate();

        studio.Provider.Gate = gate;

        var edit = start();

        await gate.Entered.WaitAsync(Patience, Token);

        foreach (var echo in echoes)
            _watch!.Report(At(echo));

        _watch!.Flush();
        gate.Release();

        var result = await edit.WaitAsync(Patience, Token);

        Assert.False(result.HasErrors, Said(result));

        return await studio.WhenAsync(status => !status.IsLoading, Patience);
    }

    private static ProjectSnapshot LibOf(ProjectsStatus status) =>
        Assert.Single(status.Snapshot?.Projects ?? [], project => project.Name == "Lib");
}
