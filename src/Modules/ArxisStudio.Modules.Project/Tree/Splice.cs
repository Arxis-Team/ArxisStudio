using Avalonia.Collections;

namespace ArxisStudio.Modules.Project.Tree;

/// <summary>
/// Приводит показанный список к новому одной правкой середины.
/// </summary>
/// <remarks>
/// Общее начало и общий конец сравниваются по ссылке: строка, оставшаяся на месте, остаётся тем же
/// объектом, и всё, что между ними, уходит одним удалением и приходит одной вставкой. Список
/// получает одно-два события вместо сброса, и выделение с прокруткой не теряются. Правило одно для
/// дерева и для правой колонки — поэтому оно и вынесено.
/// </remarks>
internal static class Splice
{
    /// <summary>Приводит список к новому.</summary>
    /// <typeparam name="T">Строка — объект, узнаваемый по ссылке.</typeparam>
    /// <param name="list">Показанный список.</param>
    /// <param name="next">Каким он должен стать; строки, оставшиеся от прежнего, — те же объекты.</param>
    public static void Into<T>(AvaloniaList<T> list, IReadOnlyList<T> next)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(next);

        var prefix = 0;

        while (prefix < list.Count && prefix < next.Count && ReferenceEquals(list[prefix], next[prefix]))
            prefix++;

        var suffix = 0;

        while (suffix < list.Count - prefix
               && suffix < next.Count - prefix
               && ReferenceEquals(list[list.Count - 1 - suffix], next[next.Count - 1 - suffix]))
        {
            suffix++;
        }

        var removed = list.Count - prefix - suffix;

        if (removed > 0)
            list.RemoveRange(prefix, removed);

        var inserted = next.Count - prefix - suffix;

        if (inserted > 0)
            list.InsertRange(prefix, next.Skip(prefix).Take(inserted));
    }
}
