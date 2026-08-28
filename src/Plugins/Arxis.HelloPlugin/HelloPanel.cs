using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Arxis.HelloPlugin;

/// <summary>
/// Панель примера. Интерфейс собран на контролах студии — как это и положено
/// плагину.
/// </summary>
[ToolWindow("hello.panel")]
public sealed class HelloPanel : ToolWindow
{
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap };

    /// <inheritdoc/>
    /// <remarks>
    /// Подписи не написаны в коде, а взяты из словарей плагина — теми же
    /// ключами, какими названы панель и пункт меню в манифесте. Привязкой, а не
    /// строкой: смена языка в студии должна перерисовать и панель плагина.
    /// </remarks>
    protected override Control Build()
    {
        var greet = new AxButton();

        greet.Bind(ContentControl.ContentProperty, Context.Strings.Text("command.greet"));
        greet.Click += (_, _) => Context.Commands.Invoke("hello.greet");

        var count = new AxButton();

        count.Bind(ContentControl.ContentProperty, Context.Strings.Text("panel.count"));
        count.Click += async (_, _) => await CountAsync();

        var intro = new TextBlock { FontSize = 12.5 };

        intro.Bind(TextBlock.TextProperty, Context.Strings.Text("panel.intro"));

        return new StackPanel
        {
            Spacing = 10,
            Margin = new Avalonia.Thickness(12),
            VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                intro,
                greet,
                count,
                _result,
            },
        };
    }

    /// <summary>
    /// Считает файлы проекта в фоне.
    /// </summary>
    /// <remarks>
    /// Обход папки — дело недолгое, но именно такого рода: на большом проекте
    /// он занимает секунды, а сделанный в потоке интерфейса заморозил бы студию
    /// целиком. Здесь показано всё, ради чего заведены фоновые задачи: имя,
    /// ход, отмена и возвращение в поток интерфейса.
    /// </remarks>
    private async Task CountAsync()
    {
        // Ответ на нажатие — не подпись: он рассказывает о том, что случилось
        // только что, и берётся индексатором на языке этой минуты.
        var strings = Context.Strings;

        if (Context.ProjectPath is not { Length: > 0 } path)
        {
            _result.Text = strings["panel.noproject"];
            return;
        }

        var folder = System.IO.Path.GetDirectoryName(path)!;

        try
        {
            var found = await Context.Tasks.RunAsync(strings["task.walk"], async (progress, token) =>
            {
                var files = System.IO.Directory.EnumerateFiles(folder, "*", System.IO.SearchOption.AllDirectories).ToList();
                var counted = 0;

                foreach (var file in files)
                {
                    counted++;
                    progress.Report((double)counted / files.Count, $"{counted} / {files.Count}");

                    // Настоящая работа была бы здесь. Ожидание нужно примеру,
                    // чтобы человек успел увидеть и полосу, и то, что отмена
                    // действительно работает.
                    await Task.Delay(15, token);
                }

                return counted;
            });

            // Сюда мы вернулись в поток интерфейса: об этом позаботился await,
            // потому что начато всё было в нём.
            _result.Text = $"{strings["panel.counted"]}: {found}";
        }
        catch (OperationCanceledException)
        {
            _result.Text = strings["panel.cancelled"];
        }
    }
}
