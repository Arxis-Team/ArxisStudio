using ArxisStudio.Sdk;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArxisStudio.Modules.Console;

/// <summary>
/// Панель «Проблемы»: находки студии, модулей и плагинов.
/// </summary>
/// <remarks>
/// Панель ничего не проверяет сама — она показывает то, что сообщили другие:
/// модель решения, разбор разметки, плагин со своей проверкой. Двойной щелчок
/// открывает файл: находка без пути к файлу — почти всегда находка, с которой
/// нечего делать.
/// </remarks>
[Sdk.ToolWindow("console.problems")]
public sealed class ProblemsPanel : Sdk.ToolWindow
{
    private readonly ListBox _list = new() { Background = Brushes.Transparent };
    private readonly TextBlock _empty = new()
    {
        Classes = { "dimmer", "small" },
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <inheritdoc/>
    protected override Control Build()
    {
        _empty.Bind(
            TextBlock.TextProperty,
            new Avalonia.Data.Binding(nameof(LocalizedString.Value)) { Source = Localizer.Instance.Track("panel.problems.none") });

        _list.ItemTemplate = new FuncDataTemplate<StudioProblem>((problem, _) => Row(problem), supportsRecycling: true);
        _list.DoubleTapped += OnDoubleTapped;

        if (Context.GetService<IStudioProblems>() is { } problems)
        {
            problems.Changed += (_, _) => Show(problems);
            Show(problems);
        }
        else
        {
            Show(null);
        }

        return new Panel { Children = { _empty, _list } };
    }

    private void Show(IStudioProblems? problems)
    {
        var found = problems?.All ?? [];

        _list.ItemsSource = found;
        _list.IsVisible = found.Count > 0;
        _empty.IsVisible = found.Count == 0;
    }

    /// <summary>Строка находки: серьёзность, код, сообщение и место.</summary>
    private static Control Row(StudioProblem problem)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Height = 18,
        };

        row.Children.Add(Severity(problem.Severity));
        row.Children.Add(Cell(problem.Code, 64, "AxFg3Brush"));
        row.Children.Add(new TextBlock
        {
            Classes = { "small" },
            Text = problem.Message,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        if (problem.Where is { Length: > 0 } where)
            row.Children.Add(Cell(where, double.NaN, "AxFg3Brush"));

        return row;
    }

    private static TextBlock Severity(StudioProblemSeverity severity)
    {
        var cell = new TextBlock
        {
            Classes = { "mono", "small" },
            Width = 48,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Text = severity switch
            {
                StudioProblemSeverity.Error => "ERROR",
                StudioProblemSeverity.Warning => "WARN",
                _ => "INFO",
            },
        };

        cell.Classes.Add(severity switch
        {
            StudioProblemSeverity.Error => "bad",
            StudioProblemSeverity.Warning => "warn",
            _ => "ok",
        });

        return cell;
    }

    private static TextBlock Cell(string text, double width, string brushKey)
    {
        var cell = new TextBlock
        {
            Classes = { "mono", "small" },
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        if (!double.IsNaN(width))
            cell.Width = width;

        cell.Bind(TextBlock.ForegroundProperty, cell.GetResourceObservable(brushKey));
        return cell;
    }

    private async void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_list.SelectedItem is not StudioProblem { FilePath: { Length: > 0 } path })
            return;

        if (Context.GetService<IStudioDocuments>() is { } documents)
            await documents.OpenAsync(path);
    }
}
