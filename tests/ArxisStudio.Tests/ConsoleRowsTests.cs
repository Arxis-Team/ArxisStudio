using ArxisStudio.Modules.Console.Log;
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

        var sources = LogSources.Only("Layout", ["Plugins", "Layout"]);
        var built = LogRows.Build(records, LogFilter.Everything with { Sources = sources }, collapse: false);

        Assert.Equal("два", Assert.Single(built.Rows).Text);
    }

    /// <summary>Спрятанный источник пропадает из списка, остальные остаются.</summary>
    [Fact]
    public void A_hidden_source_leaves_the_list_and_the_others_stay()
    {
        var records = Records(
            (StudioLogLevel.Info, "Plugins", "раз"),
            (StudioLogLevel.Info, "Layout", "два"),
            (StudioLogLevel.Info, "Startup", "три"));

        var sources = LogSources.All.Hide("Layout");
        var built = LogRows.Build(records, LogFilter.Everything with { Sources = sources }, collapse: false);

        Assert.Equal(["раз", "три"], built.Rows.Select(row => row.Text));
    }

    /// <summary>
    /// Источник, о котором отбор не знал, показан.
    /// </summary>
    /// <remarks>
    /// Ради этого отбор и перечисляет спрятанных. Плагин просыпается щелчком, сборка начинается
    /// через полчаса после запуска, и первая же их ошибка обязана дойти до глаз — даже если отбор
    /// человек сделал час назад.
    /// </remarks>
    [Fact]
    public void A_source_the_filter_never_heard_of_is_shown()
    {
        var records = Records(
            (StudioLogLevel.Info, "Plugins", "раз"),
            (StudioLogLevel.Error, "Hello", "упал"));

        var sources = LogSources.Only("Plugins", ["Plugins", "Layout"]);
        var built = LogRows.Build(records, LogFilter.Everything with { Sources = sources }, collapse: false);

        Assert.Equal(["раз", "упал"], built.Rows.Select(row => row.Text));
    }

    /// <summary>Отбор по источнику сравнивается как множество, а не как ссылка.</summary>
    /// <remarks>
    /// На этом равенстве стоит отказ панели перестраивать список: меню остаётся открытым, и один и
    /// тот же отбор приходит к ней столько раз, сколько человек щёлкнул.
    /// </remarks>
    [Fact]
    public void Two_source_filters_hiding_the_same_names_are_equal()
    {
        var first = LogSources.All.Hide("Plugins").Hide("Layout");
        var second = LogSources.All.Hide("Layout").Hide("Plugins");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second.Show("Layout"));

        Assert.True(LogSources.All.ShowsAll, "Пустой отбор не прячет никого");
        Assert.True(LogSources.All.Hide("A").Show("A").ShowsAll, "Спрятанный и показанный — это пустой отбор");
    }

    /// <summary>Флажок источника переворачивается в обе стороны.</summary>
    [Fact]
    public void A_source_toggles_both_ways()
    {
        var hidden = LogSources.All.Toggle("Plugins");

        Assert.False(hidden.Shows("Plugins"), "После переворота источник спрятан");
        Assert.True(hidden.Toggle("Plugins").Shows("Plugins"), "Второй переворот возвращает источник");
        Assert.Equal(LogSources.All.Hide("Plugins"), hidden);
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

    /// <summary>
    /// Счётчик повторов называет себя словами.
    /// </summary>
    /// <remarks>
    /// Знак показывает одно число — места у него на число, — и «3» рядом с записью значит что
    /// угодно, пока подсказка не скажет, что это повторы. Формат приходит из словаря студии, и
    /// строка журнала его только подставляет.
    /// </remarks>
    [Fact]
    public void The_repeat_badge_says_what_its_number_means()
    {
        var row = new LogRow(Record(StudioLogLevel.Info, "Плагины", "поднят"), repeats: "Повторено {0} раз");

        row.Repeat();
        row.Repeat();

        Assert.Equal("Повторено 3 раз", row.RepeatsTip);
        Assert.Equal("3", row.RepeatsText);
    }

    /// <summary>Без формата подсказка остаётся числом — и не врёт.</summary>
    [Fact]
    public void Without_a_caption_the_badge_keeps_its_number()
    {
        var row = new LogRow(Record(StudioLogLevel.Info, "Плагины", "поднят"));

        row.Repeat();

        Assert.Equal("2", row.RepeatsTip);
    }

    /// <summary>
    /// В буфер обмена запись уходит целиком, а не первой строкой.
    /// </summary>
    /// <remarks>
    /// Список показывает первую строку, потому что строка списка однострочна. Копируют запись как
    /// раз ради остального — стека исключения.
    /// </remarks>
    [Fact]
    public void A_copied_record_carries_its_whole_message()
    {
        var row = new LogRow(Record(StudioLogLevel.Error, "Плагины", "не вышло\r\n  в методе Open\r\n  в методе Run"));

        var text = LogText.Of(row);

        Assert.Contains("в методе Run", text, StringComparison.Ordinal);
        Assert.StartsWith(row.Stamp, text, StringComparison.Ordinal);
        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("Плагины", text, StringComparison.Ordinal);
    }

    /// <summary>Несколько записей уходят в порядке показа, каждая своей строкой.</summary>
    [Fact]
    public void Copied_records_keep_the_order_they_are_shown_in()
    {
        var first = new LogRow(Record(StudioLogLevel.Info, "A", "раз"));
        var second = new LogRow(Record(StudioLogLevel.Info, "A", "два"));

        var text = LogText.Of([first, second]);

        Assert.Equal($"{LogText.Of(first)}{Environment.NewLine}{LogText.Of(second)}", text);
    }

    /// <summary>Только сообщение — без времени, уровня и источника.</summary>
    [Fact]
    public void A_copied_message_carries_nothing_but_itself()
    {
        var row = new LogRow(Record(StudioLogLevel.Warning, "A", "внимание"));

        Assert.Equal("внимание", LogText.Message(row));
    }

    /// <summary>
    /// Уровень строка знает о себе сама — им список выбирает значок.
    /// </summary>
    /// <remarks>
    /// Слово уровня приходит из SDK по-английски, и на экране его больше нет: уровень назван
    /// значком, как в консоли Unity. Значок выбирается по этим четырём ответам, и ровно один из
    /// них истинный.
    /// </remarks>
    [Theory]
    [InlineData(StudioLogLevel.Error)]
    [InlineData(StudioLogLevel.Warning)]
    [InlineData(StudioLogLevel.Info)]
    [InlineData(StudioLogLevel.Debug)]
    public void A_row_answers_for_exactly_one_level(StudioLogLevel level)
    {
        var row = new LogRow(Record(level, "A", "раз"));

        bool[] answers = [row.IsError, row.IsWarning, row.IsInfo, row.IsDebug];

        Assert.Single(answers, answer => answer);
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
    }

    private static StudioLogRecord Record(StudioLogLevel level, string source, string message) =>
        new(DateTimeOffset.Now, level, source, message);

    private static List<StudioLogRecord> Records(params (StudioLogLevel Level, string Source, string Message)[] written) =>
        [.. written.Select(record => new StudioLogRecord(
            DateTimeOffset.Now, record.Level, record.Source, record.Message))];
}
