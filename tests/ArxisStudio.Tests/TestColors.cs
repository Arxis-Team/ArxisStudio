using System.Runtime.CompilerServices;

namespace ArxisStudio.Tests;

/// <summary>
/// Процесс тестов не пишет образцы цвета в профиль человека.
/// </summary>
/// <remarks>
/// Окно «Цвет», открытое тестом, сохраняло бы образцы в <c>%AppData%/ArxisStudio/colors</c> — туда, где лежат
/// настоящие библиотеки человека. Переменная среды держит библиотеки профиля в памяти всему процессу, а тесты
/// хранилища называют свою временную папку прямо. Так же живут снимки форм (<see cref="TestPreviews"/>).
/// </remarks>
internal static class TestColors
{
    /// <summary>Держит библиотеки профиля в памяти.</summary>
    [ModuleInitializer]
    internal static void Disable() => Environment.SetEnvironmentVariable("ARXIS_COLORS", "0");
}
