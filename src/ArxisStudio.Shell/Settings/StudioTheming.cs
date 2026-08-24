using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;

namespace ArxisStudio.Shell.Settings;

/// <summary>
/// Применение выбранной темы ко всей студии.
/// </summary>
/// <remarks>
/// Вариант ставится и приложению, и каждому открытому окну: приложение задаёт
/// значение по умолчанию для окон, которые ещё появятся, а уже показанное окно
/// свой вариант само не перечитывает — без второго шага смена темы видна только
/// после перезапуска.
/// </remarks>
public static class StudioTheming
{
    /// <summary>Применяет тему к приложению и всем открытым окнам.</summary>
    /// <param name="theme">Выбранная тема.</param>
    public static void Apply(StudioTheme theme)
    {
        var variant = theme == StudioTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;

        if (Application.Current is not { } app)
            return;

        app.RequestedThemeVariant = variant;

        if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                window.RequestedThemeVariant = variant;
                StudioWindowChrome.Apply(window, theme);
            }
        }
    }
}
