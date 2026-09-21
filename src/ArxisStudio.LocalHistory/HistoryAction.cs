using System.Collections.Immutable;

namespace ArxisStudio.LocalHistory;

/// <summary>
/// Действие: одна или несколько правок, случившихся вместе и под одной меткой.
/// </summary>
/// <remarks>
/// Переименование файла вместе с вложенным в него — одно действие, а не два: отменяют его тоже
/// целиком. Так же и пачка внешних перемен, пришедшая одним сохранением или одним переключением
/// ветки.
/// </remarks>
public sealed record HistoryAction
{
    /// <summary>Номер: растёт с каждым действием и не повторяется в одной папке истории.</summary>
    public required long Id { get; init; }

    /// <summary>Когда, UTC.</summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>Метка для человека: «Переименование», «Удаление», «Внешнее изменение».</summary>
    public required string Label { get; init; }

    /// <summary>Кто сделал.</summary>
    public required HistoryOrigin Origin { get; init; }

    /// <summary>Правки по порядку.</summary>
    public required ImmutableArray<HistoryChange> Changes { get; init; }
}

/// <summary>Правка пути вместе с действием, которому она принадлежит.</summary>
/// <param name="Action">Действие.</param>
/// <param name="Change">Правка.</param>
public sealed record HistoryRevision(HistoryAction Action, HistoryChange Change);

/// <summary>
/// Каким история видела файл в последний раз.
/// </summary>
/// <param name="Size">Длина в байтах.</param>
/// <param name="Written">Время последней записи, UTC.</param>
/// <param name="Content">Адрес содержимого; null — файл больше предела, и содержимое не хранится.</param>
/// <remarks>
/// По длине и времени записи неизменённый файл узнаётся без чтения: опорный снимок большого решения
/// иначе хэшировал бы каждый файл при каждом открытии.
/// </remarks>
public sealed record HistoryFileState(long Size, DateTime Written, ContentId? Content)
{
    /// <summary>Файл больше предела, и его содержимое история не хранит.</summary>
    public bool TooLarge => Content is null;

    /// <summary>Та же длина и то же время записи — файл, скорее всего, не менялся.</summary>
    /// <param name="size">Длина на диске.</param>
    /// <param name="written">Время записи на диске, UTC.</param>
    public bool Looks(long size, DateTime written) => Size == size && Written == written;
}
