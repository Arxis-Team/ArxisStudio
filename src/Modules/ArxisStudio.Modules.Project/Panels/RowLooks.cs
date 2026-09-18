using System.Globalization;
using ArxisStudio.Modules.Project.Looks;
using ArxisStudio.Modules.Project.Model;
using Avalonia;
using Avalonia.Data.Converters;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Отступ строки: глубина, умноженная на шаг лестницы дерева.
/// </summary>
/// <remarks>
/// Шаг берётся у ячейки шеврона, а не числом: ширина ячейки — ключ темы <c>AxTreeIndent</c>, и
/// при смене плотности отступ идёт за ним сам, как идёт у дерева темы.
/// </remarks>
internal sealed class Indent : IMultiValueConverter
{
    /// <summary>Единственный экземпляр — состояния у преобразователя нет.</summary>
    public static Indent Instance { get; } = new();

    /// <inheritdoc/>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [int depth, double step, ..] && !double.IsNaN(step)
            ? new Thickness(depth * step, 0, 0, 0)
            : new Thickness(0);
}

/// <summary>Значок строки: по узлу и по тому, раскрыт ли он.</summary>
internal sealed class Glyph : IMultiValueConverter
{
    /// <summary>Единственный экземпляр — состояния у преобразователя нет.</summary>
    public static Glyph Instance { get; } = new();

    /// <inheritdoc/>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [Node node, bool expanded, ..] ? Glyphs.Of(node, expanded) : null;
}

/// <summary>Значок узла свёрнутым: для подложки плитки, строки списка и сегмента крошек.</summary>
internal sealed class NodeGlyph : IValueConverter
{
    /// <summary>Единственный экземпляр — состояния у преобразователя нет.</summary>
    public static NodeGlyph Instance { get; } = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Node node ? Glyphs.Of(node, expanded: false) : null;

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Силуэт плитки по узлу.</summary>
internal sealed class Silhouette : IValueConverter
{
    /// <summary>Единственный экземпляр — состояния у преобразователя нет.</summary>
    public static Silhouette Instance { get; } = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Node node ? Glyphs.TileOf(node) : null;

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Ключ цвета значка строки.</summary>
internal sealed class TintKey : IValueConverter
{
    /// <summary>Единственный экземпляр — состояния у преобразователя нет.</summary>
    public static TintKey Instance { get; } = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Node node ? Glyphs.TintOf(node) : null;

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
