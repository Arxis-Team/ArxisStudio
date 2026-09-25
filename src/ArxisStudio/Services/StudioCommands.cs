using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;

namespace ArxisStudio.Services;

/// <summary>
/// Команды студии: то, что плагины заявляют и вызывают друг у друга.
/// </summary>
/// <remarks>
/// Обработчик команды — чужой код, и зовётся он отсюда: и когда человек выбрал
/// пункт меню, и когда одна команда вызывает другую. Поэтому вызов идёт через
/// шов, а хозяин команды запоминается при заявке — по стеку упавшего
/// обработчика плагина уже не назвать.
/// </remarks>
/// <param name="guard">Шов вызовов плагинов; null — звать напрямую.</param>
public sealed class StudioCommands(PluginGuard? guard = null) : IStudioCommands
{
    private readonly Dictionary<string, Handler> _handlers = new(StringComparer.Ordinal);

    /// <summary>Идентификаторы заявленных команд.</summary>
    public IReadOnlyCollection<string> Registered => _handlers.Keys;

    /// <summary>
    /// Будильник: зовётся, когда у команды не нашлось обработчика.
    /// </summary>
    /// <remarks>
    /// Хозяин команды может ещё спать — ждать своего <c>onCommand:</c>.
    /// Реестр о хосте плагинов не знает и знать не должен (ссылка сюда пришла
    /// бы кольцом), поэтому пробуждение выставляет окно студии. Без
    /// будильника поведение прежнее: не нашлось — false.
    /// </remarks>
    public Action<string>? Awaken { get; set; }

    /// <summary>
    /// Заявке отказано: команда занята другим хозяином. В поле — что сказать в журнал.
    /// </summary>
    public event EventHandler<string>? Conflict;

    /// <inheritdoc/>
    public void Register(string id, Action handler) => Register(id, handler, owner: null);

    /// <summary>
    /// Заявляет команду от имени плагина.
    /// </summary>
    /// <param name="id">Идентификатор команды.</param>
    /// <param name="handler">Что делать по вызову.</param>
    /// <param name="owner">Чей это обработчик; null — самой студии.</param>
    /// <returns><c>true</c>, если обработчик заявлен.</returns>
    /// <remarks>
    /// Занятую команду другому хозяину не отдают — тем же правилом, что у экспортов. Пока заявка
    /// перезаписывала молча, плагин мог занять команду соседа или самой студии — пункт меню и
    /// сочетание исполняли бы его код, — а его выгрузка снимала команду целиком, по владельцу, и до
    /// перезапуска та не находила обработчика вовсе.
    /// <para>
    /// Своё перезаявить можно: это обновление, а не спор. Студия старше принесённого: её заявка
    /// вытесняет чужую, сколько бы та ни простояла, — иначе плагин, поднявшийся раньше окна,
    /// отнимал бы у студии её же команду.
    /// </para>
    /// </remarks>
    public bool Register(string id, Action handler, string? owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(handler);

        if (_handlers.TryGetValue(id, out var taken) && !string.Equals(taken.Owner, owner, StringComparison.Ordinal))
        {
            if (owner is not null)
            {
                Conflict?.Invoke(this,
                    $"{owner}: команда {id} уже заявлена {(taken.Owner is null ? "студией" : $"плагином {taken.Owner}")} — заявка отклонена");

                return false;
            }

            Conflict?.Invoke(this, $"Команда {id} принадлежит студии — заявка плагина {taken.Owner} снята");
        }

        _handlers[id] = new Handler(handler, owner);

        return true;
    }

    /// <inheritdoc/>
    public bool Invoke(string id)
    {
        if (!_handlers.TryGetValue(id, out var handler))
        {
            // Будим и спрашиваем ещё раз: хозяин, ждавший этой команды,
            // зарегистрирует обработчик при подъёме. Рекурсия конечна —
            // хост убирает плагин из ждущих до подъёма, и второй звонок по
            // той же команде никого не найдёт.
            Awaken?.Invoke(id);

            if (!_handlers.TryGetValue(id, out handler))
                return false;
        }

        if (guard is null || handler.Owner is not { } owner)
        {
            handler.Run();
            return true;
        }

        return guard.Run(owner, $"команда {id}", handler.Run);
    }

    /// <summary>
    /// Убирает все команды одного владельца.
    /// </summary>
    /// <param name="pluginId">Чьи обработчики снять.</param>
    /// <remarks>
    /// По владельцу, а не по манифесту: манифест при перезагрузке уже
    /// свежий, и команда, убранная новой версией, осталась бы висеть с
    /// обработчиком из выгруженного контекста. Владельца реестр помнит с
    /// рождения записи — он и есть правда о том, чьё это.
    /// </remarks>
    public void RemoveOwnedBy(string pluginId)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);

        foreach (var id in _handlers
                     .Where(pair => string.Equals(pair.Value.Owner, pluginId, StringComparison.Ordinal))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _handlers.Remove(id);
        }
    }

    private readonly record struct Handler(Action Run, string? Owner);
}

/// <summary>
/// Команды глазами одного плагина.
/// </summary>
/// <remarks>
/// Реестр один на студию, а имя заявителя у каждого своё: контракт SDK о
/// хозяине команды не говорит, и подставить его может только тот, кто выдаёт
/// плагину контекст.
/// </remarks>
/// <param name="commands">Общий реестр команд.</param>
/// <param name="pluginId">Чьи заявки идут через эту обёртку.</param>
public sealed class PluginCommands(StudioCommands commands, string pluginId) : IStudioCommands
{
    /// <inheritdoc/>
    public void Register(string id, Action handler) => commands.Register(id, handler, pluginId);

    /// <inheritdoc/>
    public bool Invoke(string id) => commands.Invoke(id);
}
