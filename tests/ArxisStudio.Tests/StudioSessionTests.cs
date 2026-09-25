using ArxisStudio.Services;
using ArxisStudio.ViewModels;
using Avalonia;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Сессия перезапуска: файл между двумя копиями студии и место окна в нём.
/// </summary>
/// <remarks>
/// Файл — это и договор копий: прежняя ждёт его исчезновения, новая стирает прочитанное. Поэтому
/// проверяется не только круг туда и обратно, но и то, что испорченный файл остаётся на месте:
/// стёртый, он сказал бы прежней «принято» о том, что не прочиталось.
/// </remarks>
public class StudioSessionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"arxis-session-{Guid.NewGuid():N}");

    public StudioSessionTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Записанное читается тем же, и файл после этого стёрт.</summary>
    [Fact]
    public void A_session_comes_back_as_written_and_the_file_is_gone()
    {
        var file = StudioSession.FileFor(_folder, 4242);
        var written = new StudioSession
        {
            Studio = true,
            Window = new StudioPlacement(-1900, 40, 1280.5, 800, 1.5, Maximized: true),
            Project = @"C:\Проекты\Hello World\Hello.slnx",
            Documents = [@"C:\Проекты\Hello World\Program.cs", @"C:\Проекты\Hello World\README.md"],
            Onstage = ["arxis.project:project.panel", @"doc:C:\Проекты\Hello World\Program.cs"],
            Active = @"doc:C:\Проекты\Hello World\Program.cs",
            Focused = "arxis.project:project.panel",
            Settings = new SettingsSession
            {
                Page = "studio.plugins",
                Search = "код",
                Plugin = "arxis.codeviewer",
                Folded = new Dictionary<string, bool> { ["builtin"] = true },
                Window = new StudioPlacement(100, 120, 900, 640, 1, Maximized: false),
            },
            Welcome = new WelcomeSession(WelcomeSection.Learn, "hello", Plugins: true),
            Reasons = new Dictionary<string, string> { ["arxis.codeviewer"] = "AvaloniaEdit заводит свои свойства" },
        };

        StudioSession.Write(written, file);

        var read = StudioSession.Take(file, out var complaint);

        Assert.Null(complaint);
        Assert.NotNull(read);
        Assert.False(File.Exists(file), "прочитанная сессия осталась на диске — прежняя копия не дождётся ответа");

        Assert.True(read.Studio);
        Assert.Equal(written.Window, read.Window);
        Assert.Equal(written.Project, read.Project);
        Assert.Equal(written.Documents, read.Documents);
        Assert.Equal(written.Onstage, read.Onstage);
        Assert.Equal(written.Active, read.Active);
        Assert.Equal(written.Focused, read.Focused);
        Assert.Equal(written.Welcome, read.Welcome);
        Assert.Equal(written.Reasons, read.Reasons);

        var settings = Assert.IsType<SettingsSession>(read.Settings);

        Assert.Equal("studio.plugins", settings.Page);
        Assert.Equal("код", settings.Search);
        Assert.Equal("arxis.codeviewer", settings.Plugin);
        Assert.Equal(written.Settings.Folded, settings.Folded);
        Assert.Equal(written.Settings.Window, settings.Window);
    }

    /// <summary>
    /// Испорченный файл не бросает и остаётся на месте: ответить им прежней копии нельзя.
    /// </summary>
    [Fact]
    public void A_broken_session_is_refused_without_a_throw_and_left_in_place()
    {
        var file = Path.Combine(_folder, "restart-1.json");

        File.WriteAllText(file, "{ это не json");

        Assert.Null(StudioSession.Take(file, out var complaint));
        Assert.NotNull(complaint);
        Assert.True(File.Exists(file), "испорченная сессия стёрта — прежняя копия сочла бы её принятой");
    }

    /// <summary>Файла нет — сессии нет, и это не исключение.</summary>
    [Fact]
    public void A_missing_session_is_refused_without_a_throw()
    {
        Assert.Null(StudioSession.Take(Path.Combine(_folder, "restart-2.json"), out var complaint));
        Assert.NotNull(complaint);
    }

    /// <summary>Пустоты вместо списков приходят пустыми списками, а не <c>null</c>.</summary>
    /// <remarks>
    /// Восстановление перебирает списки не спрашивая: <c>null</c>, пришедший из файла мимо
    /// компилятора, уронил бы его посреди шагов.
    /// </remarks>
    [Fact]
    public void Nulls_in_the_file_become_empty_lists()
    {
        var file = Path.Combine(_folder, "restart-3.json");

        File.WriteAllText(file, """
            { "Studio": true, "Documents": null, "Onstage": [ null, "" , "left" ], "Reasons": null,
              "Settings": { "Search": null, "Folded": null }, "Welcome": { "Section": "Projects", "Filter": null, "Plugins": false } }
            """);

        var read = StudioSession.Take(file, out var complaint);

        Assert.Null(complaint);
        Assert.NotNull(read);
        Assert.Empty(read.Documents);
        Assert.Equal(["left"], read.Onstage);
        Assert.Empty(read.Reasons);
        Assert.Equal(string.Empty, read.Settings!.Search);
        Assert.Empty(read.Settings.Folded);
        Assert.Equal(string.Empty, read.Welcome!.Filter);
    }

    /// <summary>Окно, которое видно на экране, встаёт туда, где стояло.</summary>
    [Fact]
    public void A_window_on_a_screen_lands_where_it_stood()
    {
        var placement = new StudioPlacement(100, 80, 800, 600, 1, Maximized: false);

        Assert.Equal(
            new PixelPoint(100, 80),
            placement.Land([new PixelRect(0, 0, 1920, 1080)], new PixelRect(0, 0, 1920, 1040)));
    }

    /// <summary>Окно с отключённого монитора встаёт посреди основного.</summary>
    [Fact]
    public void A_window_from_a_lost_monitor_lands_in_the_middle_of_the_primary()
    {
        var placement = new StudioPlacement(-1800, 100, 800, 600, 1, Maximized: false);

        Assert.Equal(
            new PixelPoint(560, 220),
            placement.Land([new PixelRect(0, 0, 1920, 1080)], new PixelRect(0, 0, 1920, 1040)));
    }

    /// <summary>
    /// Размер в точках переводится в пиксели масштабом экрана, на котором окно стояло.
    /// </summary>
    /// <remarks>
    /// Позиция у Avalonia в пикселях, а размер в точках. Окно шириной 400 точек при масштабе 2 —
    /// это 800 пикселей, и край его, свешенный с монитора слева, задевает экран: двигать такое окно
    /// значит спорить с человеком. Без перевода прямоугольник кончался бы раньше экрана, и окно
    /// уезжало бы в середину.
    /// </remarks>
    [Fact]
    public void The_size_is_turned_into_pixels_by_the_scaling_of_its_screen()
    {
        var placement = new StudioPlacement(-700, 100, 400, 300, 2, Maximized: false);

        Assert.Equal(
            new PixelPoint(-700, 100),
            placement.Land([new PixelRect(0, 0, 3840, 2160)], new PixelRect(0, 0, 3840, 2100)));
    }
}
