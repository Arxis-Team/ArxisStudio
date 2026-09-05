using System.Globalization;
using ArxisStudio.Shell.Localization;
using Avalonia.Data.Converters;

namespace ArxisStudio.Converters;

/// <summary>
/// Надпись на кнопке плагина: она предлагает действие, а не сообщает состояние,
/// поэтому у включённого плагина написано «Выключить».
/// </summary>
public sealed class PluginToggleConverter : IValueConverter
{
    /// <summary>Общий экземпляр для разметки.</summary>
    public static PluginToggleConverter Instance { get; } = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Localizer.Instance["plugins.disable"] : Localizer.Instance["plugins.enable"];

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
