using ArxisStudio.Docking;
using Avalonia.Threading;

namespace ArxisStudio.Services;

/// <summary>
/// Отложенная запись раскладки: правки копятся, файл пишется, когда человек договорил движение.
/// </summary>
/// <remarks>
/// Правок много и они частые: тянут границу — десятки за секунду, щёлкают по вкладкам — каждый
/// щелчок. Писать файл на каждую значит стучать по диску весь день; ждать конца сеанса — потерять
/// раскладку при жёстком закрытии. Пауза даёт договорить движение и записывает уже итог.
/// <para>
/// Вынесено из <see cref="StudioDock"/>: у раскладки своя работа — дерево, окна и каретка, — а
/// здесь одна забота: когда писать и когда уже нельзя.
/// </para>
/// </remarks>
internal sealed class DockLayoutWriter
{
    /// <summary>Сколько ждать перед записью.</summary>
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(2);

    private readonly DockLayoutStore? _store;
    private readonly Func<DockLayout?> _snapshot;
    private readonly Action<string> _complain;
    private readonly DispatcherTimer? _timer;
    private bool _dirty;
    private bool _closed;

    /// <summary>Заводит запись над хранилищем.</summary>
    /// <param name="store">Куда писать; null — никуда, и правки только копятся.</param>
    /// <param name="snapshot">Раскладка, как она ложится в файл; null — писать нечего.</param>
    /// <param name="complain">Кому сказать, что файл не записался.</param>
    public DockLayoutWriter(DockLayoutStore? store, Func<DockLayout?> snapshot, Action<string> complain)
    {
        _store = store;
        _snapshot = snapshot;
        _complain = complain;

        if (store is not null)
            _timer = new DispatcherTimer(Pause, DispatcherPriority.Background, (_, _) => Flush());
    }

    /// <summary>
    /// Помечает раскладку изменившейся и заводит отсчёт заново.
    /// </summary>
    /// <remarks>
    /// Заново с каждой правкой: пока границу тянут, писать нечего — итог станет известен, когда её
    /// отпустят.
    /// </remarks>
    public void Note()
    {
        _dirty = true;
        _timer?.Stop();
        _timer?.Start();
    }

    /// <summary>Помечает раскладку изменившейся, не заводя отсчёта: записать её попросят сразу.</summary>
    public void Touch() => _dirty = true;

    /// <summary>
    /// Записывает раскладку, не дожидаясь паузы.
    /// </summary>
    /// <remarks>
    /// Нужно при закрытии окна и при смене набора: отложенная запись до них просто не доживёт.
    /// </remarks>
    public void Flush()
    {
        _timer?.Stop();

        if (_closed || !_dirty || _store is null || _snapshot() is not { } layout)
            return;

        _dirty = false;

        if (_store.Save(layout) is { } complaint)
            _complain(complaint);
    }

    /// <summary>
    /// Записывает раскладку в последний раз и больше не пишет.
    /// </summary>
    /// <remarks>
    /// Студия закрывается: окна, закрываясь следом, вернут панели домой, и эта правка, дойдя до
    /// файла, стёрла бы из него сами окна.
    /// </remarks>
    public void Close()
    {
        Flush();

        _closed = true;
        _timer?.Stop();
    }
}
