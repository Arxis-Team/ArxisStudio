using ArxisStudio.Controls;
using ArxisStudio.Shell.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArxisStudio.Shell;

/// <summary>
/// Место, в которое студия ставит панель плагина.
/// </summary>
/// <remarks>
/// Контрол, упавший на замере или раскладке, роняет весь проход: Avalonia
/// считает дерево целиком и чужого исключения ни от кого не ждёт. Панель
/// плагина, вставленная в дерево напрямую, унесла бы с собой окно студии — со
/// всеми открытыми документами.
/// <para>
/// Поэтому она вставляется сюда. Проход через эту границу обёрнут: упавшая
/// панель заменяется заглушкой, которая говорит, что случилось, и предлагает
/// перезапустить, — а зона, куда человек привык смотреть, остаётся на месте.
/// Пустое место вместо панели было бы хуже: оно ничего не объясняет.
/// </para>
/// <para>
/// Подменять содержимое посреди прохода нельзя — дерево сейчас считают, — и
/// подмена уходит в очередь. До неё панель занимает нулевой размер: раз замер
/// не удался, размера у неё и нет.
/// </para>
/// </remarks>
public sealed class PluginSurface : Decorator
{
    private readonly Action<Exception>? _crashed;
    private readonly Action? _reload;
    private bool _broken;

    /// <summary>Создаёт место для панели.</summary>
    /// <param name="content">Содержимое панели.</param>
    /// <param name="crashed">Кому сказать о падении; null — молча.</param>
    /// <param name="reload">
    /// Чем построить панель заново; null — кнопку перезапуска не показывать.
    /// </param>
    public PluginSurface(Control content, Action<Exception>? crashed = null, Action? reload = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        Child = content;
        _crashed = crashed;
        _reload = reload;
    }

    /// <summary>Панель упала и заменена заглушкой.</summary>
    public bool IsBroken => _broken;

    /// <summary>
    /// Ставит на место построенную заново панель.
    /// </summary>
    /// <param name="content">Свежее содержимое.</param>
    /// <remarks>
    /// Перезапуск начинает счёт заново: новая копия панели отвечает за себя, а
    /// не за грехи прежней. Иначе одна кнопка «Перезапустить» разом и чинила
    /// бы панель, и оставляла её на последнем предупреждении.
    /// </remarks>
    public void Reset(Control content)
    {
        ArgumentNullException.ThrowIfNull(content);

        _broken = false;
        Child = content;
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize) =>
        Guarded(() => base.MeasureOverride(availableSize));

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize) =>
        Guarded(() => base.ArrangeOverride(finalSize));

    private Size Guarded(Func<Size> pass)
    {
        try
        {
            return pass();
        }

        // Отказ процесса перехватывать нечем: после нехватки памяти студия всё
        // равно не продолжится, и делать вид, что панель просто не нарисовалась,
        // значит скрыть настоящую причину.
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            Break(e);

            return default;
        }
    }

    private void Break(Exception error)
    {
        if (_broken)
            return;

        _broken = true;
        _crashed?.Invoke(error);

        // Дерево сейчас считают: подменить ребёнка можно только следующим
        // проходом.
        Dispatcher.UIThread.Post(() => Child = Stub(error), DispatcherPriority.Loaded);
    }

    private Control Stub(Exception error)
    {
        var box = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(16),
            VerticalAlignment = VerticalAlignment.Top,
        };

        box.Children.Add(new TextBlock
        {
            Text = Localizer.Instance["panel.crashed"],
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        // Подробности — то, чем сбой отличается от любого другого: без них
        // заглушка говорит только «сломалось», и человеку нечего сообщить
        // автору плагина.
        box.Children.Add(new TextBlock
        {
            Text = Message(error),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        });

        if (_reload is { } reload)
        {
            var button = new AxButton
            {
                Content = Localizer.Instance["panel.reload"],
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            button.Click += (_, _) => reload();
            box.Children.Add(button);
        }

        return box;
    }

    private static string Message(Exception error) =>
        error is System.Reflection.TargetInvocationException { InnerException: { } inner }
            ? inner.Message
            : error.Message;
}
