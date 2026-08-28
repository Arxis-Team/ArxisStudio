namespace ArxisStudio.Shell.Settings;

/// <summary>Вариант оформления студии.</summary>
public enum StudioTheme
{
    /// <summary>Тёмная тема.</summary>
    Dark,

    /// <summary>Светлая тема.</summary>
    Light,
}

/// <summary>Плотность интерфейса.</summary>
public enum StudioDensity
{
    /// <summary>Компактная — значения по умолчанию из дизайн-спецификации.</summary>
    Compact,

    /// <summary>Обычная — увеличенные отступы.</summary>
    Regular,
}

/// <summary>
/// Настройки студии: то, что переживает перезапуск и правится на экране Settings.
/// Простая изменяемая модель — её сериализует <see cref="JsonSettingsStore"/>.
/// </summary>
public sealed class StudioSettings
{
    /// <summary>Оформление.</summary>
    public StudioTheme Theme { get; set; } = StudioTheme.Dark;

    /// <summary>Акцентный цвет в формате <c>#RRGGBB</c>.</summary>
    public string AccentColor { get; set; } = "#3574F0";

    /// <summary>Плотность интерфейса.</summary>
    public StudioDensity Density { get; set; } = StudioDensity.Compact;

    /// <summary>
    /// Язык интерфейса: код культуры, например <c>en</c> или <c>ru</c>.
    /// </summary>
    /// <remarks>
    /// При первом запуске — английский: это язык, на котором написана студия,
    /// и на него же падает всё непереведённое. Выбранный однажды язык лежит
    /// здесь и переживает обновления.
    /// </remarks>
    public string Language { get; set; } = Localization.Localizer.FallbackLanguage;

    /// <summary>Показывать сетку на канве дизайнера.</summary>
    public bool ShowCanvasGrid { get; set; } = true;

    /// <summary>Автосохранение документов.</summary>
    public bool AutoSave { get; set; } = true;

    /// <summary>Подсказки в дизайнере форм.</summary>
    public bool DesignerHints { get; set; } = true;

    /// <summary>Открывать последний проект при запуске.</summary>
    public bool OpenLastProject { get; set; }

    /// <summary>Канал обновлений.</summary>
    public string UpdateChannel { get; set; } = "Stable";
}
