using ArxisStudio.Controls;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Shell;

/// <summary>
/// Кнопка полосы: обычная кнопка студии, умеющая быть включённой.
/// </summary>
/// <remarks>
/// Включённость инструмента тема записывает псевдоклассом <c>:selected</c>, а
/// своего свойства у кнопки под это нет — «включённость знает приложение, а не
/// контрол». Полоса и есть то приложение: здесь состояние становится свойством,
/// которое можно поставить из реестра, а псевдокласс за ним следует.
/// <para>
/// Тему кнопка берёт у <see cref="AxButton"/>: наследник без этой оговорки
/// искал бы тему по своему типу и остался бы без неё.
/// </para>
/// </remarks>
public sealed class ToolBarButton : AxButton
{
    /// <summary>Включён ли инструмент, который представляет кнопка.</summary>
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<ToolBarButton, bool>(nameof(IsChecked));

    /// <inheritdoc cref="IsCheckedProperty"/>
    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(AxButton);

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsCheckedProperty)
            PseudoClasses.Set(":selected", change.GetNewValue<bool>());
    }
}
