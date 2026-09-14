using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ArxisStudio.Services;

/// <summary>Сочетание, отданное команде.</summary>
/// <param name="Gesture">Что нажимают.</param>
/// <param name="CommandId">Кого зовут.</param>
/// <param name="Owner">Чьё это; <c>null</c> — самой студии.</param>
/// <param name="Personal">Назначил человек в <c>keymap.json</c>, а не студия и не манифест.</param>
public sealed record ShortcutBinding(KeyGesture Gesture, string CommandId, string? Owner = null, bool Personal = false);

/// <summary>Отказ: сочетание уже занято.</summary>
/// <param name="Gesture">Что просили.</param>
/// <param name="CommandId">Кто просил.</param>
/// <param name="Winner">Кому оно досталось раньше.</param>
/// <param name="Owner">Чья это была просьба; <c>null</c> — самой студии.</param>
/// <param name="Personal">Просил человек в <c>keymap.json</c>.</param>
public sealed record ShortcutConflict(KeyGesture Gesture, string CommandId, string Winner, string? Owner = null, bool Personal = false);

/// <summary>
/// Сочетания клавиш студии: что нажали — кого позвать.
/// </summary>
/// <remarks>
/// Своего исполнения у реестра нет: он переводит нажатие в имя команды и зовёт
/// реестр команд. Так сочетание и пункт меню делают ровно одно и то же, и
/// разойтись они не могут — звать им нечего, кроме одного идентификатора.
/// <para>
/// Обработчик всплывающий, а не туннельный, и это решение. Туннельный видел бы
/// клавишу раньше всех и отнимал бы её у того, кому она принадлежит: в
/// терминале <c>Ctrl+W</c> стирает слово, и студия не вправе закрывать вкладку
/// вместо этого. Всплывающий отдаёт клавишу тому, кто её обработал, и получает
/// только невостребованное.
/// </para>
/// <para>
/// Занятое сочетание второму не отдаётся, и отказ не молчит: <see cref="Refused"/>
/// помнит всех, кому не досталось, вместе с именем победителя. Команда при этом
/// остаётся доступна из меню — потерять сочетание не значит потерять команду.
/// </para>
/// <para>
/// Человек старше всех. Его сочетания из <c>keymap.json</c> раздаются раньше
/// студийных и манифестных, поэтому занятое им достаётся ему, а студия и плагины
/// получают отказ с его командой в победителях. Команда, которой человек назначил
/// сочетание сам, своего по умолчанию не получает вовсе.
/// </para>
/// </remarks>
/// <param name="invoke">Кому передать имя команды; <c>false</c> — такой команды нет.</param>
public sealed class StudioShortcuts(Func<string, bool> invoke)
{
    private readonly List<ShortcutBinding> _bindings = [];
    private readonly List<ShortcutConflict> _refused = [];
    private readonly HashSet<string> _personal = new(StringComparer.Ordinal);

    /// <summary>Все отданные сочетания, в порядке выдачи.</summary>
    public IReadOnlyList<ShortcutBinding> All => _bindings;

    /// <summary>Кому сочетания не досталось и почему.</summary>
    public IReadOnlyList<ShortcutConflict> Refused => _refused;

    /// <summary>
    /// Просит сочетание для команды.
    /// </summary>
    /// <param name="gesture">Как в манифесте: <c>Ctrl+W</c>, <c>Shift+F6</c>.</param>
    /// <param name="commandId">Кого звать.</param>
    /// <param name="owner">Чьё это сочетание; <c>null</c> — самой студии.</param>
    /// <returns>Досталось ли.</returns>
    /// <remarks>
    /// Неразобранная строка — отказ без записи в конфликтах: там помнят тех, у
    /// кого сочетание отняли, а разобрать «Ctrl+Шифт» не смог бы никто.
    /// <para>
    /// Команда, за которую решил человек, получает <c>true</c> и ничего сверх
    /// его решения: отказа не было — вопрос о её сочетании закрыт раньше.
    /// </para>
    /// </remarks>
    public bool Bind(string gesture, string commandId, string? owner = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gesture);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        if (_personal.Contains(commandId))
            return true;

        if (!TryParse(gesture, out var parsed))
            return false;

        return Give(parsed, commandId, owner, personal: false);
    }

    /// <summary>
    /// Раздаёт сочетания, назначенные человеком.
    /// </summary>
    /// <param name="keymap">Прочитанный <c>keymap.json</c>.</param>
    /// <exception cref="InvalidOperationException">Сочетания уже раздавались.</exception>
    /// <remarks>
    /// Раньше всех и один раз. Отдай студия или плагин сочетание первыми, человек
    /// получил бы отказ в своём же файле, а пересчёт задним числом отнимал бы
    /// клавишу у того, кто уже её держит, — и зависел бы от порядка подъёма.
    /// Порядок внутри файла — порядок записей: два одинаковых сочетания у двух
    /// команд достаются первой, а второй — отказ, как у всех.
    /// </remarks>
    public void Personalize(StudioKeymap keymap)
    {
        ArgumentNullException.ThrowIfNull(keymap);

        if (_bindings.Count > 0 || _refused.Count > 0 || _personal.Count > 0)
            throw new InvalidOperationException("Сочетания человека раздаются раньше всех и один раз.");

        foreach (var entry in keymap.Entries)
        {
            _personal.Add(entry.CommandId);

            foreach (var gesture in entry.Gestures)
            {
                if (TryParse(gesture, out var parsed))
                    Give(parsed, entry.CommandId, owner: null, personal: true);
            }
        }
    }

    private bool Give(KeyGesture gesture, string commandId, string? owner, bool personal)
    {
        if (_bindings.FirstOrDefault(bound => bound.Gesture.Equals(gesture)) is { } taken)
        {
            _refused.Add(new ShortcutConflict(gesture, commandId, taken.CommandId, owner, personal));

            return false;
        }

        _bindings.Add(new ShortcutBinding(gesture, commandId, owner, personal));

        return true;
    }

    /// <summary>
    /// Отпускает сочетания одного расширения.
    /// </summary>
    /// <param name="owner">Чьи сочетания снять.</param>
    /// <remarks>
    /// Сочетание живёт ровно столько, сколько живёт команда за ним. Выключенный
    /// плагин, чьё сочетание осталось, отнимал бы клавишу у всех и не делал бы
    /// ничего; перезагруженный просил бы своё же сочетание второй раз и получал
    /// отказ с сообщением, что оно занято им самим.
    /// <para>
    /// Отказы того же хозяина уходят вместе с привязками: список проигравших —
    /// ответ на вопрос «почему моё сочетание не работает», а у снятого плагина
    /// этого вопроса больше нет.
    /// </para>
    /// </remarks>
    public void RemoveOwnedBy(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        _bindings.RemoveAll(bound => string.Equals(bound.Owner, owner, StringComparison.Ordinal));
        _refused.RemoveAll(refusal => string.Equals(refusal.Owner, owner, StringComparison.Ordinal));
    }

    /// <summary>Каким сочетанием зовут эту команду; <c>null</c> — никаким.</summary>
    /// <param name="commandId">Имя команды.</param>
    /// <remarks>Нужно подписи в меню и в палитре: там сочетание пишут рядом с названием.</remarks>
    public string? Gesture(string commandId) =>
        _bindings.FirstOrDefault(bound => string.Equals(bound.CommandId, commandId, StringComparison.Ordinal))
            ?.Gesture.ToString();

    /// <summary>Начинает слушать клавиши этого окна.</summary>
    /// <param name="window">Окно студии.</param>
    public void Attach(TopLevel window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Bubble);
    }

    /// <summary>
    /// Разбирает сочетание, не бросая на неразобранном.
    /// </summary>
    /// <remarks>
    /// Строка приходит из манифеста, то есть от человека, и опечатка в ней не
    /// повод не поднять плагин. Об опечатке скажет проверка манифеста.
    /// </remarks>
    private static bool TryParse(string gesture, out KeyGesture parsed)
    {
        try
        {
            parsed = KeyGesture.Parse(gesture);

            return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            parsed = null!;

            return false;
        }
    }

    /// <summary>
    /// Нажатие, до которого никому не было дела.
    /// </summary>
    /// <remarks>
    /// Клавиша помечается обработанной только если команда нашлась и сходила.
    /// Сочетание, отданное команде, которой нет, клавишу не съедает: иначе
    /// плагин, объявивший жест и не поднявшийся, отнимал бы нажатие у всех.
    /// </remarks>
    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
            return;

        foreach (var bound in _bindings)
        {
            if (!bound.Gesture.Matches(e))
                continue;

            e.Handled = invoke(bound.CommandId);

            return;
        }
    }
}
