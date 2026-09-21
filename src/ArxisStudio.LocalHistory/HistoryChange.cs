namespace ArxisStudio.LocalHistory;

/// <summary>Что случилось с путём.</summary>
public enum HistoryChangeKind
{
    /// <summary>Путь появился.</summary>
    Created,

    /// <summary>Изменилось содержимое файла.</summary>
    Modified,

    /// <summary>Путь пропал.</summary>
    Deleted,

    /// <summary>Путь переехал или переименован; откуда — <see cref="HistoryChange.From"/>.</summary>
    Moved,
}

/// <summary>Кто сделал действие.</summary>
public enum HistoryOrigin
{
    /// <summary>Студия — по просьбе человека или плагина.</summary>
    Studio,

    /// <summary>Кто-то мимо студии: другой редактор, система контроля версий, сборка.</summary>
    External,
}

/// <summary>
/// Одна правка пути внутри действия.
/// </summary>
/// <remarks>
/// Содержимое названо адресами до и после, а не байтами: байты лежат в хранилище один раз, сколько
/// бы правок на них ни ссылалось. Адреса нет, когда содержимого не было (до появления, после
/// удаления) или когда файл больше предела и история его не хранит — об этом говорит
/// <see cref="TooLarge"/>, чтобы окно истории могло сказать честно, почему вернуть нечего.
/// </remarks>
public sealed record HistoryChange
{
    /// <summary>Что случилось.</summary>
    public required HistoryChangeKind Kind { get; init; }

    /// <summary>Полный путь — нынешний, а у переезда — куда.</summary>
    public required string Path { get; init; }

    /// <summary>Откуда переехал; у остальных правок — null.</summary>
    public string? From { get; init; }

    /// <summary>Содержимое до правки; null — его не было или оно не хранится.</summary>
    public ContentId? Before { get; init; }

    /// <summary>Содержимое после правки; null — его не стало или оно не хранится.</summary>
    public ContentId? After { get; init; }

    /// <summary>Путь — папка.</summary>
    public bool IsDirectory { get; init; }

    /// <summary>Файл больше предела, и его содержимое история не хранит.</summary>
    public bool TooLarge { get; init; }
}
