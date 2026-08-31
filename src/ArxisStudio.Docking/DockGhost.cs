using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Docking;

/// <summary>
/// Призрак окна: обещание того, что получится, если отпустить вкладку сейчас.
/// </summary>
/// <remarks>
/// Настоящее окно, а не плашка внутри вида. Бросок мимо всех окон студии —
/// такой же законный, как бросок в середину области, но рисовать его внутри
/// нечего: под курсором в этот миг чужое приложение или рабочий стол. Так же
/// устроено и у Unity — промерено живьём: призрак виден и над чужим окном, и в
/// середине области, и он там один и тот же.
/// <para>
/// Левый верхний угол призрака — там же, где окажется угол настоящего окна:
/// <see cref="DockFloat"/> заводится от точки курсора, и призрак стоит от неё
/// же. Место обещано точно.
/// </para>
/// <para>
/// Размер — нарочно меньше настоящего. Призрак говорит «здесь будет своё окно»,
/// а не «оно будет вот такое»: рамка в полный рост окна закрывает собой то, ради
/// чего человек и смотрит, — раскладку, по которой он выбирает место. Настоящий
/// размер окно берёт себе само (<see cref="DockFloat.DefaultWidth"/> на
/// <see cref="DockFloat.DefaultHeight"/>), и от призрака он заведомо больше.
/// </para>
/// <para>
/// Ни ввода, ни внимания призрак не забирает: показывается неактивным, не встаёт
/// в панель задач и мыши не ловит. Тяге он не мешал бы и без этого — указатель
/// на время тяги захвачен тем окном, где она началась, — но мигающий фокус
/// человек заметил бы.
/// </para>
/// </remarks>
public class DockGhost : Window
{
    /// <summary>Ширина призрака.</summary>
    /// <remarks>
    /// Меньше настоящего окна и нарочно: в полный рост рамка закрывает собой ту
    /// самую раскладку, по которой человек и выбирает место.
    /// </remarks>
    private const double Wide = 280;

    /// <inheritdoc cref="Wide"/>
    private const double Tall = 160;

    private readonly TextBlock _title = new();

    /// <summary>Заводит призрака и снимает с него всё, что делает окно окном.</summary>
    public DockGhost()
    {
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        Focusable = false;
        IsHitTestVisible = false;

        Width = Wide;
        Height = Tall;

        Content = new Border { Classes = { "dock-ghost" }, Child = _title };
    }

    /// <summary>Ставит призрака под курсор и подписывает несомой панелью.</summary>
    /// <param name="at">Точка на экране — левый верхний угол будущего окна.</param>
    /// <param name="title">Подпись несомой панели.</param>
    /// <param name="owner">Окно, из которого тянут; при нём призрак и живёт.</param>
    public void Follow(PixelPoint at, string title, Window? owner)
    {
        _title.Text = title;

        // Заголовок окну не показывают — показывать его негде, — но по нему
        // призрака узнают средства отладки и списки окон системы.
        Title = title;
        Position = at;

        if (IsVisible)
            return;

        // Хозяин нужен, чтобы призрак не пережил студию и не остался висеть над
        // пустым местом, если тягу оборвали закрытием окна.
        if (owner is { IsVisible: true })
            Show(owner);
        else
            Show();
    }
}
