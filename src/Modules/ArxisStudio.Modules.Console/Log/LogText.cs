using System.Text;

namespace ArxisStudio.Modules.Console.Log;

/// <summary>
/// Запись журнала текстом: то, что уходит в буфер обмена и в подробности.
/// </summary>
/// <remarks>
/// Отдельным типом, а не парой строк внутри панели: вид записи один и тот же у трёх дорог — кнопка
/// полосы, <c>Ctrl+C</c> и контекстное меню, — и разойдись они, человек получал бы разное в
/// зависимости от того, как попросил.
/// <para>
/// Сообщение берётся целиком, со стеком: в списке видна первая строка, потому что строка списка
/// однострочна, но копируют запись ради остального.
/// </para>
/// </remarks>
public static class LogText
{
    /// <summary>Запись одной строкой: время, уровень, источник, сообщение.</summary>
    /// <param name="row">Строка журнала.</param>
    /// <returns>Текст записи; сообщение — целиком.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> равен <c>null</c>.</exception>
    public static string Of(LogRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return $"{row.Stamp} {row.Level} {row.Source} {row.Record.Message}";
    }

    /// <summary>
    /// Несколько записей подряд, в порядке показа.
    /// </summary>
    /// <param name="rows">Строки журнала.</param>
    /// <returns>Записи, разделённые переводом строки; пустая строка — если записей нет.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> равен <c>null</c>.</exception>
    /// <remarks>
    /// Разделитель — <see cref="Environment.NewLine"/>: текст уходит в буфер обмена, а оттуда — в
    /// письмо, в отчёт или в редактор той же машины.
    /// </remarks>
    public static string Of(IEnumerable<LogRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var text = new StringBuilder();

        foreach (var row in rows)
        {
            if (text.Length > 0)
                text.Append(Environment.NewLine);

            text.Append(Of(row));
        }

        return text.ToString();
    }

    /// <summary>Только сообщение записи, без времени, уровня и источника.</summary>
    /// <param name="row">Строка журнала.</param>
    /// <returns>Сообщение целиком.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> равен <c>null</c>.</exception>
    /// <remarks>
    /// Нужно, когда сообщение несут дальше — в поиск по коду или в отчёт об ошибке: приставка о
    /// времени и источнике там только мешает.
    /// </remarks>
    public static string Message(LogRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.Record.Message;
    }
}
