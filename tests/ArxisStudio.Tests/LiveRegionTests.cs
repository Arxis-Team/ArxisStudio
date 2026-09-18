using System.Xml.Linq;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Сказанное студией человеку экранный диктор читает сам, не дожидаясь, пока до строки дойдут.
/// </summary>
/// <remarks>
/// Строка состояния, полоса Welcome и итог действия в менеджере плагинов — единственные места, где
/// студия отвечает на просьбу: «файл нечем открыть», «плагин не встал». Зрячий видит ответ краем
/// глаза, а диктору он без живой области нем: каретка стоит там, где человек что-то делал, и туда
/// ответ не приходит. Живая область — вежливая: диктор дочитывает начатое и говорит ответ следом.
/// <para>
/// Правило — по разметке самой студии, а не по списку окон: сообщение в ней привязано к свойству
/// <c>Status</c>. Строка пути в окне проекта тоже зовётся так, но она модуля и меняется на каждый
/// выбор, а выбор диктор уже прочёл, — живой ей быть незачем.
/// </para>
/// </remarks>
public class LiveRegionTests
{
    private const string Avalonia = "https://github.com/avaloniaui";

    /// <summary>Каждое сообщение человеку в разметке студии — вежливая живая область.</summary>
    [Fact]
    public void Every_message_the_studio_tells_is_a_polite_live_region()
    {
        var messages = MarkupSources.Own()
            .SelectMany(source => XDocument.Parse(source.Text).Descendants(XName.Get("TextBlock", Avalonia))
                .Where(text => text.Attribute("Text")?.Value == "{Binding Status}")
                .Select(text => (source.Name, Live: text.Attribute("AutomationProperties.LiveSetting")?.Value)))
            .ToList();

        Assert.Contains(messages, message => message.Name == "MainWindow.axaml");
        Assert.Contains(messages, message => message.Name == "WelcomeWindow.axaml");
        Assert.Contains(messages, message => message.Name == "SettingsWindow.axaml");

        var silent = messages.Where(message => message.Live != "Polite").Select(message => message.Name).ToList();

        Assert.True(silent.Count == 0, "сообщение диктору немо: " + string.Join(", ", silent));
    }
}
