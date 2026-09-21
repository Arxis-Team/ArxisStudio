using System.Globalization;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Project.Model;

/// <summary>Вырезано или скопировано.</summary>
internal enum ClipMode
{
    /// <summary>Вырезано: вставка переносит.</summary>
    Cut,

    /// <summary>Скопировано: вставка копирует.</summary>
    Copy,
}

/// <summary>Единица буфера: файл или папка и то, что вложено в файл, — они едут вместе.</summary>
/// <param name="Root">Файл или папка.</param>
/// <param name="IsFolder">Это папка.</param>
/// <param name="Nested">Вложенные в файл.</param>
internal sealed record ClipItem(CanonicalPath Root, bool IsFolder, IReadOnlyList<CanonicalPath> Nested);

/// <summary>Что лежит в буфере правки.</summary>
/// <param name="Mode">Вырезано или скопировано.</param>
/// <param name="Items">Единицы — в том порядке, в каком их взяла правка.</param>
internal sealed record FileClip(ClipMode Mode, IReadOnlyList<ClipItem> Items)
{
    /// <summary>Все пути буфера: корни и вложенные.</summary>
    public IEnumerable<CanonicalPath> Paths => Items.SelectMany(item => item.Nested.Prepend(item.Root));

    /// <summary>Буфер из того, что взяла правка.</summary>
    /// <param name="mode">Вырезано или скопировано.</param>
    /// <param name="selection">Выбор.</param>
    public static FileClip Of(ClipMode mode, EditSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return new FileClip(mode,
        [
            .. selection.Roots.Select(root => new ClipItem(
                root.Path,
                root.Kind == NodeKind.Folder,
                [.. EditSelection.Nested(root).Select(nested => nested.Path)])),
        ]);
    }
}

/// <summary>
/// Вставка: куда, под каким именем и с чем она столкнётся.
/// </summary>
/// <remarks>
/// <para>
/// <b>Куда.</b> В папку строки, на которой вставляют: папка — в неё саму, файл — в его папку,
/// проект — в папку проекта. Решению, папке решения и зависимостям вставлять некуда.
/// </para>
/// <para>
/// <b>Под каким именем.</b> Под своим. Копия в ту же папку сразу получает имя с номером, как в
/// проводнике при «Оставить оба»: <c>MainWindow (2).axaml</c>, и вложенный идёт за ним —
/// <c>MainWindow (2).axaml.cs</c>. Номер числом, а не словом «копия»: имя файла живёт в репозитории
/// и не должно зависеть от языка студии.
/// </para>
/// </remarks>
internal static class Pasting
{
    /// <summary>Папка, в которую вставляют на этом узле; пусто — вставлять некуда.</summary>
    /// <param name="node">Узел строки или плитки.</param>
    public static CanonicalPath? Folder(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Path.IsEmpty)
            return null;

        return node.Kind switch
        {
            NodeKind.Folder => node.Path,
            NodeKind.File or NodeKind.Project => node.Path.Directory,
            _ => null,
        };
    }

    /// <summary>Имя с номером: у файла номер перед расширением, у папки — в конце.</summary>
    /// <param name="name">Имя.</param>
    /// <param name="folder">Это папка.</param>
    /// <param name="number">Номер, с двух.</param>
    public static string Numbered(string name, bool folder, int number)
    {
        ArgumentNullException.ThrowIfNull(name);

        var suffix = string.Create(CultureInfo.InvariantCulture, $" ({number})");

        if (folder)
            return name + suffix;

        var stem = Renaming.Stem(name);

        return stem + suffix + name[stem.Length..];
    }

    /// <summary>Пары вставки единицы в папку под именем корня; вложенные идут за корнем.</summary>
    /// <param name="item">Единица.</param>
    /// <param name="folder">Папка назначения.</param>
    /// <param name="name">Имя корня на месте.</param>
    public static IReadOnlyList<FileMove> Pairs(ClipItem item, CanonicalPath folder, string name)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(name);

        var owner = item.Root.FileName;
        var pairs = new List<FileMove> { new(item.Root, folder.Combine(name)) };

        foreach (var nested in item.Nested)
        {
            var renamed = string.Equals(owner, name, StringComparison.Ordinal)
                ? nested.FileName
                : Renaming.Companion(owner, name, nested.FileName) ?? nested.FileName;

            pairs.Add(new FileMove(nested, folder.Combine(renamed)));
        }

        return pairs;
    }

    /// <summary>Свободное имя с номером — и для корня, и для всего, что едет с ним.</summary>
    /// <param name="item">Единица.</param>
    /// <param name="folder">Папка назначения.</param>
    /// <param name="exists">Есть ли на диске такой путь.</param>
    public static string Free(ClipItem item, CanonicalPath folder, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(exists);

        for (var number = 2; ; number++)
        {
            var name = Numbered(item.Root.FileName, item.IsFolder, number);

            if (Pairs(item, folder, name).All(pair => !exists(pair.To.Value)))
                return name;
        }
    }

    /// <summary>Занято ли место: корень или что-то из того, что едет с ним.</summary>
    /// <param name="item">Единица.</param>
    /// <param name="folder">Папка назначения.</param>
    /// <param name="exists">Есть ли на диске такой путь.</param>
    public static bool Taken(ClipItem item, CanonicalPath folder, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(exists);

        return Pairs(item, folder, item.Root.FileName).Any(pair => exists(pair.To.Value));
    }

    /// <summary>Вставляют ли туда же, откуда взяли.</summary>
    /// <param name="item">Единица.</param>
    /// <param name="folder">Папка назначения.</param>
    public static bool SameFolder(ClipItem item, CanonicalPath folder)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Root.Directory == folder;
    }

    /// <summary>Папку вставляют в неё саму или глубже — этого не сделать ни переносом, ни копией.</summary>
    /// <param name="item">Единица.</param>
    /// <param name="folder">Папка назначения.</param>
    public static bool IntoItself(ClipItem item, CanonicalPath folder)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.IsFolder && (folder == item.Root || folder.StartsWith(item.Root));
    }
}
