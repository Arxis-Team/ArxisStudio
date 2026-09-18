using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Цвет значка по ключу кисти темы.
/// </summary>
/// <remarks>
/// Ключ, а не кисть: строка дерева не знает темы, и цвет, взятый кистью однажды, остался бы
/// прежним после переключения на светлую. Здесь цвет привязан к ресурсу и идёт за темой сам.
/// Без ключа значок идёт вторичным цветом подписи — как значок строки в дереве темы.
/// </remarks>
internal static class Tint
{
    /// <summary>Ключ кисти; пусто — вторичный цвет подписи.</summary>
    public static readonly AttachedProperty<string?> KeyProperty =
        AvaloniaProperty.RegisterAttached<TemplatedControl, string?>("Key", typeof(Tint));

    private const string Plain = "AxTextSecondaryBrush";

    private static readonly AttachedProperty<IDisposable?> BindingProperty =
        AvaloniaProperty.RegisterAttached<TemplatedControl, IDisposable?>("Binding", typeof(Tint));

    static Tint() => KeyProperty.Changed.AddClassHandler<TemplatedControl>(OnKeyChanged);

    /// <summary>Ключ кисти контрола.</summary>
    /// <param name="control">Контрол.</param>
    public static string? GetKey(TemplatedControl control) => control.GetValue(KeyProperty);

    /// <summary>Ставит ключ кисти контролу.</summary>
    /// <param name="control">Контрол.</param>
    /// <param name="value">Ключ; пусто — вторичный цвет подписи.</param>
    public static void SetKey(TemplatedControl control, string? value) => control.SetValue(KeyProperty, value);

    private static void OnKeyChanged(TemplatedControl control, AvaloniaPropertyChangedEventArgs change)
    {
        control.GetValue(BindingProperty)?.Dispose();

        var key = change.GetNewValue<string?>() ?? Plain;
        var binding = control.Bind(TemplatedControl.ForegroundProperty, control.GetResourceObservable(key), BindingPriority.Style);

        control.SetValue(BindingProperty, binding);
    }
}
