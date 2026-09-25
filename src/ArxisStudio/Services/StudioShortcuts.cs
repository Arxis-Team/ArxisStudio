using Avalonia.Controls;
using System.Globalization;
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
/// Исключение — короткий список команд, которые проходят мимо того, кто держит клавиатуру
/// (<see cref="Pass"/>): переход между панелями и палитра. Их студия слышит на спуске, раньше
/// всех. Терминал обрабатывает всякую клавишу, и F6 с палитрой в нём глохли: из терминала
/// уходили только мышью или Shift+Esc. Так устроен и терминал VS Code — короткий список команд
/// проходит мимо оболочки (<c>commandsToSkipShell</c>), и F6 в нём тот же.
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
/// <para>
/// Реестр помнит, кто и в каком порядке просил сочетания, и новый файл человека
/// раздаётся не поверх прежнего, а заново: сперва человеку, потом всем просившим
/// в том же порядке, в каком они просили.
/// </para>
/// </remarks>
/// <param name="invoke">Кому передать имя команды; <c>false</c> — такой команды нет.</param>
public sealed class StudioShortcuts(Func<string, bool> invoke)
{
    private readonly List<ShortcutBinding> _bindings = [];
    private readonly List<ShortcutConflict> _refused = [];
    private readonly HashSet<string> _personal = new(StringComparer.Ordinal);
    private readonly HashSet<string> _passing = new(StringComparer.Ordinal);
    private readonly List<Request> _asked = [];

    /// <summary>Все отданные сочетания, в порядке выдачи.</summary>
    public IReadOnlyList<ShortcutBinding> All => _bindings;

    /// <summary>Кому сочетания не досталось и почему.</summary>
    public IReadOnlyList<ShortcutConflict> Refused => _refused;

    /// <summary>
    /// Раздача переменилась: расширение попросило сочетание или ушло, человек сохранил файл.
    /// </summary>
    /// <remarks>
    /// Слушает страница «Клавиши», пока открыто её окно: человек возвращается к ней из редактора
    /// файла или со страницы плагинов того же окна и должен видеть то, что стало.
    /// </remarks>
    public event EventHandler? Changed;

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

        // Просьба запоминается до ответа на неё: команда, за которую решил человек,
        // получит своё по умолчанию, когда человек уберёт её из файла.
        _asked.Add(new Request(gesture, commandId, owner));

        var given = Deal(gesture, commandId, owner);

        Changed?.Invoke(this, EventArgs.Empty);

        return given;
    }

    /// <summary>
    /// Раздаёт сочетания, назначенные человеком, — раньше всех остальных.
    /// </summary>
    /// <param name="keymap">Прочитанный <c>keymap.json</c>.</param>
    /// <returns>Отказы, которых до этой раздачи не было.</returns>
    /// <remarks>
    /// Файл раздаётся не поверх прежнего, а заново: прежние сочетания человека
    /// снимаются, его новые отдаются первыми, а за ними — просьбы студии и плагинов
    /// в том порядке, в каком они приходили. Раздай его вслед за остальными, человек
    /// получил бы отказ в своём же файле, а кому достаётся занятое, решал бы порядок
    /// подъёма. Порядок внутри файла — порядок записей: два одинаковых сочетания у
    /// двух команд достаются первой, а второй — отказ, как у всех.
    /// <para>
    /// Файл, который не прочитался или не разобрался целиком, не меняет ничего:
    /// остаются сочетания, которые были, — при запуске это сочетания по умолчанию.
    /// Полупустой файл, сохранённый посреди правки, не должен снимать всё, что
    /// человек назначил раньше.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ShortcutConflict> Personalize(StudioKeymap keymap)
    {
        ArgumentNullException.ThrowIfNull(keymap);

        if (keymap.Broken)
            return [];

        var before = _refused.ToList();

        _bindings.Clear();
        _refused.Clear();
        _personal.Clear();

        foreach (var entry in keymap.Entries)
        {
            _personal.Add(entry.CommandId);

            foreach (var gesture in entry.Gestures)
            {
                if (TryParse(gesture, out var parsed))
                    Give(parsed, entry.CommandId, owner: null, personal: true);
            }
        }

        foreach (var asked in _asked)
            Deal(asked.Gesture, asked.CommandId, asked.Owner);

        Changed?.Invoke(this, EventArgs.Empty);

        return [.. _refused.Where(refusal => !before.Contains(refusal))];
    }

    private bool Deal(string gesture, string commandId, string? owner)
    {
        if (_personal.Contains(commandId))
            return true;

        if (!TryParse(gesture, out var parsed))
            return false;

        return Give(parsed, commandId, owner, personal: false);
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
    /// этого вопроса больше нет. Уходят и его просьбы: следующий файл человека
    /// раздаётся тем, кто есть, а вернувшийся плагин попросит своё заново.
    /// </para>
    /// </remarks>
    public void RemoveOwnedBy(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        _bindings.RemoveAll(bound => string.Equals(bound.Owner, owner, StringComparison.Ordinal));
        _refused.RemoveAll(refusal => string.Equals(refusal.Owner, owner, StringComparison.Ordinal));
        _asked.RemoveAll(request => string.Equals(request.Owner, owner, StringComparison.Ordinal));

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Каким сочетанием зовут эту команду; <c>null</c> — никаким.</summary>
    /// <param name="commandId">Имя команды.</param>
    /// <remarks>
    /// Нужно подписи в палитре: там сочетание пишут рядом с названием. Пишется оно так, как пишет
    /// его платформа: на macOS у той же команды знаки модификаторов, а клавиши с именем Ctrl под
    /// пальцем нет вовсе.
    /// </remarks>
    public string? Gesture(string commandId) => Written(Bound(commandId));

    /// <summary>
    /// Сочетание словами платформы; <c>null</c> — нечего писать.
    /// </summary>
    /// <param name="gesture">Сочетание.</param>
    /// <remarks>
    /// Одно место на всю студию: <c>ToString()</c> у жеста отдаёт инвариантную запись, и
    /// разошедшиеся экраны — палитра, клавиши, меню — писали бы одно и то же по-разному.
    /// </remarks>
    public static string? Written(KeyGesture? gesture) =>
        gesture?.ToString("p", CultureInfo.CurrentCulture);

    /// <summary>
    /// Сочетание этой команды — жестом, а не строкой; <c>null</c> — никаким.
    /// </summary>
    /// <param name="commandId">Имя команды.</param>
    /// <remarks>
    /// Меню и подсказка полосы берут жест отсюда: писать его словами — дело показа, и пишется он
    /// по-разному на разных платформах. Строка, собранная здесь, отняла бы у них этот выбор.
    /// </remarks>
    public KeyGesture? Bound(string commandId) =>
        _bindings.FirstOrDefault(bound => string.Equals(bound.CommandId, commandId, StringComparison.Ordinal))
            ?.Gesture;

    /// <summary>
    /// Велит команде проходить мимо того, кто держит клавиатуру: её сочетание студия слышит раньше
    /// всех, на спуске события.
    /// </summary>
    /// <param name="commandId">Команда студии.</param>
    /// <remarks>
    /// Проходит команда, а не сочетание: человек, переназначивший её в <c>keymap.json</c>, получает
    /// новое сочетание таким же проходящим. Список короткий и только студийный — отнятая у терминала
    /// клавиша — это клавиша, которой не получит программа в нём.
    /// </remarks>
    public void Pass(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        _passing.Add(commandId);
    }

    /// <summary>Начинает слушать клавиши этого окна.</summary>
    /// <param name="window">Окно студии.</param>
    public void Attach(TopLevel window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.AddHandler(InputElement.KeyDownEvent, OnPassing, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Bubble);
    }

    /// <summary>
    /// Слушает клавиши и в оторванных окнах раскладки — нынешних и тех, что заведут потом.
    /// </summary>
    /// <param name="dock">Раскладка студии.</param>
    /// <remarks>
    /// Оторванное окно — то же дерево в другом окне, и сочетания в нём обязаны работать те же. Но
    /// клавиша, нажатая там, до главного окна не доходит — путь события у каждого окна свой, — и F6
    /// уводил в оторванное окно, а обратно не выпускал: ни F6, ни Ctrl+W, ни палитра там не
    /// работали. Окна, поднятые раскладкой ещё до этого вызова, получают клавиши здесь, новые — в
    /// миг, когда раскладка их заводит.
    /// </remarks>
    public void Follow(StudioDock dock)
    {
        ArgumentNullException.ThrowIfNull(dock);

        foreach (var window in dock.Floating)
            Attach(window);

        dock.Floated += (_, window) => Attach(window);
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

    /// <summary>Сочетание проходящей команды — раньше того, кто держит клавиатуру.</summary>
    private void OnPassing(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _passing.Count == 0)
            return;

        foreach (var bound in _bindings)
        {
            if (!bound.Gesture.Matches(e))
                continue;

            if (_passing.Contains(bound.CommandId))
                e.Handled = invoke(bound.CommandId);

            return;
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

    /// <summary>Просьба о сочетании, как её передали.</summary>
    private readonly record struct Request(string Gesture, string CommandId, string? Owner);
}
