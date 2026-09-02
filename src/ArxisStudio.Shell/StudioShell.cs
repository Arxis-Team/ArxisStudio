using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Shell;

/// <summary>
/// Каркас окна студии: верхний тулбар, рабочая область
/// (<see cref="ContentControl.Content"/>) и статус-бар.
/// </summary>
/// <remarks>
/// Зон здесь больше нет. Пока раскладку определял разработчик студии, три
/// фиксированных кармана были нормальной ценой; теперь её определяет человек, и
/// каркас отдаёт всю середину дереву доков, ничего о нём не зная. Размеры
/// областей отсюда ушли туда же — в дерево, откуда они попадают в файл и
/// возвращаются при следующем запуске.
/// </remarks>
public class StudioShell : ContentControl
{
    /// <summary>Содержимое верхнего тулбара; высоту задаёт оно само.</summary>
    public static readonly StyledProperty<object?> TopBarProperty =
        AvaloniaProperty.Register<StudioShell, object?>(nameof(TopBar));

    /// <summary>Содержимое статус-бара (24px, высота компактного контрола).</summary>
    public static readonly StyledProperty<object?> StatusBarProperty =
        AvaloniaProperty.Register<StudioShell, object?>(nameof(StatusBar));

    /// <inheritdoc cref="TopBarProperty"/>
    public object? TopBar
    {
        get => GetValue(TopBarProperty);
        set => SetValue(TopBarProperty, value);
    }

    /// <inheritdoc cref="StatusBarProperty"/>
    public object? StatusBar
    {
        get => GetValue(StatusBarProperty);
        set => SetValue(StatusBarProperty, value);
    }
}
