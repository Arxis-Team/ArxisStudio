using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Console.Log;

/// <summary>Что вышло из прохода по журналу.</summary>
/// <param name="Rows">Строки для показа, по порядку.</param>
/// <param name="Counts">Сколько записей каждого уровня в журнале целиком.</param>
public sealed record LogBuild(List<LogRow> Rows, LogCounts Counts);

/// <summary>
/// Превращает записи журнала в строки панели.
/// </summary>
/// <remarks>
/// Отбор, свёртка и счётчики считаются одним проходом. Разнести их по трём
/// значило бы пройти по двум тысячам записей трижды за кадр ради ответа,
/// который получается за один.
/// </remarks>
public static class LogRows
{
    /// <summary>
    /// Строит список заново — по всему журналу.
    /// </summary>
    /// <param name="records">Снимок журнала.</param>
    /// <param name="filter">Отбор.</param>
    /// <param name="collapse">Схлопывать ли одинаковые подряд.</param>
    /// <param name="stamps">Показывать ли столбец времени.</param>
    /// <remarks>
    /// Полный проход нужен, когда журнал вытеснил старое, его очистили или
    /// человек сменил отбор: во всех трёх случаях прежние строки не годятся
    /// ни одной.
    /// </remarks>
    public static LogBuild Build(
        IReadOnlyList<StudioLogRecord> records,
        LogFilter filter,
        bool collapse,
        bool stamps = true)
    {
        ArgumentNullException.ThrowIfNull(records);

        var rows = new List<LogRow>(records.Count);
        var counts = new LogCounts();

        foreach (var record in records)
        {
            counts = Count(counts, record.Level);

            if (filter.Matches(record))
                Emit(rows, record, collapse, stamps);
        }

        return new LogBuild(rows, counts);
    }

    /// <summary>
    /// Дописывает хвост к уже построенным строкам.
    /// </summary>
    /// <param name="rows">Строки, которые уже показаны; пополняются на месте.</param>
    /// <param name="records">Снимок журнала.</param>
    /// <param name="from">С какой записи начинать — сколько их уже учтено.</param>
    /// <param name="filter">Отбор.</param>
    /// <param name="collapse">Схлопывать ли одинаковые подряд.</param>
    /// <param name="stamps">Показывать ли столбец времени.</param>
    /// <returns>Сколько записей каждого уровня в дописанном хвосте.</returns>
    /// <remarks>
    /// Быстрый путь: пока журнал только растёт, перестраивать нечего — старые
    /// строки остались теми же, и вместе с ними остались выделение и место
    /// прокрутки. Считать это оптимизацией не стоит: полный проход по двум
    /// тысячам записей на каждую строку журнала — это и есть та работа, из-за
    /// которой панели начинают тормозить.
    /// </remarks>
    public static LogCounts Append(
        IList<LogRow> rows,
        IReadOnlyList<StudioLogRecord> records,
        int from,
        LogFilter filter,
        bool collapse,
        bool stamps = true)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(records);

        var counts = new LogCounts();

        for (var index = Math.Max(from, 0); index < records.Count; index++)
        {
            var record = records[index];

            counts = Count(counts, record.Level);

            if (filter.Matches(record))
                Emit(rows, record, collapse, stamps);
        }

        return counts;
    }

    /// <summary>Складывает два счёта — свой и хвоста.</summary>
    /// <param name="left">Что было.</param>
    /// <param name="right">Что добавилось.</param>
    public static LogCounts Add(LogCounts left, LogCounts right) => new(
        left.Debug + right.Debug,
        left.Info + right.Info,
        left.Warning + right.Warning,
        left.Error + right.Error);

    /// <summary>
    /// Ставит запись строкой — или поднимает счётчик у последней.
    /// </summary>
    /// <remarks>
    /// Сравнение идёт с последней <b>выпущенной</b> строкой, а не с последней
    /// записью журнала: одинаковые записи, между которыми отбор спрятал
    /// чужую, человек видит соседними — и схлопнуть их он ждёт.
    /// </remarks>
    private static void Emit(IList<LogRow> rows, StudioLogRecord record, bool collapse, bool stamps)
    {
        if (collapse && rows.Count > 0 && rows[^1].SameAs(record))
        {
            rows[^1].Repeat();
            return;
        }

        rows.Add(new LogRow(record, stamps));
    }

    private static LogCounts Count(LogCounts counts, StudioLogLevel level) => level switch
    {
        StudioLogLevel.Debug => counts with { Debug = counts.Debug + 1 },
        StudioLogLevel.Warning => counts with { Warning = counts.Warning + 1 },
        StudioLogLevel.Error => counts with { Error = counts.Error + 1 },
        _ => counts with { Info = counts.Info + 1 },
    };
}
