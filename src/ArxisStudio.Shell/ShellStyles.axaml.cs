using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace ArxisStudio.Shell;

/// <summary>
/// Стили каркаса студии: тема <see cref="StudioShell"/> и общие мелочи экранов.
/// Подключаются приложением после ArxisTheme.
/// </summary>
public partial class ShellStyles : Styles
{
    /// <summary>Создаёт стили и загружает их XAML.</summary>
    /// <param name="sp">Провайдер сервисов из места включения; может быть null.</param>
    public ShellStyles(IServiceProvider? sp = null)
    {
        AvaloniaXamlLoader.Load(sp, this);
    }
}
