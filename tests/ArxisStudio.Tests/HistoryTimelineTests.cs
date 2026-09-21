using System.Text;
using ArxisStudio.Modules.Project.History;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Каким файл был перед каждой строкой его истории — и чем его байты показать.
/// </summary>
public class HistoryTimelineTests
{
    private static readonly CanonicalPath File =
        CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "arxis-timeline", "Views", "Main.axaml"));

    /// <summary>
    /// Перед правкой — её «до», перед появлением файла не было, а переезд папки и метка содержимого
    /// не меняют.
    /// </summary>
    [Fact]
    public void Before_each_row_the_file_was_what_the_row_took_away()
    {
        var states = Timeline.Before(
        [
            Row(new LocalHistoryChange { Kind = LocalHistoryChangeKind.Modified, Path = File, Before = Content("c2"), After = Content("c3") }),
            Row(new LocalHistoryChange { Kind = LocalHistoryChangeKind.Moved, Path = File.Directory, From = File.Directory, IsDirectory = true }),
            Label(),
            Row(new LocalHistoryChange { Kind = LocalHistoryChangeKind.Modified, Path = File, Before = Content("c1"), After = Content("c2") }),
            Row(new LocalHistoryChange { Kind = LocalHistoryChangeKind.Created, Path = File, After = Content("c1") }),
        ]);

        Assert.Equal(
        [
            new FileState(FileStateKind.Stored, Content("c2")),
            new FileState(FileStateKind.Stored, Content("c2")),
            new FileState(FileStateKind.Stored, Content("c2")),
            new FileState(FileStateKind.Stored, Content("c1")),
            FileState.Absent,
        ], states);
    }

    /// <summary>
    /// Самая новая строка без правки — метка — показывает файл таким, как сейчас; удалённое — тем, что
    /// удалили; содержимое, которого история не хранит, так и называется.
    /// </summary>
    [Fact]
    public void Labels_deletions_and_what_is_not_kept_are_told_apart()
    {
        var states = Timeline.Before(
        [
            Label(),
            Row(new LocalHistoryChange { Kind = LocalHistoryChangeKind.Deleted, Path = File, Before = Content("gone") }),
            Row(new LocalHistoryChange { Kind = LocalHistoryChangeKind.Modified, Path = File, TooLarge = true }),
            Row(new LocalHistoryChange { Kind = LocalHistoryChangeKind.Moved, Path = File, From = File.Directory.Combine("Old.axaml") }),
        ]);

        Assert.Equal(
            [FileState.Now, new FileState(FileStateKind.Stored, Content("gone")), FileState.NotStored, FileState.NotStored],
            states);
    }

    /// <summary>
    /// Текст узнаётся по отметке порядка байт или как UTF-8; нулевой байт — двоичный файл, а не
    /// прочитавшийся UTF-8 читается побайтно, но читается.
    /// </summary>
    [Fact]
    public void Text_is_told_by_its_mark_and_binary_by_a_zero_byte()
    {
        Assert.Equal("было", TextSniff.Decode([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("было")]));
        Assert.Equal("было", TextSniff.Decode([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("было")]));
        Assert.Equal("было", TextSniff.Decode(Encoding.UTF8.GetBytes("было")));
        Assert.Null(TextSniff.Decode([0x4D, 0x5A, 0x00, 0x03]));
        Assert.Equal("ÿx", TextSniff.Decode([0xFF, 0x78]));
    }

    private static LocalHistoryContent Content(string id) => new(id);

    private static LocalHistoryRevision Row(LocalHistoryChange change) => new()
    {
        Action = new LocalHistoryAction
        {
            Id = 1,
            Time = DateTimeOffset.UnixEpoch,
            Label = "правка",
            Origin = LocalHistoryOrigin.External,
            Changes = [change],
        },
        Changes = [change],
    };

    private static LocalHistoryRevision Label() => new()
    {
        Action = new LocalHistoryAction
        {
            Id = 2,
            Time = DateTimeOffset.UnixEpoch,
            Label = "метка",
            Origin = LocalHistoryOrigin.Studio,
        },
    };
}
