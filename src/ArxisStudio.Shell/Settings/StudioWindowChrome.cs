using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Shell.Settings;

/// <summary>
/// Красит рамку окна в цвет темы.
/// </summary>
/// <remarks>
/// Окна студии сами рисуют заголовок, но системную рамку оставляют — она даёт
/// тень, привязку к краям экрана и изменение размера. Windows красит эту рамку
/// своим серым, и вокруг тёмной студии появляется светлая кайма шириной в
/// несколько пикселей. Просить у системы нужный цвет — единственный способ её
/// убрать, не отказываясь от самой рамки.
///
/// Настройка появилась в Windows 11; на более ранних версиях вызов просто
/// ничего не делает, как и на других платформах.
/// </remarks>
public static class StudioWindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;

    /// <summary>Приводит рамку окна к текущей теме.</summary>
    /// <param name="window">Окно студии.</param>
    /// <param name="theme">Выбранная тема.</param>
    public static void Apply(Window window, StudioTheme theme)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
            return;

        // Рамка сливается с полосой заголовка, а не с содержимым: полоса
        // примыкает к рамке, и разница цветов заметнее всего именно там.
        var border = window.TryFindResource("AxBg2Color", window.ActualThemeVariant, out var value) && value is Color color
            ? color
            : theme == StudioTheme.Light ? Color.FromRgb(0xF7, 0xF8, 0xFA) : Color.FromRgb(0x2B, 0x2D, 0x30);

        ApplyWindows(handle, border, theme == StudioTheme.Dark);
    }

    /// <summary>Приводит рамки всех открытых окон к текущей теме.</summary>
    /// <param name="theme">Выбранная тема.</param>
    public static void ApplyToAllWindows(StudioTheme theme)
    {
        if (Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return;

        foreach (var window in desktop.Windows)
            Apply(window, theme);
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindows(IntPtr handle, Color border, bool dark)
    {
        try
        {
            var darkMode = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

            // COLORREF: 0x00BBGGRR — порядок каналов обратный привычному.
            var colorRef = border.R | (border.G << 8) | (border.B << 16);
            DwmSetWindowAttribute(handle, DwmwaBorderColor, ref colorRef, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref colorRef, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Рамка останется системного цвета — это некрасиво, но не мешает работать.
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
