namespace ArxisStudio.Modules.Console;

/// <summary>
/// Место встречи команды и панели: команда просит, панель делает.
/// </summary>
/// <remarks>
/// Панель создаёт студия, когда ставит её в раскладку, а команды заявляет
/// точка входа модуля при подъёме — друг о друге они не знают. Просьба,
/// пришедшая раньше панели, не теряется: панель заберёт её, как только
/// построится.
/// <para>
/// Очистка журнала сюда не идёт: она зовёт службу студии напрямую и потому
/// работает и без построенной панели, и вообще без дока. Через хаб идёт
/// только то, что умеет одна панель.
/// </para>
/// </remarks>
public static class ConsoleHub
{
    private static readonly Lock Gate = new();

    private static Action? _panel;
    private static bool _waiting;

    /// <summary>Просит панель показаться; та ответит, когда сможет.</summary>
    public static void Show()
    {
        Action? panel;

        lock (Gate)
        {
            panel = _panel;

            if (panel is null)
                _waiting = true;
        }

        panel?.Invoke();
    }

    /// <summary>Панель встала; накопленная просьба отдаётся сразу.</summary>
    /// <param name="panel">Что сделать по просьбе.</param>
    public static void Attach(Action panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        bool waiting;

        lock (Gate)
        {
            _panel = panel;
            waiting = _waiting;
            _waiting = false;
        }

        // Сколько бы просьб ни накопилось, показаться надо один раз: панель
        // либо на виду, либо нет, и повторный показ ничего не добавляет.
        if (waiting)
            panel();
    }

    /// <summary>Панель ушла: просьбы к ней снова копятся.</summary>
    public static void Detach()
    {
        lock (Gate)
        {
            _panel = null;
        }
    }

    /// <summary>Забывает и панель, и очередь — для тестов, которые делят один процесс.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _panel = null;
            _waiting = false;
        }
    }
}
