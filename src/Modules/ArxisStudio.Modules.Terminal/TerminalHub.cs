using ArxisStudio.Modules.Terminal.Shells;

namespace ArxisStudio.Modules.Terminal;

/// <summary>Что просят у панели.</summary>
public enum TerminalRequestKind
{
    /// <summary>Показать терминал: открыть сеанс, если ни одного нет, иначе оставить как есть.</summary>
    Open,

    /// <summary>Открыть ещё один сеанс указанной оболочки.</summary>
    NewSession,

    /// <summary>Спросить адрес и открыть сеанс SSH.</summary>
    NewSsh,

    /// <summary>Показать диалог настроек.</summary>
    Settings,
}

/// <summary>Просьба к панели.</summary>
/// <param name="Kind">Что сделать.</param>
/// <param name="Profile">Какую оболочку открыть; нужна только <see cref="TerminalRequestKind.NewSession"/>.</param>
public sealed record TerminalRequest(TerminalRequestKind Kind, ShellProfile? Profile = null);

/// <summary>
/// Место встречи команд и панели: команда просит, панель делает.
/// </summary>
/// <remarks>
/// Панель создаёт студия, когда ставит её в раскладку, а команды заявляет
/// точка входа модуля при подъёме — друг о друге они не знают. Диалоги тоже
/// живут на стороне панели: у неё есть окно, которому они принадлежат, а у
/// команды — нет. Просьба, пришедшая раньше панели, не теряется: панель
/// заберёт её, как только построится.
/// </remarks>
public static class TerminalHub
{
    private static readonly Lock Gate = new();
    private static readonly List<TerminalRequest> Waiting = [];
    private static Action<TerminalRequest>? _panel;

    /// <summary>Просит панель; та ответит, когда сможет.</summary>
    /// <param name="request">О чём просят.</param>
    public static void Open(TerminalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Action<TerminalRequest>? panel;

        lock (Gate)
        {
            panel = _panel;

            if (panel is null)
                Waiting.Add(request);
        }

        panel?.Invoke(request);
    }

    /// <summary>Панель встала и готова; накопленные просьбы отдаются сразу.</summary>
    /// <param name="panel">Кто отвечает на просьбы.</param>
    public static void Attach(Action<TerminalRequest> panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        TerminalRequest[] waiting;

        lock (Gate)
        {
            _panel = panel;
            waiting = [.. Waiting];
            Waiting.Clear();
        }

        foreach (var request in waiting)
            panel(request);
    }

    /// <summary>Панель ушла: просьбы снова копятся.</summary>
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
            Waiting.Clear();
        }
    }
}
