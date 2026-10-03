using ArxisStudio.Sdk;
using ArxisStudio.Shell;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ArxisStudio.Dragging;

/// <summary>
/// Перетаскивание между панелями студии: служба, которую она раздаёт расширениям и своим модулям.
/// </summary>
/// <remarks>
/// <para>
/// Тяга своя, на захвате указателя, а не системная: несут только между окнами студии, как вкладки
/// докинга, и мышь, которой водят инструменты студии и тесты, ведёт её так же, как настоящая. Окна
/// спрашиваются по точке экрана — оторванные раньше главного: они его собственные и лежат поверх.
/// </para>
/// <para>
/// Тяга одна на студию — указатель один. Новая бросает прежнюю, как бросило бы её отпускание кнопки:
/// источник, забывший закрыть сеанс, не оставит висеть подсказку у курсора.
/// </para>
/// <para>
/// Целей служба не помнит: цель ищется на каждом движении по дереву окна, а держится, только пока над
/// ней несут. Плагин, выгруженный между двумя тягами, ничем у неё не задержится.
/// </para>
/// </remarks>
public sealed class StudioDrags : IStudioDragDrop
{
    /// <summary>Имя источника в журнале.</summary>
    public const string LogSource = "DragDrop";

    private readonly Func<IEnumerable<TopLevel>> _windows;
    private readonly IStudioLog _log;
    private readonly Func<Exception, bool>? _blame;

    private DragSession? _current;

    /// <summary>Заводит службу над окнами студии.</summary>
    /// <param name="windows">Окна студии в порядке «кто выше»: оторванные, потом главное.</param>
    /// <param name="log">Журнал студии.</param>
    /// <param name="blame">
    /// Приписывает исключение цели плагину по стеку; <c>true</c> — приписано. Пусто — только журнал.
    /// </param>
    public StudioDrags(Func<IEnumerable<TopLevel>> windows, IStudioLog log, Func<Exception, bool>? blame = null)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(log);

        _windows = windows;
        _log = log;
        _blame = blame;
    }

    /// <summary>Открытый сеанс; тяги нет — пусто. Тестам.</summary>
    internal DragSession? Current => _current;

    /// <summary>Подсказка у курсора открытого сеанса; тяги нет — пусто. Тестам.</summary>
    internal DragGhost? Ghost => _current?.Ghost;

    /// <inheritdoc/>
    public Task<DragDropEffects> DragAsync(
        Control source,
        PointerEventArgs trigger,
        StudioDragData data,
        DragDropEffects allowedEffects,
        StudioDragVisual visual)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var session = Open(source, data, allowedEffects, visual);

        return new DragLoop(session, source, trigger).Done;
    }

    /// <inheritdoc/>
    public IStudioDragSession Begin(Control source, StudioDragData data, DragDropEffects allowedEffects, StudioDragVisual visual) =>
        Open(source, data, allowedEffects, visual);

    /// <summary>Сеанс кончился: служба его больше не держит.</summary>
    /// <param name="session">Кончившийся сеанс.</param>
    internal void Ended(DragSession session)
    {
        if (ReferenceEquals(_current, session))
            _current = null;
    }

    /// <summary>
    /// Окна студии сверху вниз и окно источника последним: тест или встроенное окно, которого студия не
    /// перечислила, — всё равно окно, где несут.
    /// </summary>
    /// <param name="own">Окно источника.</param>
    internal IEnumerable<TopLevel> Windows(TopLevel own) => _windows().Append(own).Distinct();

    /// <summary>
    /// Обработчик цели упал: виновник получает сбой, как за всякое необработанное исключение.
    /// </summary>
    /// <param name="target">Цель.</param>
    /// <param name="error">Исключение.</param>
    internal void Fail(Interactive target, Exception error)
    {
        if (_blame?.Invoke(error) == true)
            return;

        _log.Write(StudioLogLevel.Error, LogSource, $"цель перетаскивания {target.GetType().Name} упала: {Faults.Message(error)}");
    }

    /// <summary>Пишет в журнал о нарушенном договоре цели.</summary>
    /// <param name="message">Что не так.</param>
    internal void Warn(string message) => _log.Write(StudioLogLevel.Warning, LogSource, message);

    private DragSession Open(Control source, StudioDragData data, DragDropEffects allowedEffects, StudioDragVisual visual)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(visual);
        Dispatcher.UIThread.VerifyAccess();

        // Прежняя тяга бросается раньше, чем встанет новая: у курсора одна подсказка, а цель прежней
        // должна услышать DragLeave.
        _current?.Dispose();

        var session = new DragSession(this, source, data, allowedEffects, visual);

        _current = session;

        return session;
    }
}
