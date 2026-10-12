using System.Text.Json;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>Список недавних проектов: порядок, отсутствие дублей, переживание перезапуска.</summary>
public class RecentProjectsTests : IDisposable
{
    private readonly string _directory = TempFolder.Create("recent");

    private string StateFile => Path.Combine(_directory, "recent.json");

    public void Dispose()
    {
        TempFolder.Erase(_directory, strict: true);

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

    /// <summary>
    /// Пустая запись в файле пропадает при чтении, а целые остаются.
    /// </summary>
    /// <remarks>
    /// <c>null</c> в списке и запись без пути — законный JSON, и разбор отдавал их как есть: открытие
    /// следующего проекта падало на пустой записи, а плитка Welcome — на имени без пути (запись 383).
    /// </remarks>
    [Fact]
    public void Holes_in_the_file_are_dropped_and_the_rest_kept()
    {
        var kept = Project("WaveChat.sln");

        File.WriteAllText(StateFile, $$"""
            [ null, { "path": null, "openedAt": "2026-10-12T10:00:00+03:00" },
              { "path": {{JsonSerializer.Serialize(kept)}}, "openedAt": "2026-10-12T09:00:00+03:00" } ]
            """);

        var recent = new RecentProjects(StateFile);

        Assert.Equal(kept, Assert.Single(recent.Items).Path);
        Assert.Equal("WA", recent.Items[0].Initials);

        recent.Touch(Project("Other.sln"));

        Assert.Equal(2, recent.Items.Count);
    }

    /// <summary>Убирают по тем же правилам пути, по каким добавляют.</summary>
    /// <remarks>
    /// В списке лежат полные пути, и сравнение с пришедшим как есть не нашло бы записи, названной
    /// относительным путём. Пока убирать было некому, разойтись этим двум было негде.
    /// </remarks>
    [Fact]
    public void Removing_follows_the_same_path_rules_as_adding()
    {
        var recent = new RecentProjects(StateFile);

        recent.Touch(Project("WaveChat.sln"));
        recent.Remove(Path.Combine(_directory, ".", "WaveChat.sln"));

        Assert.Empty(recent.Items);
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
