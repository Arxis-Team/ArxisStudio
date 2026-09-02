using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Arxis.HelloPlugin;

/// <summary>
/// Свой контрол примера в полосе студии: кнопка со значком и подписью.
/// </summary>
/// <remarks>
/// Кнопку такого вида студия нарисовала бы и по манифесту; здесь она собрана
/// руками, чтобы показать дорогу для того, чего манифестом не описать. Плата —
/// плагин поднимается при старте: чужой контрол не нарисовать, не подняв
/// плагин. Подпись — привязкой к словарю, тем же ключом, что пункт меню: смена
/// языка в студии должна перерисовать и её.
/// </remarks>
[ToolBarItem("hello.strip")]
public sealed class HelloStrip : ToolBarItem
{
    /// <inheritdoc/>
    protected override Control Build()
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };

        label.Bind(TextBlock.TextProperty, Context.Strings.Text("command.greet"));

        var button = new AxButton
        {
            Classes = { "ghost", "compact" },
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new AxIcon { Classes = { "small" }, Data = AxIcons.Star },
                    label,
                },
            },
        };

        // Та же команда, что у пункта меню и у кнопки на панели: дорога одна.
        button.Click += (_, _) => Context.Commands.Invoke("hello.greet");

        return button;
    }
}
