using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Arxis.HelloPlugin;

/// <summary>
/// Панель примера. Интерфейс собран на контролах студии — как это и положено
/// плагину.
/// </summary>
[ToolWindow("hello.panel")]
public sealed class HelloPanel : ToolWindow
{
    /// <inheritdoc/>
    protected override Control Build()
    {
        var button = new AxButton { Content = "Поздороваться" };

        button.Click += (_, _) => Context.Commands.Invoke("hello.greet");

        return new StackPanel
        {
            Spacing = 10,
            Margin = new Avalonia.Thickness(12),
            VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                new TextBlock { Text = "Пример внешнего плагина", FontSize = 12.5 },
                button,
            },
        };
    }
}
