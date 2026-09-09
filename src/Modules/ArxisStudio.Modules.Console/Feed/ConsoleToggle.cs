using ArxisStudio.Controls;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Console.Feed;

/// <summary>
/// Кнопка панели, умеющая быть нажатой: счётчик уровня, свёртка, автопрокрутка.
/// </summary>
/// <remarks>
/// Включённость тема записывает псевдоклассом <c>:selected</c>, а своего
/// свойства у кнопки студии под это нет — «включённость знает приложение, а не
/// контрол». Панель и есть то приложение.
/// <para>
/// Это построчный повтор <c>ToolBarButton</c> из оболочки студии, и повтор
/// здесь неизбежен: <c>PseudoClasses</c> защищён, поставить его может только
/// наследник, а на <c>ArxisStudio.Shell</c> модуль не ссылается и ссылаться не
/// должен — он живёт тем же контрактом, что и внешний плагин.
/// </para>
/// <para>
/// Тему кнопка берёт у <see cref="AxButton"/>: наследник без этой оговорки
/// искал бы тему по своему типу и остался бы без неё.
/// </para>
/// </remarks>
public sealed class ConsoleToggle : AxButton
{
    /// <summary>Нажата ли кнопка.</summary>
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<ConsoleToggle, bool>(nameof(IsChecked));

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
        ArgumentNullException.ThrowIfNull(change);

        base.OnPropertyChanged(change);

        if (change.Property == IsCheckedProperty)
            PseudoClasses.Set(":selected", change.GetNewValue<bool>());
    }
}
