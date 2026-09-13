using ArxisStudio.Palette;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Палитра команд: отбор и сбор списка.
/// </summary>
/// <remarks>
/// Своей описи у палитры нет, и это в ней главное: названия команд расширений
/// она берёт у меню — того самого дерева, что строится по манифестам без
/// загрузки сборок. Заведи она второй список, он разошёлся бы с меню на первой
/// же правке и разошёлся бы молча.
/// </remarks>
public class CommandPaletteTests
{
    /// <summary>Свои команды идут первыми, за ними — команды меню.</summary>
    /// <remarks>
    /// Своё выше принесённого — тем же правилом, что и в самом меню: иначе
    /// порядок зависел бы от того, что человек успел установить.
    /// </remarks>
    [Fact]
    public void The_studios_own_commands_come_first()
    {
        var gathered = CommandPalette.Gather(
            [Branch("Инструменты", Leaf("Поздороваться", "hello.greet"))],
            [new PaletteEntry("Закрыть вкладку", "studio.close")],
            _ => null);

        Assert.Equal(["studio.close", "hello.greet"], gathered.Select(entry => entry.CommandId));
    }

    /// <summary>
    /// Одна команда — одна строка, сколько бы раз её ни объявили.
    /// </summary>
    /// <remarks>
    /// Повтор здесь не выдумка: команду объявляют и пунктом меню, и кнопкой
    /// полосы, и дважды в разных ветках. В списке она должна быть одна.
    /// </remarks>
    [Fact]
    public void One_command_is_one_row_however_often_it_was_declared()
    {
        var gathered = CommandPalette.Gather(
            [
                Branch("Инструменты", Leaf("Поздороваться", "hello.greet")),
                Branch("Правка", Leaf("Поздороваться ещё раз", "hello.greet")),
            ],
            [],
            _ => null);

        Assert.Equal("Поздороваться", Assert.Single(gathered).Title);
    }

    /// <summary>Ветки в палитру не попадают: нажать на них нечем.</summary>
    [Fact]
    public void Branches_do_not_get_into_the_palette()
    {
        var gathered = CommandPalette.Gather([Branch("Инструменты")], [], _ => null);

        Assert.Empty(gathered);
    }

    /// <summary>Сочетание команды пишется рядом с названием.</summary>
    /// <remarks>
    /// Палитра — место, где человек узнаёт, что у команды есть клавиша. Без
    /// этого он ходил бы через палитру всегда.
    /// </remarks>
    [Fact]
    public void The_gesture_of_a_command_stands_next_to_its_name()
    {
        var gathered = CommandPalette.Gather(
            [],
            [new PaletteEntry("Закрыть вкладку", "studio.close")],
            id => id == "studio.close" ? "Ctrl+W" : null);

        Assert.Equal("Ctrl+W", Assert.Single(gathered).Gesture);
    }

    /// <summary>Пустой запрос показывает всё.</summary>
    [Fact]
    public void An_empty_query_shows_everything()
    {
        var all = All();

        Assert.Same(all, CommandPalette.Match(all, null));
        Assert.Same(all, CommandPalette.Match(all, "   "));
    }

    /// <summary>
    /// Начинающиеся с набранного идут впереди.
    /// </summary>
    /// <remarks>
    /// Первая строка выбрана заранее, и нажать Enter человек может не глядя.
    /// Значит первой обязана быть та, которую он набирал, а не та, у которой
    /// набранное случилось в середине.
    /// </remarks>
    [Fact]
    public void Those_that_begin_with_the_query_come_first()
    {
        var found = CommandPalette.Match(All(), "след");

        Assert.Equal("Следующая панель", found[0].Title);
        Assert.Contains(found, entry => entry.Title == "Перейти к следующему");
        Assert.DoesNotContain(found, entry => entry.Title == "Закрыть вкладку");
    }

    /// <summary>Регистр и лишние пробелы в запросе не мешают.</summary>
    [Fact]
    public void Case_and_stray_spaces_do_not_get_in_the_way()
    {
        Assert.Equal("Закрыть вкладку", CommandPalette.Match(All(), "  ЗАКРЫТЬ  ".Trim())[0].Title);
    }

    private static IReadOnlyList<PaletteEntry> All() =>
    [
        new("Закрыть вкладку", "studio.close"),
        new("Следующая панель", "studio.panel.next"),
        new("Перейти к следующему", "hello.next"),
    ];

    private static StudioMenuItem Leaf(string title, string command) => new(title, "hello", command);

    private static StudioMenuItem Branch(string title, params StudioMenuItem[] children)
    {
        var branch = new StudioMenuItem(title);

        branch.Children.AddRange(children);

        return branch;
    }
}
