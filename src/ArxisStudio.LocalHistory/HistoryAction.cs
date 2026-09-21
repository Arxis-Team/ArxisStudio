using System.Collections.Immutable;

namespace ArxisStudio.LocalHistory;

/// <summary>
/// Действие: одна или несколько правок, случившихся вместе и под одной меткой.
/// </summary>
/// <remarks>
/// <para>
/// Переименование файла вместе с вложенным в него — одно действие, а не два: отменяют его тоже
/// целиком. Так же и пачка внешних перемен, пришедшая одним сохранением или одним переключением
/// ветки.
/// </para>
/// <para>
/// Действие без правок — метка, как «Put Label» у IntelliJ: отметка на времени, к которой потом
/// возвращаются («до переделки»). Правок у неё нет, а место есть — папка, на которой её поставили.
/// </para>
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

    /// <summary>Правки по порядку; у метки — пусто.</summary>
    public required ImmutableArray<HistoryChange> Changes { get; init; }

    /// <summary>Какое действие это отменяет; null — это не отмена.</summary>
    /// <remarks>
    /// Отмена — такое же действие, со своими правками и своим содержимым: отменённое можно вернуть,
    /// отменив отмену. Номер нужен, чтобы знать, что уже отменено (<see cref="LocalHistoryStore.Undone"/>).
    /// </remarks>
    public long? Undoes { get; init; }

    /// <summary>У метки — папка, на которой её поставили: метку видно в истории всего, что под ней.</summary>
    /// <remarks>У правок — null. Метка без папки видна везде.</remarks>
    public string? Scope { get; init; }

    /// <summary>Это метка, а не правка.</summary>
    public bool IsLabel => Changes.IsDefaultOrEmpty;
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
