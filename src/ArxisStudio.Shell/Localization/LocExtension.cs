using Avalonia.Data;

namespace ArxisStudio.Shell.Localization;

/// <summary>
/// Разметочное расширение для строк интерфейса: <c>Text="{loc:Loc projects.open}"</c>.
/// Возвращает привязку к <see cref="LocalizedString.Value"/>, поэтому смена языка
/// перерисовывает уже показанный текст.
/// </summary>
public sealed class LocExtension
{
    /// <summary>Создаёт расширение без ключа — ключ задаётся свойством.</summary>
    public LocExtension()
    {
    }

    /// <summary>Создаёт расширение с ключом строки.</summary>
    /// <param name="key">Ключ в словаре локализации.</param>
    public LocExtension(string key) => Key = key;

    /// <summary>Ключ строки.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Возвращает привязку к строке текущего языка.</summary>
    /// <param name="serviceProvider">Контекст разметки; не используется.</param>
    public Binding ProvideValue(IServiceProvider serviceProvider) =>
        new(nameof(LocalizedString.Value))
        {
            Source = Localizer.Instance.Track(Key),
            Mode = BindingMode.OneWay,
        };
}
