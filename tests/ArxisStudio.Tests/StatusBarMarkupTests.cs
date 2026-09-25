using System.Xml.Linq;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Строка состояния главного окна: кому место отмеряется первым.
/// </summary>
/// <remarks>
/// Главное окно в наборе не строит никто — оно поднимает раскладку и читает папку плагинов, — и
/// сторож читает саму разметку, как <see cref="WindowPlacementTests"/>. Спрашивает он порядок: в
/// <c>DockPanel</c> место раздаётся по очереди детей, и стоящий позже получает то, что осталось.
/// </remarks>
public class StatusBarMarkupTests
{
    /// <summary>
    /// Указатель перезапуска стоит в полосе раньше сообщения, и длинное сообщение его не вытесняет.
    /// </summary>
    /// <remarks>
    /// Найдено живой проверкой: путь показанного документа занимал строку целиком, а указатель,
    /// стоявший после него, уезжал за край вместе со ссылкой «Перезапустить».
    /// </remarks>
    [Fact]
    public void The_restart_notice_takes_its_place_before_the_status_message()
    {
        var markup = MarkupSources.All().Single(source => source.Name == "MainWindow.axaml").Text;
        var bar = XDocument.Parse(markup).Descendants().Single(element => element.Name.LocalName == "StudioShell.StatusBar");
        var children = bar.Elements().Single().Elements().ToList();

        var notice = children.FindIndex(child => (string?)child.Attribute("IsVisible") == "{Binding IsRestartRequired}");
        var message = children.FindIndex(child => (string?)child.Attribute("Text") == "{Binding Status}");

        Assert.True(notice >= 0, "в строке состояния нет указателя перезапуска");
        Assert.True(message >= 0, "в строке состояния нет сообщения");
        Assert.True(
            notice < message,
            "указатель перезапуска стоит после сообщения: длинный путь документа вытолкнет его за край вместе со ссылкой");
    }
}
