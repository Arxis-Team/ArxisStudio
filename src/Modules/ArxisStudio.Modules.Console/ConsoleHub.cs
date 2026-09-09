namespace ArxisStudio.Modules.Console;

/// <summary>Кого просят показаться.</summary>
public enum ConsolePanelKind
{
    /// <summary>Журнал.</summary>
    Log,

    /// <summary>Находки.</summary>
    Problems,
}

/// <summary>
/// Место встречи команд и панелей: команда просит, панель делает.
/// </summary>
/// <remarks>
/// Панели создаёт студия, когда ставит их в раскладку, а команды заявляет
/// точка входа модуля при подъёме — друг о друге они не знают. Просьба,
/// пришедшая раньше панели, не теряется: панель заберёт её, как только
/// построится.
/// <para>
/// Панелей у консоли две, и поэтому здесь два отдельных поля, а не словарь:
/// просьба к журналу и просьба к находкам — разные адреса, и разбирать
/// адресата ветвлением на каждом вызове дороже, чем написать два поля.
/// </para>
/// <para>
/// Очистка журнала сюда не идёт: она зовёт службу студии напрямую и потому
/// работает и без построенной панели, и вообще без дока. Через хаб идёт
/// только то, что умеет одна панель.
/// </para>
/// </remarks>
public static class ConsoleHub
{
    private static readonly Lock Gate = new();
    private static readonly List<ConsolePanelKind> Waiting = [];

    private static Action? _log;
    private static Action? _problems;

    /// <summary>Просит панель показаться; та ответит, когда сможет.</summary>
    /// <param name="kind">Кого просят.</param>
    public static void Show(ConsolePanelKind kind)
    {
        Action? panel;

        lock (Gate)
        {
            panel = kind == ConsolePanelKind.Log ? _log : _problems;

            if (panel is null)
                Waiting.Add(kind);
        }

        panel?.Invoke();
    }

    /// <summary>Панель журнала встала; накопленные просьбы отдаются сразу.</summary>
    /// <param name="panel">Что сделать по просьбе.</param>
    public static void AttachLog(Action panel) => Attach(ConsolePanelKind.Log, panel);

    /// <summary>Панель находок встала; накопленные просьбы отдаются сразу.</summary>
    /// <param name="panel">Что сделать по просьбе.</param>
    public static void AttachProblems(Action panel) => Attach(ConsolePanelKind.Problems, panel);

    /// <summary>Панель журнала ушла: просьбы к ней снова копятся.</summary>
    public static void DetachLog()
    {
        lock (Gate)
        {
            _log = null;
        }
    }

    /// <summary>Панель находок ушла: просьбы к ней снова копятся.</summary>
    public static void DetachProblems()
    {
        lock (Gate)
        {
            _problems = null;
        }
    }

    /// <summary>Забывает и панели, и очередь — для тестов, которые делят один процесс.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _log = null;
            _problems = null;
            Waiting.Clear();
        }
    }

    private static void Attach(ConsolePanelKind kind, Action panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        int waiting;

        lock (Gate)
        {
            if (kind == ConsolePanelKind.Log)
                _log = panel;
            else
                _problems = panel;

            waiting = Waiting.RemoveAll(pending => pending == kind);
        }

        // Сколько бы просьб ни накопилось, показаться надо один раз: панель
        // либо на виду, либо нет, и повторный показ ничего не добавляет.
        if (waiting > 0)
            panel();
    }
}
