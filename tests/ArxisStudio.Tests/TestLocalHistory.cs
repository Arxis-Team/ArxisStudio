using System.Runtime.CompilerServices;

namespace ArxisStudio.Tests;

/// <summary>
/// Процесс тестов не пишет локальную историю человека.
/// </summary>
/// <remarks>
/// Служба проектов, поднятая тестом без своей папки истории, писала бы в машинную папку
/// пользователя — ту самую, где лежит история его настоящих решений. Переменная среды выключает
/// историю всему процессу, а тесты истории называют свою временную папку прямо, и она сильнее
/// переменной. Инициализатор модуля ставит её раньше, чем тест дотронется до службы.
/// </remarks>
internal static class TestLocalHistory
{
    /// <summary>Выключает историю по умолчанию.</summary>
    [ModuleInitializer]
    internal static void Disable() => Environment.SetEnvironmentVariable("ARXIS_LOCAL_HISTORY", "0");
}
