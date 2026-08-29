namespace ArxisStudio.Shell.Localization;

/// <summary>
/// Следящие строки одного источника: по одной на ключ, и все они его.
/// </summary>
/// <remarks>
/// Заведена ради времени жизни. Привязка Avalonia держит свой источник слабо —
/// это её обдуманное решение, чтобы показанный контрол не удерживал модель, —
/// поэтому строку, показанную на экране, не держит никто, кроме того, кто её
/// завёл. Пока таким держателем был список слабых ссылок, строки умирали на
/// первой же сборке мусора, и смена языка переставала обновлять уже
/// показанный текст.
/// <para>
/// Отсюда правило: слабая ссылка допустима на то, у чего есть хозяин, и не
/// допустима вместо хозяина. Хозяин строк — их источник: у студии он один и
/// живёт с ней, у плагина свой и уходит вместе с ним.
/// </para>
/// <para>
/// Одна строка на ключ, а не на привязку: показанный дважды заголовок — это
/// один и тот же текст, и заводить под него два объекта незачем. Память
/// ограничена словарём, а не числом контролов, которые успели построить.
/// </para>
/// </remarks>
/// <param name="source">Откуда строки берут текст.</param>
public sealed class TrackedStrings(IStringSource source)
{
    private readonly Dictionary<string, LocalizedString> _byKey = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <summary>Строка по ключу — та же самая при повторном обращении.</summary>
    /// <param name="key">Ключ строки.</param>
    public LocalizedString this[string key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);

            lock (_lock)
            {
                if (!_byKey.TryGetValue(key, out var tracked))
                    _byKey[key] = tracked = new LocalizedString(source, key);

                return tracked;
            }
        }
    }

    /// <summary>
    /// Говорит всем строкам перечитать текст.
    /// </summary>
    /// <remarks>
    /// Зовётся из потока, в котором меняют язык, — то есть из потока
    /// интерфейса: привязки ждут уведомления именно оттуда.
    /// </remarks>
    public void Refresh()
    {
        LocalizedString[] all;

        lock (_lock)
            all = [.. _byKey.Values];

        foreach (var tracked in all)
            tracked.Refresh();
    }
}
