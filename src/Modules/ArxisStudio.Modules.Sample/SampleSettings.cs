using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace ArxisStudio.Modules.Sample;

/// <summary>Тон приветствия — тон текста темы: цвет назначает тема, а не настройка.</summary>
public enum GreetingTone
{
    /// <summary>Основной.</summary>
    Primary,

    /// <summary>Вторичный.</summary>
    Secondary,

    /// <summary>Акцентный.</summary>
    Accent,
}

/// <summary>
/// Настройки панели образца — объект, который образец показывает в инспекторе студии (<c>IStudioInspector.Inspect</c>).
/// </summary>
/// <remarks>
/// Обычный объект CLR, без единого типа инспектора: свойства с чтением и записью, атрибуты .NET — раздел, границы,
/// значение по умолчанию — и <see cref="INotifyPropertyChanged"/>, по которому о перемене узнают и панель, и
/// инспектор. Подписи и пояснения дописывает таблица метаданных образца (<c>SampleMetadata</c>): атрибут строку не
/// переведёт, а словарь модуля — переведёт.
/// </remarks>
public sealed class SampleSettings : INotifyPropertyChanged
{
    /// <summary>Раздел настроек — его описывает таблица метаданных образца.</summary>
    public const string Category = "sample";

    private string _greeting = "Arxis";
    private int _repeat = 1;
    private GreetingTone _tone;
    private bool _loud;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Кого панель приветствует.</summary>
    [Category(Category)]
    [DefaultValue("Arxis")]
    public string Greeting
    {
        get => _greeting;
        set => Set(ref _greeting, value ?? string.Empty);
    }

    /// <summary>Сколько раз.</summary>
    [Category(Category)]
    [Range(1, 5)]
    [DefaultValue(1)]
    public int Repeat
    {
        get => _repeat;
        set => Set(ref _repeat, Math.Clamp(value, 1, 5));
    }

    /// <summary>Тон приветствия.</summary>
    [Category(Category)]
    [DefaultValue(GreetingTone.Primary)]
    public GreetingTone Tone
    {
        get => _tone;
        set => Set(ref _tone, value);
    }

    /// <summary>Громко — полужирным и с восклицанием.</summary>
    [Category(Category)]
    [DefaultValue(false)]
    public bool Loud
    {
        get => _loud;
        set => Set(ref _loud, value);
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
