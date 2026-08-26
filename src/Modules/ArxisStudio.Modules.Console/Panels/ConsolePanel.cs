using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArxisStudio.Modules.Console;

/// <summary>
/// Панель «Консоль»: журнал студии — сборка, запуск и всё, что пишут плагины.
/// </summary>
/// <remarks>
/// Журнал панель не ведёт, а читает: писать в него может кто угодно, а показан
/// он в одном месте. Строки лежат в наблюдаемой коллекции, поэтому список
/// обновляется сам — панели остаётся только доматывать его вниз.
/// </remarks>
[Sdk.ToolWindow("console.log")]
public sealed class ConsolePanel : Sdk.ToolWindow
{
    private readonly ScrollViewer _scroll = new();
    private readonly ItemsControl _list = new() { Margin = new Avalonia.Thickness(10, 4) };

    /// <inheritdoc/>
    protected override Control Build()
    {
        var feed = Context.GetService<IStudioLogFeed>();

        _list.ItemTemplate = new FuncDataTemplate<StudioLogRecord>((record, _) => Row(record), supportsRecycling: true);
        _list.ItemsSource = feed?.Records;
        _scroll.Content = _list;

        var clear = new AxButton
        {
            Classes = { "compact", "ghost" },
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Avalonia.Thickness(8, 6, 0, 0),
        };

        clear.Bind(
            ContentControl.ContentProperty,
            new Avalonia.Data.Binding(nameof(LocalizedString.Value)) { Source = Localizer.Instance.Track("console.clear") });

        clear.Click += (_, _) => feed?.Clear();

        if (feed is not null)
            feed.Changed += (_, _) => _scroll.ScrollToEnd();

        var root = new DockPanel();

        DockPanel.SetDock(clear, Dock.Top);
        root.Children.Add(clear);
        root.Children.Add(_scroll);

        return root;
    }

    /// <summary>Строка журнала: время, уровень, источник и текст.</summary>
    private static Control Row(StudioLogRecord record)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Height = 18 };

        row.Children.Add(Cell(record.Stamp, 56, dim: true));
        row.Children.Add(Level(record));
        row.Children.Add(Source(record.Source));
        row.Children.Add(new TextBlock
        {
            Classes = { "mono", "small" },
            Text = record.Message,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        return row;
    }

    private static TextBlock Cell(string text, double width, bool dim)
    {
        var cell = new TextBlock
        {
            Classes = { "mono", "small" },
            Width = width,
            Text = text,
        };

        if (dim)
            cell.Classes.Add("dimmer");

        return cell;
    }

    private static TextBlock Level(StudioLogRecord record)
    {
        var cell = new TextBlock
        {
            Classes = { "mono", "small" },
            Width = 48,
            FontWeight = FontWeight.SemiBold,
            Text = record.LevelName,
        };

        // Цвет уровня — единственное, что здесь видно боковым зрением: ошибку
        // в потоке сборки ищут глазами, а не чтением.
        cell.Classes.Add(record.Level switch
        {
            StudioLogLevel.Error => "bad",
            StudioLogLevel.Warning => "warn",
            StudioLogLevel.Info => "ok",
            _ => "dimmer",
        });

        return cell;
    }

    private static TextBlock Source(string source)
    {
        var cell = Cell(source, 52, dim: false);

        cell.Bind(TextBlock.ForegroundProperty, cell.GetResourceObservable("AxFg3Brush"));
        return cell;
    }
}
