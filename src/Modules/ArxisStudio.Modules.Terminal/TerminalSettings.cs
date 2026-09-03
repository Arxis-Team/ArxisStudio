using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Terminal;

/// <summary>
/// Настройки терминала: то, что человек правит в диалоге и что помнит студия.
/// </summary>
/// <remarks>
/// Ключи объявлены в манифесте — студия принимает только их. Значения читаются
/// с проверкой границ: файл настроек правят и руками, а шрифт в ноль пикселей
/// или история в миллиард строк — не то, что стоит принимать молча.
/// </remarks>
/// <param name="Shell">Имя профиля оболочки по умолчанию; пусто — первая из списка.</param>
/// <param name="FontSize">Кегль моноширинного шрифта.</param>
/// <param name="Scrollback">Сколько строк истории помнить сверх экрана.</param>
/// <param name="CursorBlink">Мигает ли курсор.</param>
public sealed record TerminalSettings(string Shell, double FontSize, int Scrollback, bool CursorBlink)
{
    /// <summary>Ключ настройки оболочки по умолчанию.</summary>
    public const string ShellKey = "terminal.shell";

    /// <summary>Ключ настройки кегля.</summary>
    public const string FontSizeKey = "terminal.fontSize";

    /// <summary>Ключ настройки глубины истории.</summary>
    public const string ScrollbackKey = "terminal.scrollback";

    /// <summary>Ключ настройки мигания курсора.</summary>
    public const string CursorBlinkKey = "terminal.cursorBlink";

    /// <summary>Кегль, пока человек не выбрал свой.</summary>
    public const double DefaultFontSize = 13;

    /// <summary>Меньше — не прочитать.</summary>
    public const double MinFontSize = 8;

    /// <summary>Больше — на экране умещается пара строк.</summary>
    public const double MaxFontSize = 40;

    /// <summary>История, пока человек не выбрал свою.</summary>
    public const int DefaultScrollback = 5000;

    /// <summary>Потолок истории: строка — это память на каждую ячейку.</summary>
    public const int MaxScrollback = 100_000;

    /// <summary>Настройки, пока человек ничего не менял.</summary>
    public static TerminalSettings Default { get; } = new(string.Empty, DefaultFontSize, DefaultScrollback, true);

    /// <summary>Все ключи, которые модуль объявляет в манифесте.</summary>
    public static IReadOnlyList<string> Keys { get; } = [ShellKey, FontSizeKey, ScrollbackKey, CursorBlinkKey];

    /// <summary>Читает настройки из студии, подставляя умолчания и границы.</summary>
    /// <param name="settings">Настройки модуля.</param>
    public static TerminalSettings Read(IStudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new TerminalSettings(
            settings.Get<string>(ShellKey) ?? string.Empty,
            ClampFontSize(settings.Get<double>(FontSizeKey)),
            ClampScrollback((int)Math.Round(settings.Get<double>(ScrollbackKey))),
            settings.Get<bool?>(CursorBlinkKey) ?? true);
    }

    /// <summary>Записывает настройки в студию.</summary>
    /// <param name="settings">Настройки модуля.</param>
    public void Write(IStudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Set(ShellKey, Shell);
        settings.Set(FontSizeKey, ClampFontSize(FontSize));
        settings.Set(ScrollbackKey, ClampScrollback(Scrollback));
        settings.Set(CursorBlinkKey, CursorBlink);
    }

    /// <summary>Кегль в допустимых границах; ноль и мусор — умолчание.</summary>
    /// <param name="value">Что просили.</param>
    public static double ClampFontSize(double value) =>
        double.IsFinite(value) && value > 0 ? Math.Clamp(value, MinFontSize, MaxFontSize) : DefaultFontSize;

    /// <summary>
    /// История в допустимых границах; ноль и отрицательное — умолчание.
    /// </summary>
    /// <param name="value">Что просили.</param>
    /// <remarks>
    /// Ноль значит «как обычно», а не «без истории»: нулём приходит и
    /// невынутое число — настройка, которую студия не смогла прочитать из
    /// правленого руками файла, — а терминал без истории выглядел бы
    /// поломкой, причину которой человек не найдёт.
    /// </remarks>
    public static int ClampScrollback(int value) =>
        value <= 0 ? DefaultScrollback : Math.Min(value, MaxScrollback);
}
