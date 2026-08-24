using Avalonia;

namespace DesignFixtureApp;

/// <summary>Точка входа: нужна, чтобы проект был настоящим приложением.</summary>
public static class Program
{
    /// <summary>Запускает приложение.</summary>
    /// <param name="args">Аргументы командной строки.</param>
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>Собирает приложение — используется и предпросмотром разметки.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect();
}
