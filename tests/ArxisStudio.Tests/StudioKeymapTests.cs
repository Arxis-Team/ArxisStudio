using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// <c>keymap.json</c>: сочетания, которые человек назначил сам.
/// </summary>
/// <remarks>
/// Файл пишет человек руками, и чтение ему прощает то, что прощают редакторы: комментарий,
/// запятую после последнего значения. Не прощает только непонятного — и тогда говорит, что именно
/// не разобралось, а непонятое оставляет при своём по умолчанию.
/// </remarks>
public class StudioKeymapTests
{
    /// <summary>Файла нет — сочетания по умолчанию, и жаловаться не на что.</summary>
    [Fact]
    public void A_missing_file_changes_nothing_and_says_nothing()
    {
        var keymap = StudioKeymap.Load(Path.Combine(Path.GetTempPath(), $"arxis-keymap-{Guid.NewGuid():N}.json"));

        Assert.Empty(keymap.Entries);
        Assert.Empty(keymap.Complaints);
    }

    /// <summary>Строка — одно сочетание, массив — несколько, null и пустая строка — ни одного.</summary>
    [Fact]
    public void A_string_an_array_and_null_all_read()
    {
        var keymap = StudioKeymap.Parse("""
            {
              "studio.palette": "Ctrl+Alt+P",
              "studio.close": ["Ctrl+W", "Ctrl+F4"],
              "hello.greet": null,
              "hello.focus": ""
            }
            """);

        Assert.Empty(keymap.Complaints);
        Assert.Equal(["studio.palette", "studio.close", "hello.greet", "hello.focus"], keymap.Entries.Select(entry => entry.CommandId));
        Assert.Equal(["Ctrl+Alt+P"], keymap.Entries[0].Gestures);
        Assert.Equal(["Ctrl+W", "Ctrl+F4"], keymap.Entries[1].Gestures);
        Assert.Empty(keymap.Entries[2].Gestures);
        Assert.Empty(keymap.Entries[3].Gestures);
    }

    /// <summary>Комментарий и запятая после последнего значения файл не ломают.</summary>
    [Fact]
    public void Comments_and_a_trailing_comma_are_allowed()
    {
        var keymap = StudioKeymap.Parse("""
            {
              // палитра под рукой
              "studio.palette": "Ctrl+Alt+P",
            }
            """);

        Assert.Empty(keymap.Complaints);
        Assert.Equal("studio.palette", Assert.Single(keymap.Entries).CommandId);
    }

    /// <summary>Файл, который не разобрался целиком, не меняет ничего и говорит об этом.</summary>
    [Fact]
    public void A_broken_file_is_told_and_changes_nothing()
    {
        foreach (var broken in new[] { "{ \"studio.palette\": ", "[\"Ctrl+W\"]" })
        {
            var keymap = StudioKeymap.Parse(broken);

            Assert.Empty(keymap.Entries);
            Assert.Contains("keymap.json", Assert.Single(keymap.Complaints));
        }
    }

    /// <summary>
    /// Неразобранное сочетание называется, а команда без единого разобранного остаётся при своём.
    /// </summary>
    /// <remarks>
    /// Человек хотел другое сочетание, а не никакого: опечатка не должна снимать клавишу, которой
    /// он пользовался. Разобранное рядом с опечаткой при этом работает.
    /// </remarks>
    [Fact]
    public void An_unreadable_gesture_is_told_and_the_default_stays()
    {
        var keymap = StudioKeymap.Parse("""
            {
              "studio.close": "Ctrl+Шифт",
              "studio.palette": ["Ctrl+Шифт+P", "Ctrl+Alt+P"]
            }
            """);

        var entry = Assert.Single(keymap.Entries);

        Assert.Equal("studio.palette", entry.CommandId);
        Assert.Equal(["Ctrl+Alt+P"], entry.Gestures);
        Assert.Equal(2, keymap.Complaints.Count);
        Assert.Contains(keymap.Complaints, complaint => complaint.Contains("«Ctrl+Шифт»") && complaint.Contains("studio.close"));
    }

    /// <summary>Значение не того вида называется, а команда остаётся при своём.</summary>
    [Fact]
    public void A_value_of_the_wrong_kind_is_told_and_the_default_stays()
    {
        var keymap = StudioKeymap.Parse("""{ "studio.close": 5, "studio.palette": ["Ctrl+Alt+P", 7] }""");

        Assert.Empty(keymap.Entries);
        Assert.Equal(2, keymap.Complaints.Count);
    }

    /// <summary>Названная дважды команда берёт последнее значение, но стоит на месте первого.</summary>
    /// <remarks>
    /// Место решает, кому достаётся сочетание, названное у двух команд, — и правка значения внизу
    /// файла не должна молча переставлять команду в очереди.
    /// </remarks>
    [Fact]
    public void A_command_named_twice_takes_the_last_value_in_the_first_place()
    {
        var keymap = StudioKeymap.Parse("""
            {
              "studio.close": "Ctrl+W",
              "studio.palette": "Ctrl+Alt+P",
              "studio.close": "Ctrl+F4"
            }
            """);

        Assert.Equal(["studio.close", "studio.palette"], keymap.Entries.Select(entry => entry.CommandId));
        Assert.Equal(["Ctrl+F4"], keymap.Entries[0].Gestures);
        Assert.Contains("studio.close", Assert.Single(keymap.Complaints));
    }
}
