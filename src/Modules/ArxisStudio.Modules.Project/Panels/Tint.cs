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
/// Без ключа значок идёт вторичным цветом подписи — как значок строки в дереве темы, а приглушённый
/// — вырезанный и ждущий вставки — цветом недоступного, какой бы ключ у него ни был.
/// </remarks>
internal static class Tint
{
    /// <summary>Ключ кисти; пусто — вторичный цвет подписи.</summary>
    public static readonly AttachedProperty<string?> KeyProperty =
        AvaloniaProperty.RegisterAttached<TemplatedControl, string?>("Key", typeof(Tint));

    /// <summary>Приглушён ли значок: вырезан и ждёт вставки.</summary>
    public static readonly AttachedProperty<bool> MutedProperty =
        AvaloniaProperty.RegisterAttached<TemplatedControl, bool>("Muted", typeof(Tint));

    private const string Plain = "AxTextSecondaryBrush";

    private const string Dim = "AxTextDisabledBrush";

    private static readonly AttachedProperty<IDisposable?> BindingProperty =
        AvaloniaProperty.RegisterAttached<TemplatedControl, IDisposable?>("Binding", typeof(Tint));

    static Tint()
    {
        KeyProperty.Changed.AddClassHandler<TemplatedControl>(OnChanged);
        MutedProperty.Changed.AddClassHandler<TemplatedControl>(OnChanged);
    }

    /// <summary>Ключ кисти контрола.</summary>
    /// <param name="control">Контрол.</param>
    public static string? GetKey(TemplatedControl control) => control.GetValue(KeyProperty);

    /// <summary>Ставит ключ кисти контролу.</summary>
    /// <param name="control">Контрол.</param>
    /// <param name="value">Ключ; пусто — вторичный цвет подписи.</param>
    public static void SetKey(TemplatedControl control, string? value) => control.SetValue(KeyProperty, value);

    /// <summary>Приглушён ли значок контрола.</summary>
    /// <param name="control">Контрол.</param>
    public static bool GetMuted(TemplatedControl control) => control.GetValue(MutedProperty);

    /// <summary>Приглушает значок контрола или снимает приглушение.</summary>
    /// <param name="control">Контрол.</param>
    /// <param name="value">Приглушить.</param>
    public static void SetMuted(TemplatedControl control, bool value) => control.SetValue(MutedProperty, value);

    private static void OnChanged(TemplatedControl control, AvaloniaPropertyChangedEventArgs change)
    {
        control.GetValue(BindingProperty)?.Dispose();

        var key = control.GetValue(MutedProperty) ? Dim : control.GetValue(KeyProperty) ?? Plain;
        var binding = control.Bind(TemplatedControl.ForegroundProperty, control.GetResourceObservable(key), BindingPriority.Style);

        control.SetValue(BindingProperty, binding);
    }
}
