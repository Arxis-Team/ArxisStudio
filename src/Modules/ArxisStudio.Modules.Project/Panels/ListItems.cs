using ArxisStudio.Controls;
using Avalonia;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Строки дерева и плитки колонки в их списках: что выбрано, где первое выбранное и в чём пришлось событие.
/// </summary>
/// <remarks>
/// Дерево и колонка спрашивали одно и то же каждое своей копией — строками и плитками.
/// </remarks>
internal static class ListItems
{
    /// <summary>Выбранное в списке — тем типом, каким список его держит.</summary>
    /// <typeparam name="T">Строка или плитка.</typeparam>
    /// <param name="list">Список.</param>
    public static IEnumerable<T> Of<T>(AxListBox list) => list.SelectedItems?.OfType<T>() ?? [];

    /// <summary>Место первого выбранного среди показанного; −1 — выбора нет.</summary>
    /// <typeparam name="T">Строка или плитка.</typeparam>
    /// <param name="list">Список.</param>
    /// <param name="shown">Что он показывает, по порядку.</param>
    public static int FirstIndex<T>(AxListBox list, IList<T> shown) =>
        Of<T>(list).Select(shown.IndexOf).Where(at => at >= 0).DefaultIfEmpty(-1).Min();

    /// <summary>Строка или плитка, в которой пришлось событие; пусто — мимо них.</summary>
    /// <typeparam name="T">Строка или плитка.</typeparam>
    /// <param name="source">Источник события.</param>
    public static T? At<T>(object? source)
        where T : class =>
        (source as Visual)?.FindAncestorOfType<AxListBoxItem>(includeSelf: true)?.DataContext as T;
}
