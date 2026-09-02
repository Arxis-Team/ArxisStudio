using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Arxis.MyPlugin;

/// <summary>
/// Панель плагина: студия ставит её в зону, объявленную манифестом.
/// </summary>
/// <remarks>
/// Идентификатор в атрибуте — тот же, что в <c>contributions.toolWindows</c>:
/// место студия отводит по манифесту, а класс ищет по атрибуту, когда плагин
/// поднят. Разойдутся — человек увидит пустое место вместо панели, и об этом
/// скажет анализатор при сборке.
/// <para>
/// Интерфейс строится на контролах <c>Ax*</c>: панель плагина стоит рядом с
/// панелями студии, и разнобой видно сразу. Правило проверяет
/// <c>ARX0001</c>; панели раскладки, рамки, текст и фигуры под него не
/// попадают — своего оформления они не несут.
/// </para>
/// </remarks>
[ToolWindow("panel")]
public sealed class PluginPanel : ToolWindow
{
    /// <inheritdoc/>
    protected override Control Build()
    {
        var hint = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        // Привязкой, а не строкой: смена языка в студии должна перерисовать и
        // панель плагина.
        hint.Bind(TextBlock.TextProperty, Context.Strings.Text("panel.hint"));

        var button = new AxButton();

        button.Bind(ContentControl.ContentProperty, Context.Strings.Text("command.hello"));

        // Через команду, а не напрямую: та же дорога, что у пункта меню и у
        // кнопки в полосе, — и одно место, где написано, что делать.
        button.Click += (_, _) => Context.Commands.Invoke("arxis.my-plugin.hello");

        return new StackPanel
        {
            Spacing = 10,
            Margin = new Avalonia.Thickness(12),
            VerticalAlignment = VerticalAlignment.Top,
            Children = { hint, button },
        };
    }
}
