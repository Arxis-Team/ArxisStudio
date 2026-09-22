using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Project.Model;

/// <summary>
/// Перетаскивание из проводника: что принесли, куда это ляжет и какая строка изображает цель.
/// </summary>
/// <remarks>
/// <para>
/// <b>Что.</b> Файлы и каталоги с путями на диске — как вставка чужого буфера. Элемент без
/// локального пути (письмо из почтовой программы, файл внутри архива в проводнике) и путь, которого
/// на диске уже нет, не берутся: копировать нечего.
/// </para>
/// <para>
/// <b>Куда.</b> В каталог узла, как вставка (<see cref="Pasting.Folder"/>): каталог — в него самого,
/// проект — в каталог проекта, файл — в свой каталог. Решению, папке решения и зависимостям класть
/// некуда. Каталог нельзя положить в него самого или глубже — ни копией, ни переносом.
/// </para>
/// <para>
/// <b>Чем.</b> Принесённое из проводника — только копией, как у Rider и Unity: оно лежит вне
/// решения, и перенос удалял бы его там, куда история студии не дотягивается, — отменить такое было
/// бы нечем. Перенесённое внутри окна переносится, а с Ctrl копируется: и то и другое история знает.
/// Перенос туда, где всё несомое уже лежит, — не цель: переносить нечего.
/// </para>
/// </remarks>
internal static class Dropping
{
    /// <summary>Единицы копирования из путей, которые принёс проводник; повторы и пропавшее отброшены.</summary>
    /// <param name="paths">Локальные пути принесённого.</param>
    public static IReadOnlyList<ClipItem> Items(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var items = new List<ClipItem>();
        var seen = new HashSet<CanonicalPath>();

        foreach (var local in paths)
        {
            if (!CanonicalPath.TryCreate(local, out var path) || !seen.Add(path))
                continue;

            if (Directory.Exists(path.Value))
                items.Add(new ClipItem(path, IsFolder: true, []));
            else if (File.Exists(path.Value))
                items.Add(new ClipItem(path, IsFolder: false, []));
        }

        return items;
    }

    /// <summary>
    /// Ляжет ли несомое в каталог: ни один несомый каталог не он сам и не его предок, а перенос
    /// сдвинет хоть что-то.
    /// </summary>
    /// <param name="items">Несомое.</param>
    /// <param name="folder">Каталог назначения.</param>
    /// <param name="mode">Перенос или копия.</param>
    public static bool Fits(IReadOnlyList<ClipItem> items, CanonicalPath folder, ClipMode mode = ClipMode.Copy)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items.Count > 0
            && !items.Any(item => Pasting.IntoItself(item, folder))
            && (mode == ClipMode.Copy || items.Any(item => !Pasting.SameFolder(item, folder)));
    }

    /// <summary>Изображает ли узел каталог назначения: строка каталога или проекта, чей каталог — он.</summary>
    /// <param name="node">Узел строки или плитки.</param>
    /// <param name="folder">Каталог назначения.</param>
    /// <remarks>
    /// Файл каталог не изображает: цель, выбранная над файлом, — его каталог, и отмечается строка
    /// каталога, а не файла, над которым курсор. Так видно, куда ляжет принесённое.
    /// </remarks>
    public static bool Holds(Node node, CanonicalPath folder)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Kind is NodeKind.Folder or NodeKind.Project && Pasting.Folder(node) == folder;
    }
}
