namespace ArxisStudio.Shell.Settings;

/// <summary>Вариант оформления студии.</summary>
public enum StudioTheme
{
    /// <summary>Тёмная тема.</summary>
    Dark,

    /// <summary>Светлая тема.</summary>
    Light,
}

/// <summary>
/// Настройки студии: то, что переживает перезапуск и правится в окне настроек.
/// Простая изменяемая модель — её сериализует <see cref="JsonSettingsStore"/>.
/// </summary>
/// <remarks>
/// Здесь только то, за чем стоит читатель. Акцентный цвет и плотность
/// интерфейса лежали в модели и в файле с первого дня, но не значили ничего:
/// цвет и отступы приходят из темы, а настройка, которую можно поправить и не
/// увидеть разницы, — обещание, которого никто не давал. Понадобятся — вернутся
/// вместе с тем, кто их читает.
/// </remarks>
public sealed class StudioSettings
{
    /// <summary>Оформление.</summary>
    public StudioTheme Theme { get; set; } = StudioTheme.Dark;

    /// <summary>
    /// Язык интерфейса: код культуры, например <c>en</c> или <c>ru</c>.
    /// </summary>
    /// <remarks>
    /// При первом запуске — английский: это язык, на котором написана студия,
    /// и на него же падает всё непереведённое. Выбранный однажды язык лежит
    /// здесь и переживает обновления.
    /// </remarks>
    public string Language { get; set; } = Localization.Localizer.FallbackLanguage;
}
