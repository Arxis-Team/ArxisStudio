using System.Diagnostics;
using ArxisStudio.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

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
/// <para>
/// В панели задач она есть, хотя «заставка — не окно» велело бы обратное. Довод
/// простой: почти секунду это единственное окно студии, и без кнопки человек,
/// ушедший на Alt+Tab в ту самую секунду, которую он и ждёт, вернуться к ней не
/// может. Так же сделано у Rider.
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

    private readonly Stopwatch _shown = new();

    /// <summary>Собирает заставку над моделью запуска.</summary>
    /// <param name="model">Что на ней показывать.</param>
    public SplashWindow(SplashViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        DataContext = model;
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Часы заставки идут с её появления, а не с её создания.
    /// </summary>
    /// <remarks>
    /// Отсчёт стоял в инициализаторе поля — то есть до <c>AvaloniaXamlLoader</c>
    /// и задолго до <c>Show</c>. Заставка успевала «пробыть на экране» то время,
    /// пока её собирали, и срок, ради которого правило заведено, выходил раньше,
    /// чем человек что-либо видел.
    /// </remarks>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _shown.Restart();
    }

    /// <summary>Сколько заставка на экране; до показа — нисколько.</summary>
    internal TimeSpan Visible => _shown.Elapsed;

    /// <summary>Сколько заставке осталось быть на экране.</summary>
    /// <param name="visible">Сколько она уже показана.</param>
    internal static TimeSpan Rest(TimeSpan visible) =>
        visible < Patience ? Patience - visible : TimeSpan.Zero;

    /// <summary>
    /// Дожидается, пока заставку успеют прочитать.
    /// </summary>
    /// <remarks>
    /// Уступка потоку в конце — безусловная, и это не перестраховка. Когда
    /// этапы занимают больше срока, ждать нечего, и <c>Task.Delay(Zero)</c>
    /// завершается синхронно: продолжение идёт тем же заходом, прохода
    /// отрисовки не случается, и кадр со стопроцентной полосой не рисуется
    /// никогда. Человек видит заставку, замершую на предпоследнем этапе.
    /// </remarks>
    public Task LingerAsync() => LingerAsync(_shown.Elapsed);

    /// <summary>Дожидается срока от названного времени показа.</summary>
    /// <param name="visible">Сколько заставка уже на экране.</param>
    /// <remarks>
    /// Шов ради проверки, и узкий нарочно: ждать в тесте настоящие шестьсот
    /// миллисекунд значило бы заменить координацию сном, а проверять надо не
    /// срок, а то, что кадр отдают в любом случае.
    /// </remarks>
    internal async Task LingerAsync(TimeSpan visible)
    {
        if (Rest(visible) is { Ticks: > 0 } rest)
            await Task.Delay(rest);

        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Esc закрывает заставку, но только отказавшую.
    /// </summary>
    /// <remarks>
    /// На идущем запуске отменять нечем: этапы про отмену пока не знают, и
    /// закрытое окно оставило бы студию поднимающейся втайне.
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is SplashViewModel { HasFailed: true })
        {
            Close();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// Кнопка на отказе: окно уходит, студия — за ним.
    /// </summary>
    /// <remarks>
    /// Само окно только закрывается. Завершает студию тот, кто её поднимал: на
    /// запуске она живёт при <c>OnExplicitShutdown</c>, и закрытое окно её не
    /// остановит — процесс остался бы в памяти без единого окна.
    /// </remarks>
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
