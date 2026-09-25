namespace ArxisStudio.Shell;

/// <summary>
/// Какие исключения студия переживает.
/// </summary>
/// <remarks>
/// Широкий <c>catch</c> студии стоит там, где бросает чужое или непредсказуемое: код плагина,
/// файл человека, просьба второй копии. Ловит он всё, кроме двух бед, после которых процесс уже не
/// в своём уме. Правило одно, и написанное в каждом месте своими словами, оно однажды разошлось:
/// одно место пропускало переполнение стека.
/// </remarks>
public static class Faults
{
    /// <summary>Исключение, после которого студия работает дальше: всё, кроме нехватки памяти и переполнения стека.</summary>
    /// <param name="error">Пойманное исключение.</param>
    public static bool Survivable(Exception error) => error is not (OutOfMemoryException or StackOverflowException);
}
