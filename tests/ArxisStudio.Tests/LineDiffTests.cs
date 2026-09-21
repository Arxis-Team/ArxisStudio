using System.Diagnostics;
using ArxisStudio.Modules.Project.History;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Разница строк окна истории: кратчайшая правка, пары переписанных строк и предел для текстов,
/// разошедшихся целиком.
/// </summary>
public class LineDiffTests
{
    /// <summary>Одинаковые тексты — одни общие строки, с номерами с обеих сторон.</summary>
    [Fact]
    public void Equal_texts_give_only_same_rows()
    {
        var rows = LineDiff.Rows(["a", "b"], ["a", "b"]);

        Assert.Equal(
            [new DiffRow(DiffKind.Same, 1, "a", 1, "a"), new DiffRow(DiffKind.Same, 2, "b", 2, "b")],
            rows);
    }

    /// <summary>Вставленная строка есть только справа, и номера после неё сдвигаются справа.</summary>
    [Fact]
    public void An_inserted_line_stands_on_the_right_only()
    {
        Assert.Equal(
        [
            new DiffRow(DiffKind.Same, 1, "a", 1, "a"),
            new DiffRow(DiffKind.Added, 0, null, 2, "b"),
            new DiffRow(DiffKind.Same, 2, "c", 3, "c"),
        ], LineDiff.Rows(["a", "c"], ["a", "b", "c"]));
    }

    /// <summary>Удалённая строка есть только слева.</summary>
    [Fact]
    public void A_deleted_line_stands_on_the_left_only()
    {
        Assert.Equal(
        [
            new DiffRow(DiffKind.Same, 1, "a", 1, "a"),
            new DiffRow(DiffKind.Removed, 2, "b", 0, null),
            new DiffRow(DiffKind.Same, 3, "c", 2, "c"),
        ], LineDiff.Rows(["a", "b", "c"], ["a", "c"]));
    }

    /// <summary>
    /// Переписанная строка стоит напротив прежней; лишние остаются удалёнными или добавленными.
    /// </summary>
    [Fact]
    public void A_rewritten_line_stands_opposite_its_old_self_and_the_rest_stays_apart()
    {
        Assert.Equal(
        [
            new DiffRow(DiffKind.Same, 1, "a", 1, "a"),
            new DiffRow(DiffKind.Changed, 2, "b", 2, "x"),
            new DiffRow(DiffKind.Removed, 3, "c", 0, null),
            new DiffRow(DiffKind.Same, 4, "d", 3, "d"),
        ], LineDiff.Rows(["a", "b", "c", "d"], ["a", "x", "d"]));

        Assert.Equal(
        [
            new DiffRow(DiffKind.Changed, 1, "a", 1, "x"),
            new DiffRow(DiffKind.Added, 0, null, 2, "y"),
        ], LineDiff.Rows(["a"], ["x", "y"]));
    }

    /// <summary>Пустое против текста — весь текст с одной стороны.</summary>
    [Fact]
    public void Nothing_against_a_text_is_all_on_one_side()
    {
        Assert.All(LineDiff.Rows([], ["a", "b"]), row => Assert.Equal(DiffKind.Added, row.Kind));
        Assert.All(LineDiff.Rows(["a", "b"], []), row => Assert.Equal(DiffKind.Removed, row.Kind));
        Assert.Empty(LineDiff.Rows([], []));
    }

    /// <summary>
    /// Правка — кратчайшая: на случайных текстах её длина равна той, что даёт общая
    /// подпоследовательность, и шаги собирают обе стороны обратно.
    /// </summary>
    /// <remarks>
    /// Длина кратчайшей правки — N + M − 2·LCS, и наибольшая общая подпоследовательность считается
    /// здесь медленно и наверняка, таблицей. Алфавит из трёх строк даёт много повторов — ровно то, на
    /// чём жадный поиск мог бы ошибиться.
    /// </remarks>
    [Fact]
    public void The_edit_is_the_shortest_and_rebuilds_both_texts()
    {
        var random = new Random(256);

        for (var round = 0; round < 400; round++)
        {
            var before = Text(random);
            var after = Text(random);
            var script = LineDiff.Script(before, after);
            var edits = script.Count(step => step.Kind != DiffKind.Same);

            Assert.Equal(before.Count + after.Count - (2 * Common(before, after)), edits);
            Assert.Equal(before, script.Where(step => step.Kind != DiffKind.Added).Select(step => before[step.Left]));
            Assert.Equal(after, script.Where(step => step.Kind != DiffKind.Removed).Select(step => after[step.Right]));
            Assert.All(script.Where(step => step.Kind == DiffKind.Same), step => Assert.Equal(before[step.Left], after[step.Right]));
        }
    }

    /// <summary>Классический пример Майерса: ABCABBA против CBABAC — правка длиной пять.</summary>
    [Fact]
    public void Myers_own_example_takes_five_edits()
    {
        var script = LineDiff.Script([.. "ABCABBA".Select(c => c.ToString())], [.. "CBABAC".Select(c => c.ToString())]);

        Assert.Equal(5, script.Count(step => step.Kind != DiffKind.Same));
    }

    /// <summary>Строки делятся любым переводом строки, и последний перевод пустой строки не даёт.</summary>
    [Fact]
    public void Lines_split_on_any_break_and_the_last_break_adds_nothing()
    {
        Assert.Equal(["a", "b", "c", "d"], LineDiff.Lines("a\r\nb\nc\rd\n"));
        Assert.Equal(["x"], LineDiff.Lines("x"));
        Assert.Equal([string.Empty], LineDiff.Lines("\n"));
        Assert.Equal(["a", string.Empty, "b"], LineDiff.Lines("a\n\nb"));
        Assert.Empty(LineDiff.Lines(string.Empty));
    }

    /// <summary>Малая правка большого файла находится точно — и быстро.</summary>
    [Fact]
    public void A_small_edit_in_a_large_file_is_found_exactly()
    {
        var before = Enumerable.Range(0, 50_000).Select(at => $"строка {at}").ToList();
        var after = before.ToList();

        after[25_000] = "переписано";
        after.Insert(40_000, "вставлено");

        var clock = Stopwatch.StartNew();
        var rows = LineDiff.Rows(before, after);

        clock.Stop();

        Assert.Equal(
            [(DiffKind.Changed, 25_001), (DiffKind.Added, 40_001)],
            rows.Where(row => row.Kind != DiffKind.Same).Select(row => (row.Kind, row.Right)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"малая правка большого файла искалась {clock.Elapsed}");
    }

    /// <summary>
    /// Тексты, разошедшиеся сильнее предела, приходят одним переписанным куском — и не держат окно.
    /// </summary>
    [Fact]
    public void Texts_too_different_come_as_one_rewritten_block_without_delay()
    {
        var before = Enumerable.Range(0, 20_000).Select(at => $"было {at}").ToList();
        var after = Enumerable.Range(0, 20_000).Select(at => $"стало {at}").ToList();

        var clock = Stopwatch.StartNew();
        var rows = LineDiff.Rows(before, after);

        clock.Stop();

        Assert.Equal(20_000, rows.Count);
        Assert.All(rows, row => Assert.Equal(DiffKind.Changed, row.Kind));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"разошедшиеся тексты сравнивались {clock.Elapsed}");
    }

    private static List<string> Text(Random random) =>
        [.. Enumerable.Range(0, random.Next(0, 13)).Select(_ => ((char)('a' + random.Next(3))).ToString())];

    /// <summary>Длина наибольшей общей подпоследовательности — таблицей.</summary>
    private static int Common(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var table = new int[a.Count + 1, b.Count + 1];

        for (var x = 1; x <= a.Count; x++)
        {
            for (var y = 1; y <= b.Count; y++)
            {
                table[x, y] = a[x - 1] == b[y - 1]
                    ? table[x - 1, y - 1] + 1
                    : Math.Max(table[x - 1, y], table[x, y - 1]);
            }
        }

        return table[a.Count, b.Count];
    }
}
