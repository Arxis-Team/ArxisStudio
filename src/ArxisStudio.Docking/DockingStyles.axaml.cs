using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace ArxisStudio.Docking;

/// <summary>
/// Стили движка докинга: вид группы вкладок и границы между областями.
/// Подключаются приложением после темы студии.
/// </summary>
public partial class DockingStyles : Styles
{
    /// <summary>Создаёт стили и загружает их XAML.</summary>
    /// <param name="sp">Провайдер сервисов из места включения; может быть null.</param>
    public DockingStyles(IServiceProvider? sp = null)
    {
        AvaloniaXamlLoader.Load(sp, this);
    }
}
