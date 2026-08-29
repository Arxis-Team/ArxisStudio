using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ArxisStudio.Welcome;

/// <summary>
/// Красит подпись зависимости: обычную — приглушённо, проблемную — жёлтым.
/// </summary>
/// <remarks>
/// Кисти берутся из ресурсов темы, а не пишутся цветом: карточка обязана
/// выглядеть своей и на светлой теме, и на тёмной.
/// </remarks>
public sealed class DependencyProblemBrushConverter : IValueConverter
{
    /// <summary>Общий экземпляр для разметки.</summary>
    public static DependencyProblemBrushConverter Instance { get; } = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is true ? "AxYelBrush" : "AxFg2Brush";

        return Application.Current is { } application &&
               application.TryFindResource(key, application.ActualThemeVariant, out var brush)
            ? brush as IBrush
            : null;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Кисть обратно в признак не превращается");
}
