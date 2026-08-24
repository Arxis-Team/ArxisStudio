using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>Список недавних проектов: порядок, отсутствие дублей, переживание перезапуска.</summary>
public class RecentProjectsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"arxis-recent-{Guid.NewGuid():N}");

    private string StateFile => Path.Combine(_directory, "recent.json");

    public RecentProjectsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Newest_project_comes_first()
    {
        var recent = new RecentProjects(StateFile);

        recent.Touch(Project("First.sln"));
        recent.Touch(Project("Second.sln"));

        Assert.Equal("Second", recent.Items[0].Name);
        Assert.Equal("First", recent.Items[1].Name);
    }

    [Fact]
    public void Reopening_moves_the_project_up_instead_of_duplicating_it()
    {
        var recent = new RecentProjects(StateFile);

        recent.Touch(Project("First.sln"));
        recent.Touch(Project("Second.sln"));
        recent.Touch(Project("First.sln"));

        Assert.Equal(2, recent.Items.Count);
        Assert.Equal("First", recent.Items[0].Name);
    }

    [Fact]
    public void The_list_survives_a_restart()
    {
        new RecentProjects(StateFile).Touch(Project("App.sln"));

        var reopened = new RecentProjects(StateFile);

        Assert.Single(reopened.Items);
        Assert.Equal("App", reopened.Items[0].Name);
    }

    [Fact]
    public void Removing_a_project_leaves_the_file_on_disk()
    {
        var path = Project("App.sln");
        var recent = new RecentProjects(StateFile);
        recent.Touch(path);

        recent.Remove(path);

        Assert.Empty(recent.Items);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void A_deleted_project_is_reported_as_missing_rather_than_dropped()
    {
        var path = Project("Gone.sln");
        var recent = new RecentProjects(StateFile);
        recent.Touch(path);

        File.Delete(path);

        Assert.Single(recent.Items);
        Assert.False(recent.Items[0].Exists);
    }

    [Fact]
    public void Corrupted_state_does_not_throw()
    {
        File.WriteAllText(StateFile, "{ this is not json");

        Assert.Empty(new RecentProjects(StateFile).Items);
    }

    [Theory]
    [InlineData("WaveChat.sln", "WA")]
    [InlineData("Wave.Chat.Desktop.sln", "WC")]
    [InlineData("A.sln", "A")]
    public void Initials_come_from_the_project_name(string fileName, string expected)
    {
        var recent = new RecentProjects(StateFile);
        recent.Touch(Project(fileName));

        Assert.Equal(expected, recent.Items[0].Initials);
    }

    private string Project(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
