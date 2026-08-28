using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правка, сорвавшаяся на живых объектах, не остаётся в документе.
/// </summary>
/// <remarks>
/// Правка идёт в два шага: сперва в документ, потом в объекты на канве. Второй
/// шаг может не удаться — контрол не построится, разметка окажется той, что
/// движок обновлений собрать не может, — и если оставить всё как есть, в
/// тексте правка будет, а на канве её не будет. Дальше человек правит форму,
/// которой не видит.
/// </remarks>
public class EditRollbackTests
{
    /// <summary>
    /// Контрол, падающий при создании, откатывает правку целиком.
    /// </summary>
    /// <remarks>
    /// Разбор такое падение возвращает отказом (AXM3041), а не исключением, —
    /// и откат идёт по ответу. Проверяется здесь именно то, чем это кончается
    /// для человека: текст прежний, история не выросла.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_control_that_refuses_to_build_takes_the_whole_edit_back()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;
        var before = document.Text;
        var history = document.CanUndo;

        var edited = before
            .Replace(
                "x:Class=\"DesignFixtureApp.MainWindow\"",
                "x:Class=\"DesignFixtureApp.MainWindow\"\n        xmlns:local=\"clr-namespace:DesignFixtureApp\"",
                StringComparison.Ordinal)
            .Replace("<ListBox x:Name=\"Notes\"/>", "<local:ExplodingControl/>", StringComparison.Ordinal);

        var error = await document.SetTextAsync(edited, "правка с падающим контролом");

        Assert.NotNull(error);
        Assert.Contains("контрол не строится", error);
        Assert.Equal(before, document.Text);
        Assert.Equal(history, document.CanUndo);

        // Форма осталась той же и на канве: узел, который правка убирала, на
        // месте.
        Assert.NotNull(fixture.Node("Notes"));
    }
}
