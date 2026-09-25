using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Порядок, в котором перезапущенная студия возвращает открытое.
/// </summary>
/// <remarks>
/// Шаги подменены записью в журнал теста: главное окно в наборе не строит никто, а порядок несущий
/// — проект раньше документов, выборы групп после них, показанный документ последним из выборов,
/// каретка за ним, окно настроек в самом конце.
/// </remarks>
public class StudioResumeTests
{
    private readonly List<string> _steps = [];
    private readonly StudioLog _log = new();

    /// <summary>Шаги идут в объявленном порядке.</summary>
    [Fact]
    public async Task The_workspace_comes_back_in_its_order()
    {
        var settings = new SettingsSession { Page = "studio.plugins" };

        await Resume(_ => true).RunAsync(new StudioSession
        {
            Studio = true,
            Project = "Hello.slnx",
            Documents = ["a.cs", "b.md"],
            Onstage = ["left", "doc:b.md"],
            Active = "doc:a.cs",
            Focused = "doc:a.cs",
            Settings = settings,
            Reasons = new Dictionary<string, string> { ["probe.held"] = "Badge" },
        });

        Assert.Equal(
            [
                "проект Hello.slnx",
                "документ a.cs",
                "документ b.md",
                "показ left",
                "показ doc:b.md",
                "показ doc:a.cs",
                "каретка doc:a.cs",
                "настройки studio.plugins",
            ],
            _steps);

        Assert.Contains(_log.Records, record => record.Message.Contains("Badge", StringComparison.Ordinal));
    }

    /// <summary>Документ, файла которого больше нет, пропускается, и остальные открываются.</summary>
    [Fact]
    public async Task A_document_whose_file_is_gone_is_skipped()
    {
        await Resume(path => path != "gone.cs").RunAsync(new StudioSession
        {
            Studio = true,
            Documents = ["gone.cs", "kept.cs"],
        });

        Assert.Equal(["документ kept.cs"], _steps);
        Assert.Contains(_log.Records, record =>
            record.Level == StudioLogLevel.Warning && record.Message.Contains("gone.cs", StringComparison.Ordinal));
    }

    /// <summary>Упавший шаг не останавливает следующих.</summary>
    [Fact]
    public async Task A_failed_step_does_not_stop_the_rest()
    {
        var resume = Resume(_ => true, _ => throw new InvalidOperationException("решение не читается"));

        await resume.RunAsync(new StudioSession
        {
            Studio = true,
            Project = "Broken.slnx",
            Documents = ["a.cs"],
            Settings = new SettingsSession(),
        });

        Assert.Equal(["документ a.cs", "настройки "], _steps);
        Assert.Contains(_log.Records, record =>
            record.Level == StudioLogLevel.Error && record.Message.Contains("решение не читается", StringComparison.Ordinal));
    }

    private StudioResume Resume(Func<string, bool> exists, Func<string, Task>? openProject = null) => new()
    {
        Log = _log,
        Exists = exists,
        OpenProject = openProject ?? (path =>
        {
            _steps.Add($"проект {path}");
            return Task.CompletedTask;
        }),
        OpenDocument = path =>
        {
            _steps.Add($"документ {path}");
            return Task.CompletedTask;
        },
        Show = id => _steps.Add($"показ {id}"),
        Focus = id => _steps.Add($"каретка {id}"),
        OpenSettings = settings => _steps.Add($"настройки {settings.Page}"),
    };
}
