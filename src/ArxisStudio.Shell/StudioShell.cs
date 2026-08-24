using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Shell;

/// <summary>
/// Каркас окна студии: верхний тулбар, зоны tool window (левая, правая, нижняя)
/// со сплиттерами, центральная область (<see cref="ContentControl.Content"/>) и
/// статус-бар. Раскладка и размеры зон — по дизайн-спецификации студии
/// (docs/design-spec.md).
/// </summary>
public class StudioShell : ContentControl
{
    /// <summary>Содержимое верхнего тулбара (42px).</summary>
    public static readonly StyledProperty<object?> TopBarProperty =
        AvaloniaProperty.Register<StudioShell, object?>(nameof(TopBar));

    /// <summary>Содержимое левой зоны (по умолчанию 262px).</summary>
    public static readonly StyledProperty<object?> LeftPaneProperty =
        AvaloniaProperty.Register<StudioShell, object?>(nameof(LeftPane));

    /// <summary>Содержимое правой зоны (по умолчанию 302px).</summary>
    public static readonly StyledProperty<object?> RightPaneProperty =
        AvaloniaProperty.Register<StudioShell, object?>(nameof(RightPane));

    /// <summary>Содержимое нижней зоны (по умолчанию 212px).</summary>
    public static readonly StyledProperty<object?> BottomPaneProperty =
        AvaloniaProperty.Register<StudioShell, object?>(nameof(BottomPane));

    /// <summary>Содержимое статус-бара (26px).</summary>
    public static readonly StyledProperty<object?> StatusBarProperty =
        AvaloniaProperty.Register<StudioShell, object?>(nameof(StatusBar));

    /// <inheritdoc cref="TopBarProperty"/>
    public object? TopBar
    {
        get => GetValue(TopBarProperty);
        set => SetValue(TopBarProperty, value);
    }

    /// <inheritdoc cref="LeftPaneProperty"/>
    public object? LeftPane
    {
        get => GetValue(LeftPaneProperty);
        set => SetValue(LeftPaneProperty, value);
    }

    /// <inheritdoc cref="RightPaneProperty"/>
    public object? RightPane
    {
        get => GetValue(RightPaneProperty);
        set => SetValue(RightPaneProperty, value);
    }

    /// <inheritdoc cref="BottomPaneProperty"/>
    public object? BottomPane
    {
        get => GetValue(BottomPaneProperty);
        set => SetValue(BottomPaneProperty, value);
    }

    /// <inheritdoc cref="StatusBarProperty"/>
    public object? StatusBar
    {
        get => GetValue(StatusBarProperty);
        set => SetValue(StatusBarProperty, value);
    }
}
