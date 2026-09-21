using ArxisStudio.Projects;

namespace ArxisStudio.Modules.Project.History;

/// <summary>Каким был файл в какую-то минуту истории.</summary>
internal enum FileStateKind
{
    /// <summary>Содержимое есть в истории: его можно показать и к нему вернуться.</summary>
    Stored,

    /// <summary>Файла тогда не было.</summary>
    Absent,

    /// <summary>Файл был, но история его содержимого не хранит: больше предела.</summary>
    NotStored,

    /// <summary>Таким, каким он лежит сейчас: после этой строки с ним ничего не случалось.</summary>
    Current,
}

/// <summary>Каким был файл: вид и ручка содержимого, если оно есть.</summary>
/// <param name="Kind">Вид.</param>
/// <param name="Content">Ручка — у <see cref="FileStateKind.Stored"/>.</param>
internal readonly record struct FileState(FileStateKind Kind, LocalHistoryContent Content)
{
    /// <summary>Такой, как сейчас.</summary>
    public static FileState Now { get; } = new(FileStateKind.Current, LocalHistoryContent.None);

    /// <summary>Не было.</summary>
    public static FileState Absent { get; } = new(FileStateKind.Absent, LocalHistoryContent.None);

    /// <summary>Не хранится.</summary>
    public static FileState NotStored { get; } = new(FileStateKind.NotStored, LocalHistoryContent.None);

    /// <summary>Содержимое из истории — или «не хранится», если ручка пуста.</summary>
    /// <param name="content">Ручка.</param>
    public static FileState Of(LocalHistoryContent content) =>
        content.IsEmpty ? NotStored : new FileState(FileStateKind.Stored, content);
}

/// <summary>
/// Каким файл был перед каждым действием его истории.
/// </summary>
/// <remarks>
/// <para>
/// Строка истории — действие, а показывает окно, каким файл был <b>до</b> него: так возврат к строке
/// отменяет её действие и всё, что было после, — как «Revert» у IntelliJ, — а самое раннее
/// содержимое, снятое опорным снимком, остаётся достижимым: оно «до» самой старой правки.
/// </para>
/// <para>
/// Идём от новой строки к старой. После новой файл такой, как сейчас. Перед правкой — её «до»;
/// перед появлением файла не было; перед удалением — удалённое. Переезд содержимого не меняет:
/// перед ним файл такой же, как после, если сам переезд не записал «до». Метка тоже ничего не меняет.
/// </para>
/// </remarks>
internal static class Timeline
{
    /// <summary>Каким файл был перед каждой строкой — в том же порядке, от новой к старой.</summary>
    /// <param name="rows">История файла, от новой строки к старой.</param>
    public static IReadOnlyList<FileState> Before(IReadOnlyList<LocalHistoryRevision> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var states = new FileState[rows.Count];
        var after = FileState.Now;

        for (var at = 0; at < rows.Count; at++)
        {
            // У файла правка в строке одна: своя или переезд папки, в которой он лежал.
            var before = rows[at].Changes.FirstOrDefault() is not { } change
                ? after
                : change.Kind switch
                {
                    LocalHistoryChangeKind.Created when !change.IsDirectory => FileState.Absent,
                    LocalHistoryChangeKind.Deleted or LocalHistoryChangeKind.Modified when !change.IsDirectory => FileState.Of(change.Before),
                    LocalHistoryChangeKind.Moved when !change.IsDirectory && !change.Before.IsEmpty => FileState.Of(change.Before),
                    _ => after,
                };

            states[at] = before;
            after = before;
        }

        return states;
    }
}
