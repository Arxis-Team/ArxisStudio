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

    private Control? _greet;

    // Панель ушла, а её задача — нет: обход папки идёт своим чередом и по
    // окончании пишет в контрол, которого на экране больше нет. Отменяется он
    // прощанием — тем самым, ради которого у панели и есть Release.
    private CancellationTokenSource? _walking;

    /// <inheritdoc/>
    /// <remarks>
    /// Панель открывают, чтобы поздороваться, — туда каретка и встаёт. Не
    /// назови панель цели, студия отдала бы фокус первому контролу, который
    /// умеет его взять: сегодня это та же кнопка, а завтра — что угодно, что
    /// автор поставит выше неё.
    /// </remarks>
    public override Control? FocusTarget => _greet;

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

        _greet = greet;

        var count = new AxButton();

        count.Bind(ContentControl.ContentProperty, Context.Strings.Text("panel.count"));
        count.Click += async (_, _) => await CountAsync();

        var intro = new TextBlock();

        intro.Bind(TextBlock.TextProperty, Context.Strings.Text("panel.intro"));

        var body = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                intro,
                greet,
                count,
                _result,
            },
        };

        // Размеры — привязкой к теме, а не числом. Это то же, что
        // {DynamicResource ...} в разметке: число не переключилось бы вместе с
        // темой и не сжалось бы вместе с плотностью, а привязка следит за
        // обоими. ARX0010 при сборке называет ключ, если здесь окажется число.
        intro.Bind(TextBlock.FontSizeProperty, intro.GetResourceObservable("AxFontSizeSmall"));
        body.Bind(StackPanel.SpacingProperty, body.GetResourceObservable("AxGapFormRow"));
        body.Bind(Layoutable.MarginProperty, body.GetResourceObservable("AxSpaceWideThickness"));

        return body;
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

        _walking?.Cancel();
        _walking?.Dispose();

        using var walking = new CancellationTokenSource();

        _walking = walking;

        try
        {
            var found = await Context.Tasks.RunAsync(strings["task.walk"], async (progress, token) =>
            {
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, walking.Token);

                token = stop.Token;

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
            // Отменить могли и человеком, и прощанием панели. Во втором случае
            // писать некуда — но и вредного в этом нет: контрол уже снят, и
            // строка никому не покажется.
            _result.Text = strings["panel.cancelled"];
        }
        finally
        {
            if (ReferenceEquals(_walking, walking))
                _walking = null;
        }
    }

    /// <summary>
    /// Панель уходит: обход папки останавливается вместе с ней.
    /// </summary>
    /// <remarks>
    /// Задача живёт своей жизнью и о панели ничего не знает: она досчитала бы
    /// файлы до конца и написала бы ответ в контрол, которого на экране больше
    /// нет. Отменить её может только та, кто её начала, — и <c>Release</c>
    /// затем и существует.
    /// </remarks>
    public override void Release()
    {
        _walking?.Cancel();
        _walking = null;
    }
}
