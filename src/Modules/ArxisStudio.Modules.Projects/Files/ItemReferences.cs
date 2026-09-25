using System.Xml.Linq;

namespace ArxisStudio.Modules.Projects.Files;

/// <summary>Что стало с путём: переехал или удалён.</summary>
/// <param name="From">Полный путь до правки.</param>
/// <param name="To">Полный путь после; null — путь удалён.</param>
/// <param name="IsDirectory">Путь — папка: правка касается и всего, что под ней.</param>
/// <remarks>
/// Сопоставление путей с правками стояло четырьмя копиями — у ссылок файла проекта дважды, у правки
/// файлов и у отмены, — и одна из них сравнивала пути строго, а другая прощала хвостовой разделитель.
/// </remarks>
internal sealed record PathChange(string From, string? To, bool IsDirectory)
{
    /// <summary>Куда путь ушёл после правок: нашёлся ли он среди них и куда — null, если удалён.</summary>
    /// <param name="path">Полный путь до правок.</param>
    /// <param name="changes">Правки в порядке их применения; первая подошедшая решает.</param>
    public static (bool Found, string? To) Map(string path, IEnumerable<PathChange> changes)
    {
        foreach (var change in changes)
        {
            if (Same(path, change.From))
                return (true, change.To);

            if (change.IsDirectory && Inside(path, change.From))
                return (true, change.To is { } to ? Moved(path, change.From, to) : null);
        }

        return (false, null);
    }

    /// <summary>Путь в папке, переехавшей целиком: хвост под папкой прежний, голова — новая.</summary>
    /// <param name="path">Путь под прежним местом папки.</param>
    /// <param name="from">Где папка была.</param>
    /// <param name="to">Куда уехала.</param>
    public static string Moved(string path, string from, string to) => to + path[from.Length..];

    /// <summary>Один ли это путь — без оглядки на регистр и хвостовой разделитель.</summary>
    /// <param name="left">Один путь.</param>
    /// <param name="right">Другой.</param>
    public static bool Same(string left, string right) =>
        string.Equals(left.TrimEnd(Path.DirectorySeparatorChar), right.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    /// <summary>Лежит ли путь внутри папки — не на ней самой.</summary>
    /// <param name="path">Путь.</param>
    /// <param name="folder">Папка.</param>
    public static bool Inside(string path, string folder) =>
        path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Переписывает в документе файла проекта ссылки, которые называют переехавшие и удалённые пути.
/// </summary>
/// <remarks>
/// <para>
/// Проект в стиле SDK берёт файлы масками, и переименованный файл он найдёт сам. Но буквальные
/// ссылки остаются на старом имени: <c>&lt;None Update="appsettings.json"&gt;</c> с
/// <c>CopyToOutputDirectory</c> после переименования молча перестаёт копировать файл, а маска
/// <c>&lt;AvaloniaResource Include="Assets\**"/&gt;</c> после переименования папки перестаёт брать
/// ресурсы. Rider и Visual Studio такие ссылки переписывают — и здесь так же.
/// </para>
/// <para>
/// <b>Что переписывается.</b> У элементов групп — <c>Include</c>, <c>Update</c> и <c>Remove</c>,
/// каждый путь списка через <c>;</c> отдельно: буквальный путь переехавшего получает новый путь,
/// удалённого — снимается, а элемент, у которого снялись все пути, уходит целиком. У маски
/// переписывается буквальная часть до первого знака маски, если она лежит в переехавшей папке.
/// <c>DependentUpon</c> — и элементом, и атрибутом — идёт за своим владельцем и за самим элементом.
/// </para>
/// <para>
/// <b>Что не трогается.</b> Пути со свойствами и метаданными (<c>$(…)</c>, <c>@(…)</c>,
/// <c>%(…)</c>) — их значения знает только вычисление проекта. <c>Link</c> — он называет место в
/// дереве проекта, а не на диске. Разделитель пути остаётся тем, каким его написал человек.
/// </para>
/// <para>
/// Чистая функция над <see cref="XDocument"/>: чтение, запись и откат — у того, кто зовёт, а
/// решения — здесь, где их можно проверить без диска.
/// </para>
/// </remarks>
internal static class ItemReferences
{
    private static readonly string[] Identity = ["Include", "Update", "Remove"];

    /// <summary>Переписывает документ на месте.</summary>
    /// <param name="document">Документ файла проекта, прочитанный с сохранением пробелов.</param>
    /// <param name="projectDirectory">Папка проекта: от неё считаются относительные пути.</param>
    /// <param name="changes">Что куда переехало и что удалено.</param>
    /// <returns>Поменялось ли что-нибудь.</returns>
    public static bool Rewrite(XDocument document, string projectDirectory, IReadOnlyList<PathChange> changes)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(projectDirectory);
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0 || document.Root is null)
            return false;

        var changed = false;

        foreach (var item in Items(document).ToList())
        {
            var before = Own(item, projectDirectory);
            var gone = false;

            foreach (var name in Identity)
            {
                if (item.Attribute(name) is not { } attribute)
                    continue;

                var value = List(attribute.Value, projectDirectory, changes);

                if (value is null)
                    continue;

                changed = true;

                if (value.Length == 0)
                {
                    gone = true;
                    break;
                }

                attribute.Value = value;
            }

            if (gone)
            {
                Remove(item);
                continue;
            }

            changed |= Parent(item, before, Own(item, projectDirectory), changes);
        }

        return changed;
    }

    /// <summary>Элементы групп — на любой глубине: группы бывают и внутри <c>Choose</c>.</summary>
    private static IEnumerable<XElement> Items(XDocument document) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == "ItemGroup")
            .SelectMany(group => group.Elements());

    /// <summary>
    /// Переписывает список путей.
    /// </summary>
    /// <returns>Новое значение; null — список не задет, и его написание остаётся прежним.</returns>
    private static string? List(string value, string directory, IReadOnlyList<PathChange> changes)
    {
        var kept = new List<string>();
        var touched = false;

        foreach (var raw in value.Split(';'))
        {
            var entry = raw.Trim();

            if (entry.Length == 0 || Unresolved(entry))
            {
                if (entry.Length > 0)
                    kept.Add(raw);

                continue;
            }

            var rewritten = Entry(entry, directory, changes);

            if (rewritten is null)
            {
                touched = true;
                continue;
            }

            if (!string.Equals(rewritten, entry, StringComparison.Ordinal))
            {
                touched = true;
                kept.Add(rewritten);
                continue;
            }

            kept.Add(raw);
        }

        return touched ? string.Join(';', kept) : null;
    }

    /// <summary>Переписывает один путь; null — путь удалён.</summary>
    private static string? Entry(string entry, string directory, IReadOnlyList<PathChange> changes)
    {
        var separator = entry.Contains('/') && !entry.Contains('\\') ? '/' : '\\';
        var wildcard = entry.IndexOfAny(['*', '?']);

        if (wildcard >= 0)
        {
            // У маски переписывается только буквальная часть — папка до первого знака маски.
            var cut = entry.LastIndexOfAny(['\\', '/'], wildcard);

            if (cut < 0 || Full(directory, entry[..cut]) is not { } folder)
                return entry;

            foreach (var change in changes)
            {
                if (change is { IsDirectory: true, To: { } to } && (PathChange.Same(folder, change.From) || PathChange.Inside(folder, change.From)))
                    return Relative(directory, PathChange.Moved(folder, change.From, to), separator) + entry[cut..];
            }

            return entry;
        }

        if (Full(directory, entry) is not { } path)
            return entry;

        // Папку пишут и с разделителем на конце — так её пишет Visual Studio: <Folder Include="Assets\" />.
        // Разделитель остаётся, как и его вид: переименование пишет запись так, как её написал
        // человек, а не как её отдал разбор пути.
        var trailing = entry.EndsWith('\\') || entry.EndsWith('/') ? separator.ToString() : string.Empty;

        return PathChange.Map(path, changes) switch
        {
            { Found: false } => entry,
            { To: null } => null,
            { To: { } moved } => Relative(directory, moved, separator).TrimEnd('\\', '/') + trailing,
        };
    }

    /// <summary>
    /// Переписывает <c>DependentUpon</c>: зависимый файл указывает на владельца от своей папки, и
    /// переехать мог каждый из двух.
    /// </summary>
    /// <param name="item">Элемент.</param>
    /// <param name="before">Путь элемента до правки.</param>
    /// <param name="after">Путь элемента после правки.</param>
    /// <param name="changes">Правки.</param>
    private static bool Parent(XElement item, string? before, string? after, IReadOnlyList<PathChange> changes)
    {
        if (before is null || after is null)
            return false;

        var oldFolder = Path.GetDirectoryName(before);
        var newFolder = Path.GetDirectoryName(after);

        if (oldFolder is null || newFolder is null)
            return false;

        var changed = false;

        foreach (var holder in Holders(item))
        {
            var value = holder.Value.Trim();

            if (value.Length == 0 || Unresolved(value) || Full(oldFolder, value) is not { } owner)
                continue;

            var mapped = PathChange.Map(owner, changes);

            // Не переехали ни владелец, ни сам элемент — запись не трогается, как бы она ни была
            // написана: правка не вправе переписывать то, чего не касалась.
            if (!mapped.Found && PathChange.Same(before, after))
                continue;

            // Владелец удалён — зависимость остаётся как есть: она ни на что не укажет, но и не соврёт.
            if (mapped is { Found: true, To: null })
                continue;

            var target = mapped.Found ? mapped.To! : owner;
            var separator = value.Contains('/') && !value.Contains('\\') ? '/' : '\\';
            var rewritten = Relative(newFolder, target, separator);

            if (!string.Equals(rewritten, value, StringComparison.Ordinal))
            {
                holder.Value = rewritten;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Где записан <c>DependentUpon</c>: атрибутом или вложенным элементом.</summary>
    private static IEnumerable<Holder> Holders(XElement item)
    {
        if (item.Attribute("DependentUpon") is { } attribute)
            yield return new Holder(() => attribute.Value, text => attribute.Value = text);

        foreach (var element in item.Elements().Where(element => element.Name.LocalName == "DependentUpon"))
            yield return new Holder(() => element.Value, text => element.Value = text);
    }

    /// <summary>Путь элемента — первый буквальный путь его <c>Include</c> или <c>Update</c>.</summary>
    private static string? Own(XElement item, string directory)
    {
        foreach (var name in (string[])["Include", "Update"])
        {
            if (item.Attribute(name)?.Value is not { } value)
                continue;

            var entry = value.Split(';').Select(part => part.Trim()).FirstOrDefault(part => part.Length > 0);

            if (entry is null || Unresolved(entry) || entry.IndexOfAny(['*', '?']) >= 0)
                return null;

            return Full(directory, entry);
        }

        return null;
    }

    /// <summary>Убирает элемент вместе с отступом перед ним: иначе на его месте осталась бы пустая строка.</summary>
    private static void Remove(XElement item)
    {
        if (item.PreviousNode is XText { } text && string.IsNullOrWhiteSpace(text.Value))
            text.Remove();

        item.Remove();
    }

    private static bool Unresolved(string entry) =>
        entry.Contains("$(", StringComparison.Ordinal)
        || entry.Contains("@(", StringComparison.Ordinal)
        || entry.Contains("%(", StringComparison.Ordinal);

    private static string? Full(string directory, string relative)
    {
        try
        {
            return Path.GetFullPath(relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar), directory);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string Relative(string directory, string path, char separator) =>
        Path.GetRelativePath(directory, path)
            .Replace(Path.DirectorySeparatorChar, separator)
            .Replace(Path.AltDirectorySeparatorChar, separator);

    /// <summary>Место, где записан <c>DependentUpon</c>.</summary>
    private sealed class Holder(Func<string> read, Action<string> write)
    {
        public string Value
        {
            get => read();
            set => write(value);
        }
    }
}
