using System.Globalization;
using ArxisStudio.Services;
using Avalonia.Data.Converters;

namespace ArxisStudio.Welcome;

/// <summary>
/// Превращает путь к значку плагина в картинку для карточки.
/// </summary>
/// <remarks>
/// Разметка привязана к записи каталога, а запись знает о значке только путь:
/// растр — забота рисующего, а не модели. Не прочиталось — вернётся null, и
/// на карточке останется общий значок.
/// </remarks>
public sealed class PluginIconConverter : IValueConverter
{
    /// <summary>Общий экземпляр для разметки.</summary>
    public static PluginIconConverter Instance { get; } = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        PluginIcons.Instance.Of(value as string);

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Значок плагина обратно в путь не превращается");
}
