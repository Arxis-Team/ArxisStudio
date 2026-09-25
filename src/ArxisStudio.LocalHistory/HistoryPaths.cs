namespace ArxisStudio.LocalHistory;

/// <summary>
/// Как история сравнивает пути: без регистра, а «внутри папки» — одним правилом.
/// </summary>
/// <remarks>
/// Правило «путь внутри папки» жило двумя копиями — у ревизий папки и у известных состояний под ней,
/// — и копии расходились на разделителе. Одна не узнавала папку с разделителем на конце, другая после
/// папки ждала только основной разделитель. Теперь папка та же и с разделителем на конце любого вида,
/// а после неё годится любой из двух.
/// </remarks>
internal static class HistoryPaths
{
    /// <summary>Один ли это путь: регистр не в счёт.</summary>
    /// <param name="left">Один путь.</param>
    /// <param name="right">Другой.</param>
    public static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>Лежит ли путь внутри папки, на любой глубине.</summary>
    /// <param name="path">Путь.</param>
    /// <param name="folder">Папка — с разделителем на конце или без.</param>
    public static bool Inside(string path, string folder)
    {
        var root = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return path.Length > root.Length
               && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               && (path[root.Length] == Path.DirectorySeparatorChar || path[root.Length] == Path.AltDirectorySeparatorChar);
    }
}
