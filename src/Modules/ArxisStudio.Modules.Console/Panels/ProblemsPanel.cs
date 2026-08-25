using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ArxisStudio.Modules.Console;

/// <summary>
/// Панель «Проблемы»: место для диагностик проекта.
/// </summary>
/// <remarks>
/// Пока панель пуста: диагностики модели решения студия собирает, но приводить
/// их к строкам с переходом на место в файле — работа, у которой есть свой
/// срок. Панель заведена сейчас, чтобы к тому времени было куда их положить.
/// </remarks>
[Sdk.ToolWindow("console.problems")]
public sealed class ProblemsPanel : Sdk.ToolWindow
{
    /// <inheritdoc/>
    protected override Control Build()
    {
        var empty = new TextBlock
        {
            Classes = { "dimmer" },
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        empty.Bind(
            TextBlock.TextProperty,
            new Avalonia.Data.Binding(nameof(LocalizedString.Value)) { Source = Localizer.Instance.Track("panel.problems.none") });

        return empty;
    }
}
