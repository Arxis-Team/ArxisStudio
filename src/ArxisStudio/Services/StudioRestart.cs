using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;

namespace ArxisStudio.Services;

/// <summary>
/// Перезапуск студии глазами интерфейса: нужен ли он, когда о нём спросить и как его начать.
/// </summary>
/// <remarks>
/// Кому нужен перезапуск, знает служба расширений (<see cref="StudioPlugins.AwaitingRestart"/>);
/// как перезапуститься — приложение (<see cref="Perform"/>): у него окна, процесс и сессия. Здесь —
/// то, что между ними: вопрос человеку и порядок шагов. Одна служба на студию, её держит главное
/// окно, а Welcome получает её от приложения — вопрос задаётся одинаково из обоих.
/// <para>
/// Спрашивается только о новом. Человек, ответивший «Не сейчас», не хочет слышать тот же вопрос
/// на каждом «Сохранить»: студия продолжает говорить о перезапуске строкой состояния и в
/// менеджере, а снова спросит, когда появится новый повод.
/// </para>
/// </remarks>
public sealed class StudioRestart
{
    private readonly StudioPlugins _plugins;

    // Появился повод, о котором человека ещё не спрашивали.
    private bool _unanswered;

    // Идёт вопрос или сам перезапуск: второй вход ждать не станет, а начал бы всё заново.
    private bool _busy;

    /// <summary>Заводит службу над расширениями студии.</summary>
    /// <param name="plugins">Служба расширений: она знает, кому нужен перезапуск.</param>
    public StudioRestart(StudioPlugins plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        _plugins = plugins;
        _plugins.RestartRequired += (_, _) =>
        {
            _unanswered = true;
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Перезапуск стал нужен — или нужен по новой причине.</summary>
    public event EventHandler? Changed;

    /// <summary>Перезапуск нужен: чьи-то изменения применит только он.</summary>
    public bool IsRequired => _plugins.AwaitingRestart.Count > 0;

    /// <summary>
    /// Вопрос человеку; <c>true</c> — перезапустить.
    /// </summary>
    /// <remarks>Шов ради тестов: модальный вопрос в безголовом прогоне ждал бы ответа вечно.</remarks>
    public Func<Window, Task<bool>> Ask { get; set; } = AskAsync;

    /// <summary>
    /// Сам перезапуск; <c>true</c> — студия закрывается, <c>false</c> — не вышло, и она работает дальше.
    /// </summary>
    /// <remarks>
    /// Ставит приложение: процесс, окна и сессия — его. Не поставлен — перезапускать некому, и о
    /// нём не спрашивают: вопрос, на который «да» ничего не делает, хуже молчания.
    /// </remarks>
    public Func<Task<bool>>? Perform { get; set; }

    /// <summary>
    /// Спрашивает о перезапуске, если с прошлого вопроса появился новый повод.
    /// </summary>
    /// <param name="owner">Окно, которому принадлежит вопрос; обязано быть открытым.</param>
    /// <param name="prepare">
    /// Что дописать до перезапуска — несохранённое в окне настроек; <c>false</c> — отбой.
    /// </param>
    /// <param name="fallback">
    /// Что сделать, если <paramref name="prepare"/> отработал, а перезапуска не будет.
    /// </param>
    public async Task OfferAsync(Window owner, Func<Task<bool>>? prepare = null, Func<Task>? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (!_unanswered || _busy || Perform is null)
            return;

        _unanswered = false;
        _busy = true;

        bool agreed;

        try
        {
            agreed = await Ask(owner);
        }
        finally
        {
            _busy = false;
        }

        if (agreed)
            await RestartAsync(prepare, fallback);
    }

    /// <summary>
    /// Перезапускает студию без вопроса: человек уже попросил об этом сам.
    /// </summary>
    /// <param name="prepare">
    /// Что дописать до перезапуска; <c>false</c> — отбой, и студия не трогается.
    /// </param>
    /// <param name="fallback">
    /// Что сделать, если <paramref name="prepare"/> отработал, а перезапуска не будет: отказ записи
    /// на середине или новая копия, которая не поднялась. Окно настроек применяет здесь вживую то,
    /// что подготовка только записала, — иначе студия жила бы по-старому, а диск помнил по-новому.
    /// </param>
    /// <returns><c>true</c> — студия закрывается ради новой копии.</returns>
    /// <remarks>
    /// Идущему перезапуску второй вход отказывает сразу — и ни подготовки, ни запасного шага не
    /// зовёт: они принадлежат первому, и применять вживую то, что первый вот-вот унесёт в новую
    /// копию, было бы работой поперёк него.
    /// </remarks>
    public async Task<bool> RestartAsync(Func<Task<bool>>? prepare = null, Func<Task>? fallback = null)
    {
        if (_busy || Perform is not { } perform)
            return false;

        _busy = true;

        var restarting = false;

        try
        {
            if (prepare is null || await prepare())
                restarting = await perform();

            return restarting;
        }
        finally
        {
            try
            {
                if (!restarting && prepare is not null && fallback is not null)
                    await fallback();
            }
            finally
            {
                _busy = false;
            }
        }
    }

    /// <summary>Вопрос, как у Rider: применить изменения в плагинах перезапуском.</summary>
    /// <remarks>
    /// Согласие не опасное: несохранённое в студии перезапуск дописывает, а открытое — возвращает.
    /// Поэтому каретка стоит на нём и Enter соглашается.
    /// </remarks>
    private static Task<bool> AskAsync(Window owner) =>
        StudioAsk.ConfirmAsync(
            owner,
            Localizer.Instance["restart.title"],
            Localizer.Instance["restart.message"],
            Localizer.Instance["restart.now"],
            refuse: Localizer.Instance["restart.later"],
            question: true);
}
