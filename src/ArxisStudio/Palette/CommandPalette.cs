using ArxisStudio.Services;

namespace ArxisStudio.Palette;

/// <summary>Строка палитры.</summary>
/// <param name="Title">Что написано.</param>
/// <param name="CommandId">Кого звать.</param>
/// <param name="Gesture">Каким сочетанием её зовут ещё; <c>null</c> — никаким.</param>
public sealed record PaletteEntry(string Title, string CommandId, string? Gesture = null);

/// <summary>
/// Палитра команд: всё, что студия умеет, одним списком.
/// </summary>
/// <remarks>
/// Своей описи у палитры нет, и это главное в ней. Названия команд расширений
/// она берёт у меню — того самого дерева, что строится по манифестам без
/// загрузки сборок, — а свои команды студия называет сама. Заведи палитра
/// второй список, он разошёлся бы с меню на первой же правке, и разошёлся бы
/// молча.
/// <para>
/// Ради неё же терпимо правило «занятое сочетание второму не достаётся»:
/// команда, оставшаяся без жеста, отсюда в одном нажатии. Без палитры это
/// правило было бы просто потерей.
/// </para>
/// </remarks>
public static class CommandPalette
{
    /// <summary>
    /// Собирает строки палитры.
    /// </summary>
    /// <param name="menu">Дерево меню: команды расширений с переведёнными названиями.</param>
    /// <param name="own">Команды самой студии — названия у неё свои.</param>
    /// <param name="declared">Команды, назвавшие себя в манифесте.</param>
    /// <param name="gesture">Чем зовут команду, кроме палитры; <c>null</c> — ничем.</param>
    /// <returns>Строки без повторов, свои первыми.</returns>
    /// <remarks>
    /// Повтор здесь не выдумка: одну команду можно объявить и пунктом меню, и
    /// кнопкой полосы, и дважды в разных ветках. В списке она должна быть одна.
    /// <para>
    /// Порядок источников решает спор о названии, и решает в пользу меню:
    /// назвавшись и там и там, команда покажется текстом пункта. Название в
    /// манифесте нужно тем, у кого пункта нет, — иначе оно стало бы вторым
    /// местом для одной строки, и рано или поздно они разошлись бы.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PaletteEntry> Gather(
        IReadOnlyList<StudioMenuItem> menu,
        IReadOnlyList<PaletteEntry> own,
        IReadOnlyList<PaletteEntry> declared,
        Func<string, string?> gesture)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(gesture);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var gathered = new List<PaletteEntry>();

        foreach (var entry in own.Concat(Flatten(menu)).Concat(declared))
        {
            if (seen.Add(entry.CommandId))
                gathered.Add(entry with { Gesture = gesture(entry.CommandId) });
        }

        return gathered;
    }

    /// <summary>
    /// Отбирает строки по набранному.
    /// </summary>
    /// <param name="all">Всё, что есть.</param>
    /// <param name="query">Что набрали; пусто — всё.</param>
    /// <returns>Отобранное: начинающиеся с запроса впереди, дальше по алфавиту.</returns>
    /// <remarks>
    /// Отбор подстрокой, а не по буквам вразбивку. Вразбивку удобнее тому, кто
    /// помнит название, и хуже тому, кто его ищет: «пан» находило бы половину
    /// списка, и первая строка перестала бы быть предсказуемой. Начать с
    /// предсказуемого правильнее, чем с умного.
    /// </remarks>
    public static IReadOnlyList<PaletteEntry> Match(IReadOnlyList<PaletteEntry> all, string? query)
    {
        ArgumentNullException.ThrowIfNull(all);

        if (string.IsNullOrWhiteSpace(query))
            return all;

        var asked = query.Trim();

        return
        [
            .. all
                .Where(entry => entry.Title.Contains(asked, StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(entry => entry.Title.StartsWith(asked, StringComparison.CurrentCultureIgnoreCase))
                .ThenBy(entry => entry.Title, StringComparer.CurrentCulture),
        ];
    }

    /// <summary>Разворачивает дерево меню в плоский список команд.</summary>
    /// <remarks>
    /// Ветки в палитру не попадают: раскрывать здесь нечего, а «Инструменты»
    /// без вложенного — строка, на которую нельзя нажать.
    /// </remarks>
    private static IEnumerable<PaletteEntry> Flatten(IEnumerable<StudioMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { Length: > 0 } id)
                yield return new PaletteEntry(item.Title, id);

            foreach (var nested in Flatten(item.Children))
                yield return nested;
        }
    }
}
