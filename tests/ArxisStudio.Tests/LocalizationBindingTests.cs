using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Привязка к строкам интерфейса. Смена языка должна перерисовывать уже
/// показанный текст — иначе переключатель в настройках меняет только настройку.
/// </summary>
public class LocalizationBindingTests : IDisposable
{
    public void Dispose()
    {
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);
        GC.SuppressFinalize(this);
    }

    [AvaloniaFact]
    public void Switching_language_updates_text_already_on_screen()
    {
        Localizer.Instance.SetLanguage("ru");

        var text = new TextBlock();
        text.Bind(TextBlock.TextProperty, new LocExtension("projects.open").ProvideValue(null!));

        var window = new Window { Content = text };
        window.Show();

        Assert.Equal("Открыть", text.Text);

        Localizer.Instance.SetLanguage("en");

        Assert.Equal("Open", text.Text);
        window.Close();
    }
}
