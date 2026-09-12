using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Что студии сказали в командной строке.
/// </summary>
/// <remarks>
/// Так её зовут из проводника, из терминала и из «Открыть с помощью», и сказанное приходит от
/// человека — значит бывает и неверным. Разбор обязан отличать «не сказали» от «сказали не то»:
/// первое молчит и открывает Welcome, второе пишет в журнал и открывает его же.
/// </remarks>
public class StudioArgumentsTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"arxis-arguments-{Guid.NewGuid():N}")).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Названное решение — то, что студия откроет.</summary>
    [Fact]
    public void A_solution_in_the_arguments_is_what_the_studio_opens()
    {
        var solution = Make("Волна.slnx");

        var asked = StudioArguments.Project([solution]);

        Assert.Equal(solution, asked.Path);
        Assert.Null(asked.Complaint);
    }

    /// <summary>Ключи путями не считаются.</summary>
    [Fact]
    public void Switches_are_not_paths()
    {
        var solution = Make("Волна.sln");

        Assert.Equal(solution, StudioArguments.Project(["--devtools", "-v", solution]).Path);
    }

    /// <summary>Решает первый названный путь: студия открывает одно.</summary>
    [Fact]
    public void The_first_path_decides()
    {
        var first = Make("Первая.slnx");
        var second = Make("Вторая.slnx");

        Assert.Equal(first, StudioArguments.Project([first, second]).Path);
    }

    /// <summary>Относительный путь считается от папки, из которой студию позвали.</summary>
    [Fact]
    public void A_relative_path_is_counted_from_where_the_studio_was_called()
    {
        var asked = StudioArguments.Project(["Волна.slnx"]);

        Assert.Null(asked.Path);
        Assert.Contains(Path.GetFullPath("Волна.slnx"), asked.Complaint);
    }

    /// <summary>Файл не того вида открывать нечем, и об этом надо сказать.</summary>
    [Fact]
    public void A_file_of_the_wrong_kind_is_refused()
    {
        var text = Make("Заметки.txt");

        var asked = StudioArguments.Project([text]);

        Assert.Null(asked.Path);
        Assert.Contains("не решение и не проект", asked.Complaint);
    }

    /// <summary>Путь, ведущий никуда, — тоже повод сказать.</summary>
    [Fact]
    public void A_path_that_leads_nowhere_is_refused()
    {
        var asked = StudioArguments.Project([Path.Combine(_root, "Пропавшая.slnx")]);

        Assert.Null(asked.Path);
        Assert.Contains("файла нет", asked.Complaint);
    }

    /// <summary>Проекта не называли — и это не ошибка.</summary>
    [Fact]
    public void Without_arguments_there_is_nothing_to_open_and_nothing_to_say()
    {
        foreach (var arguments in new[] { null, Array.Empty<string>(), new[] { "--devtools" } })
        {
            var asked = StudioArguments.Project(arguments);

            Assert.Null(asked.Path);
            Assert.Null(asked.Complaint);
        }
    }

    /// <summary>
    /// Решение — <c>.sln</c> и <c>.slnx</c>, проект — всё, что кончается на <c>proj</c>.
    /// </summary>
    /// <remarks>
    /// Список расширений проектов растёт не у студии: так их называют и MSBuild, и модель проектов,
    /// и завтрашний язык назовёт свой так же.
    /// </remarks>
    [Fact]
    public void A_solution_or_anything_ending_in_proj_is_a_project()
    {
        foreach (var name in new[] { "Волна.sln", "Волна.slnx", "App.csproj", "Lib.fsproj", "Старьё.vbproj" })
            Assert.True(StudioArguments.IsProject(name), name);

        foreach (var name in new[] { "Program.cs", "settings.json", "Волна" })
            Assert.False(StudioArguments.IsProject(name), name);
    }

    /// <summary>Кладёт файл и отдаёт полный путь к нему.</summary>
    /// <param name="name">Имя файла.</param>
    private string Make(string name)
    {
        var path = Path.Combine(_root, name);

        File.WriteAllText(path, string.Empty);

        return path;
    }
}
