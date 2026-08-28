namespace ArxisStudio.Extensibility;

/// <summary>
/// Шов между студией и плагином: всё, что студия зовёт у плагина, проходит
/// здесь.
/// </summary>
/// <remarks>
/// Вызовов таких много и они разбросаны — построить панель, выполнить команду,
/// нарисовать строку свойства, — и в каждом месте вокруг чужого кода стоял бы
/// свой <c>try</c>. Своих <c>try</c> было уже два, и оба ловили молча: в
/// журнале оставалось «упало», без имени плагина, а список включённых плагинов
/// об этом не знал вовсе.
/// <para>
/// Шов делает три вещи, которых по месту не сделать. Приписывает падение
/// плагину — по имени, а не по стеку. Считает падения: один сбой бывает у
/// всякого, а третий подряд означает, что плагин сломан, и держать его дальше
/// значит показывать человеку одно и то же уведомление до конца сеанса.
/// И, посчитав, перестаёт его звать — это и есть отключение, о котором говорит
/// план: студия работает, плагин молчит.
/// </para>
/// <para>
/// Отключение живёт до конца сеанса и в настройки не пишется: человек включал
/// этот плагин сам, и стереть его выбор за него — не то же самое, что не звать
/// сломанное сейчас. При следующем запуске плагин получит новую попытку.
/// </para>
/// </remarks>
public sealed class PluginGuard
{
    /// <summary>Сколько падений подряд плагин переживает, оставаясь в строю.</summary>
    public const int FailureLimit = 3;

    private readonly Dictionary<string, int> _failures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _faulty = new(StringComparer.Ordinal);

    /// <summary>Плагин упал на вызове студии.</summary>
    public event EventHandler<PluginFailure>? Failed;

    /// <summary>Плагин признан неисправным и больше не зовётся.</summary>
    public event EventHandler<PluginFailure>? Disabled;

    /// <summary>Плагины, которых студия больше не зовёт.</summary>
    public IReadOnlyCollection<string> Faulty => _faulty;

    /// <summary>Признан ли плагин неисправным.</summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    public bool IsFaulty(string pluginId) => _faulty.Contains(pluginId);

    /// <summary>
    /// Зовёт код плагина, ничего не ожидая в ответ.
    /// </summary>
    /// <param name="pluginId">Чей это код.</param>
    /// <param name="what">Что студия просила сделать — попадёт в журнал.</param>
    /// <param name="call">Сам вызов.</param>
    /// <returns><c>true</c>, если вызов прошёл.</returns>
    public bool Run(string pluginId, string what, Action call)
    {
        ArgumentNullException.ThrowIfNull(call);

        return Get<object>(pluginId, what, () =>
        {
            call();
            return null;
        }, out _);
    }

    /// <summary>
    /// Зовёт код плагина за результатом.
    /// </summary>
    /// <typeparam name="T">Что плагин должен вернуть.</typeparam>
    /// <param name="pluginId">Чей это код.</param>
    /// <param name="what">Что студия просила сделать.</param>
    /// <param name="call">Сам вызов.</param>
    /// <returns>Что вернул плагин; <c>null</c>, если он упал или отключён.</returns>
    public T? Get<T>(string pluginId, string what, Func<T?> call) where T : class
    {
        Get(pluginId, what, call, out var result);

        return result;
    }

    /// <summary>
    /// Зовёт код плагина за результатом, отделяя отказ от пустого ответа.
    /// </summary>
    /// <typeparam name="T">Что плагин должен вернуть.</typeparam>
    /// <param name="pluginId">Чей это код.</param>
    /// <param name="what">Что студия просила сделать.</param>
    /// <param name="call">Сам вызов.</param>
    /// <param name="result">Что вернул плагин.</param>
    /// <returns><c>true</c>, если вызов прошёл.</returns>
    /// <remarks>
    /// Плагин вправе вернуть <c>null</c> и не упав — рисовальщик, который за
    /// эту строку не берётся, отвечает именно так. Там, где это различие
    /// важно, ответ приходит отдельно от признака.
    /// </remarks>
    public bool Get<T>(string pluginId, string what, Func<T?> call, out T? result) where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);
        ArgumentNullException.ThrowIfNull(call);

        result = null;

        if (_faulty.Contains(pluginId))
            return false;

        try
        {
            result = call();

            return true;
        }

        // Нехватку памяти и переполнение стека не перехватываем: это отказ
        // процесса, а не плагина, и продолжать после них студия всё равно не
        // сможет — притвориться, что обошлось, было бы хуже падения.
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            Fail(pluginId, what, e);

            return false;
        }
    }

    /// <summary>
    /// Записывает падение, случившееся не на вызове шва.
    /// </summary>
    /// <param name="pluginId">Чей код упал.</param>
    /// <param name="what">Где это случилось.</param>
    /// <param name="error">Само исключение.</param>
    /// <remarks>
    /// Так приходят сбои раскладки: панель падает не тогда, когда её строили,
    /// а когда Avalonia считает дерево, — и перехватывает их место, куда она
    /// вставлена. Считаться они должны там же, где остальные: плагин,
    /// роняющий проход раскладки при каждом замере, сломан не меньше того,
    /// что падает при построении.
    /// </remarks>
    public void Report(string pluginId, string what, Exception error)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);
        ArgumentNullException.ThrowIfNull(error);

        Fail(pluginId, what, error);
    }

    /// <summary>
    /// Забывает падения плагина.
    /// </summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    /// <remarks>
    /// Нужно перезагрузке: человек нажал «Перезапустить» — значит, счёт идёт
    /// заново, иначе обновлённый плагин остался бы отключённым за грехи
    /// прежнего.
    /// </remarks>
    public void Forget(string pluginId)
    {
        _failures.Remove(pluginId);
        _faulty.Remove(pluginId);
    }

    private void Fail(string pluginId, string what, Exception error)
    {
        var count = _failures.TryGetValue(pluginId, out var seen) ? seen + 1 : 1;

        _failures[pluginId] = count;

        var failure = new PluginFailure(pluginId, what, error, count);

        Failed?.Invoke(this, failure);

        if (count < FailureLimit)
            return;

        _faulty.Add(pluginId);
        Disabled?.Invoke(this, failure);
    }
}

/// <summary>Падение плагина на вызове студии.</summary>
/// <param name="PluginId">Чей код упал.</param>
/// <param name="What">Что студия просила сделать.</param>
/// <param name="Error">Само исключение.</param>
/// <param name="Count">Какое это падение по счёту у этого плагина.</param>
public sealed record PluginFailure(string PluginId, string What, Exception Error, int Count)
{
    /// <summary>Сообщение исключения без обёрток отражения.</summary>
    public string Message => Error is System.Reflection.TargetInvocationException { InnerException: { } inner }
        ? inner.Message
        : Error.Message;
}
