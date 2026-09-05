using ArxisStudio.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ArxisStudio.Splash;

/// <summary>
/// Заставка запуска: что студия показывает, пока до первого окна ещё далеко.
/// </summary>
/// <remarks>
/// От запуска процесса до окна приветствия проходит около девятисот
/// миллисекунд, и всё это время на экране нет ничего: человек нажал и не знает,
/// нажалось ли. Заставка закрывает ровно этот промежуток и уходит, как только
/// открывается настоящее окно.
/// <para>
/// Окно без системной рамки: скруглить её нельзя, а скругление 12 — часть
/// узнаваемости. Поверх задач она встаёт нарочно (<c>Topmost</c>): заставка,
/// уехавшая за чужое окно, оставляет человека с тем же вопросом, ради которого
/// её и показывают.
/// </para>
/// </remarks>
public partial class SplashWindow : Window
{
    /// <summary>
    /// Сколько заставка обязана пробыть на экране.
    /// </summary>
    /// <remarks>
    /// Замерено: до первого окна проходит около секунды, из них 650 мс уходят
    /// на среду и разбор тем — до них показать нечего, — а сами этапы занимают
    /// меньше двухсот. Заставка без этого правила мелькала бы триста
    /// миллисекунд: прочитать за это время нельзя ни строки, и вместо «студия
    /// запускается» человек видел бы вспышку перед окном.
    /// <para>
    /// Плата честная и названа здесь: запуск становится длиннее на разницу.
    /// Правило перестанет что-либо задерживать само собой — как только работы
    /// на запуске станет больше, чем этот срок.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(600);

    private readonly DateTime _shown = DateTime.UtcNow;

    /// <summary>Собирает заставку над моделью запуска.</summary>
    /// <param name="model">Что на ней показывать.</param>
    public SplashWindow(SplashViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        DataContext = model;
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Сколько заставке осталось быть на экране.</summary>
    /// <param name="visible">Сколько она уже показана.</param>
    internal static TimeSpan Rest(TimeSpan visible) =>
        visible < Patience ? Patience - visible : TimeSpan.Zero;

    /// <summary>Дожидается, пока заставку успеют прочитать.</summary>
    public Task LingerAsync() => Task.Delay(Rest(DateTime.UtcNow - _shown));
}
