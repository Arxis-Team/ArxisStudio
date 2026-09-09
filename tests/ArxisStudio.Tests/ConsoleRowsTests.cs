using ArxisStudio.Modules.Console.Log;
using ArxisStudio.Modules.Console.Problems;
using ArxisStudio.Sdk;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Отбор, свёртка и счёт — то, из чего консоль строит список.
/// </summary>
/// <remarks>
/// Проверяется без единого контрола: превращение записей в строки — чистая
/// работа, и держать ради неё безголовое приложение значило бы платить
/// секундами за то, что считается миллисекундами. Панель отвечает за другое —
/// за то, что список на экране следует за журналом.
/// </remarks>
public class ConsoleRowsTests
{
    /// <summary>Счётчики считают весь журнал, а не показанное.</summary>
    /// <remarks>
    /// Это и есть смысл счётчика: человек, смотрящий предупреждения, обязан
    /// увидеть, что ошибок двенадцать. Счётчик, падающий до нуля оттого, что
    /// уровень выключили, не сообщает ничего.
    /// </remarks>
    [Fact]
    public void The_counters_count_the_whole_log_not_the_filtered_view()
    {
        var records = Records(
            (StudioLogLevel.Error, "A", "раз"),
            (StudioLogLevel.Warning, "A", "два"),
            (StudioLogLevel.Warning, "B", "три"),
            (StudioLogLevel.Info, "B", "четыре"),
            (StudioLogLevel.Debug, "B", "пять"));

        var built = LogRows.Build(records, LogFilter.Everything with { Warning = false }, collapse: false);

        // Показаны три из пяти — оба предупреждения скрыты.
        Assert.Equal(3, built.Rows.Count);
        Assert.Equal(1, built.Counts.Error);
        Assert.Equal(2, built.Counts.Warning);
        Assert.Equal(1, built.Counts.Info);
        Assert.Equal(1, built.Counts.Debug);
    }

    /// <summary>Отбор по уровню убирает строки, но не записи.</summary>
    [Fact]
    public void Turning_a_level_off_hides_only_its_rows()
    {
        var records = Records(
            (StudioLogLevel.Error, "A", "раз"),
            (StudioLogLevel.Info, "A", "два"));

        var built = LogRows.Build(records, LogFilter.Everything with { Info = false }, collapse: false);

        Assert.Equal("раз", Assert.Single(built.Rows).Text);
    }

    /// <summary>Поиск смотрит и в источник, и в сообщение.</summary>
    /// <remarks>
    /// Источник — то, по чему ищут чаще всего: человек знает, какой плагин его
    /// подвёл, но не знает, какими словами тот об этом сказал.
    /// </remarks>
    [Fact]
    public void The_query_matches_both_the_source_and_the_message()
    {
        var records = Records(
            (StudioLogLevel.Info, "Плагины", "поднят"),
            (StudioLogLevel.Info, "Layout", "плагин встал"),
            (StudioLogLevel.Info, "ToolBar", "кнопка"));

        var built = LogRows.Build(records, LogFilter.Everything with { Query = "плагин" }, collapse: false);

        // Первая подошла источником, вторая — сообщением, третья не подошла ничем.
        Assert.Equal(2, built.Rows.Count);
    }

    /// <summary>Поиск не разбирает регистра.</summary>
    [Fact]
    public void The_query_ignores_case()
    {
        var records = Records((StudioLogLevel.Info, "Plugins", "Поднят"));

        Assert.Single(LogRows.Build(records, LogFilter.Everything with { Query = "ПОДНЯТ" }, collapse: false).Rows);
    }

    /// <summary>Отбор по источнику оставляет только его записи.</summary>
    [Fact]
    public void A_chosen_source_leaves_only_its_own_records()
    {
        var records = Records(
            (StudioLogLevel.Info, "Plugins", "раз"),
            (StudioLogLevel.Info, "Layout", "два"));

        var built = LogRows.Build(records, LogFilter.Everything with { Source = "Layout" }, collapse: false);

        Assert.Equal("два", Assert.Single(built.Rows).Text);
    }

    /// <summary>Свёртка схлопывает одинаковые подряд и считает их.</summary>
    [Fact]
    public void Collapse_merges_identical_records_and_counts_them()
    {
        var records = Records(
            (StudioLogLevel.Warning, "A", "одно и то же"),
            (StudioLogLevel.Warning, "A", "одно и то же"),
            (StudioLogLevel.Warning, "A", "одно и то же"),
            (StudioLogLevel.Warning, "A", "другое"));

        var built = LogRows.Build(records, LogFilter.Everything, collapse: true);

        Assert.Equal(2, built.Rows.Count);
        Assert.Equal(3, built.Rows[0].Repeats);
        Assert.True(built.Rows[0].IsRepeated);
        Assert.Equal(1, built.Rows[1].Repeats);
        Assert.False(built.Rows[1].IsRepeated);

        // Счёт при этом остаётся счётом записей, а не строк.
        Assert.Equal(4, built.Counts.Warning);
    }

    /// <summary>Без свёртки одинаковые остаются отдельными строками.</summary>
    [Fact]
    public void Without_collapse_identical_records_stay_apart()
    {
        var records = Records(
            (StudioLogLevel.Warning, "A", "одно и то же"),
            (StudioLogLevel.Warning, "A", "одно и то же"));

        Assert.Equal(2, LogRows.Build(records, LogFilter.Everything, collapse: false).Rows.Count);
    }

    /// <summary>
    /// Свёртка сводит записи, оказавшиеся соседними после отбора.
    /// </summary>
    /// <remarks>
    /// Сравнивается последняя выпущенная строка, а не последняя запись: человек
    /// видит их соседними и схлопнуть их ждёт.
    /// </remarks>
    [Fact]
    public void Collapse_merges_what_the_filter_made_adjacent()
    {
        var records = Records(
            (StudioLogLevel.Warning, "A", "одно и то же"),
            (StudioLogLevel.Debug, "A", "между ними"),
            (StudioLogLevel.Warning, "A", "одно и то же"));

        var built = LogRows.Build(records, LogFilter.Everything with { Debug = false }, collapse: true);

        Assert.Equal(2, Assert.Single(built.Rows).Repeats);
    }

    /// <summary>Строка списка показывает первую строку сообщения.</summary>
    /// <remarks>
    /// Так в журнал попадает исключение со стеком: строка в двадцать четыре
    /// пикселя, растянутая стеком, сломала бы и вид, и оценку длины полосы
    /// прокрутки. Само сообщение при этом цело — его показывают подробности.
    /// </remarks>
    [Fact]
    public void A_row_shows_the_first_line_and_keeps_the_whole_message()
    {
        var message = "Не вышло" + Environment.NewLine + "   в методе Раз()" + Environment.NewLine + "   в методе Два()";
        var records = Records((StudioLogLevel.Error, "A", message));

        var row = Assert.Single(LogRows.Build(records, LogFilter.Everything, collapse: false).Rows);

        Assert.Equal("Не вышло", row.Text);
        Assert.Equal(message, row.Record.Message);
    }

    /// <summary>Дописанный хвост даёт те же строки, что и полный проход.</summary>
    /// <remarks>
    /// Быстрый путь — оптимизация, и оптимизация обязана давать тот же ответ.
    /// Разойдись они, список после долгого сеанса отличался бы от списка после
    /// смены отбора, и объяснить это человеку было бы нечем.
    /// </remarks>
    [Fact]
    public void Appending_a_tail_gives_what_a_full_pass_gives()
    {
        var records = Records(
            (StudioLogLevel.Info, "A", "раз"),
            (StudioLogLevel.Warning, "A", "два"),
            (StudioLogLevel.Warning, "A", "два"),
            (StudioLogLevel.Error, "B", "три"));

        var whole = LogRows.Build(records, LogFilter.Everything, collapse: true);

        var head = LogRows.Build(records.Take(2).ToList(), LogFilter.Everything, collapse: true);
        var tail = LogRows.Append(head.Rows, records, 2, LogFilter.Everything, collapse: true);

        Assert.Equal(
            whole.Rows.Select(row => (row.Text, row.Repeats)),
            head.Rows.Select(row => (row.Text, row.Repeats)));

        Assert.Equal(whole.Counts, LogRows.Add(head.Counts, tail));
    }

    /// <summary>Время можно убрать из строки, не трогая записи.</summary>
    [Fact]
    public void A_row_can_be_built_without_its_timestamp()
    {
        var records = Records((StudioLogLevel.Info, "A", "раз"));

        Assert.False(LogRows.Build(records, LogFilter.Everything, collapse: false, stamps: false).Rows[0].HasStamp);
        Assert.True(LogRows.Build(records, LogFilter.Everything, collapse: false, stamps: true).Rows[0].HasStamp);
    }

    /// <summary>Находки отбираются по уровню, коду, объяснению и месту.</summary>
    [Fact]
    public void A_finding_is_filtered_by_level_and_by_every_word_it_shows()
    {
        var error = new StudioProblem(StudioProblemSeverity.Error, "AXM3041", "разметка не разобралась", "Окно.axaml", 12);
        var note = new StudioProblem(StudioProblemSeverity.Info, "AXM0001", "к сведению");

        Assert.True(ProblemFilter.Everything.Matches(error));
        Assert.False((ProblemFilter.Everything with { Error = false }).Matches(error));

        Assert.True((ProblemFilter.Everything with { Query = "axm3041" }).Matches(error));
        Assert.True((ProblemFilter.Everything with { Query = "разобралась" }).Matches(error));
        Assert.True((ProblemFilter.Everything with { Query = "Окно.axaml:12" }).Matches(error));
        Assert.False((ProblemFilter.Everything with { Query = "Окно" }).Matches(note));
    }

    /// <summary>Находки считаются по уровням — так же, как записи.</summary>
    [Fact]
    public void The_findings_are_counted_by_level()
    {
        var counts = ProblemCounts.Of(
        [
            new StudioProblem(StudioProblemSeverity.Error, "E1", "раз"),
            new StudioProblem(StudioProblemSeverity.Error, "E2", "два"),
            new StudioProblem(StudioProblemSeverity.Warning, "W1", "три"),
            new StudioProblem(StudioProblemSeverity.Info, "I1", "четыре"),
        ]);

        Assert.Equal(2, counts.Error);
        Assert.Equal(1, counts.Warning);
        Assert.Equal(1, counts.Info);
    }

    /// <summary>
    /// Строка называет себя тем, что в ней написано.
    /// </summary>
    /// <remarks>
    /// Так её читает программа чтения с экрана. Без этого список звучит как
    /// десяток одинаковых объявлений именем класса — скан живой студии
    /// сообщает об этом ошибкой, а не замечанием.
    /// </remarks>
    [Fact]
    public void A_row_names_itself_by_what_it_shows()
    {
        var record = new StudioLogRecord(DateTimeOffset.Now, StudioLogLevel.Error, "Plugins", "упало");
        var said = new LogRow(record).ToString();

        Assert.Contains("ERROR", said, StringComparison.Ordinal);
        Assert.Contains("Plugins", said, StringComparison.Ordinal);
        Assert.Contains("упало", said, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(LogRow), said, StringComparison.Ordinal);

        var finding = new StudioProblem(StudioProblemSeverity.Error, "AXM1", "разметка", "Окно.axaml", 7);
        var about = new ProblemRow(finding).ToString();

        Assert.Contains("AXM1", about, StringComparison.Ordinal);
        Assert.Contains("разметка", about, StringComparison.Ordinal);
        Assert.Contains("Окно.axaml:7", about, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ProblemRow), about, StringComparison.Ordinal);
    }

    private static List<StudioLogRecord> Records(params (StudioLogLevel Level, string Source, string Message)[] written) =>
        [.. written.Select(record => new StudioLogRecord(
            DateTimeOffset.Now, record.Level, record.Source, record.Message))];
}
