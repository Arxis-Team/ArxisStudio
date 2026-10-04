using System.Runtime.CompilerServices;

namespace ArxisStudio.Tests;

/// <summary>
/// Процесс тестов не пишет снимки форм в машинную папку человека.
/// </summary>
/// <remarks>
/// Дизайнер, поднятый тестом без своей папки снимков, снимал бы каждую открытую форму в
/// <c>%LocalAppData%/ArxisStudio/Previews</c> — туда, где лежат снимки настоящих форм человека. Переменная
/// среды выключает снимки всему процессу, а тесты снимков называют свою временную папку швом модуля, и он
/// сильнее переменной. Так же живёт локальная история (<see cref="TestLocalHistory"/>).
/// </remarks>
internal static class TestPreviews
{
    /// <summary>Выключает снимки форм по умолчанию.</summary>
    [ModuleInitializer]
    internal static void Disable() => Environment.SetEnvironmentVariable("ARXIS_PREVIEWS", "0");
}
