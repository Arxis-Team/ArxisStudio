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

    /// <summary>
    /// Сообщение исключения без обёртки отражения — то, что показывают человеку и пишут в журнал.
    /// </summary>
    /// <remarks>
    /// Код плагина студия зовёт и отражением — команду из атрибута, конструктор панели, — и тогда
    /// беда лежит во вложенном исключении, а снаружи остаётся «Exception has been thrown by the
    /// target of an invocation». Развёртка стояла копией у хоста, у шва и у заглушки упавшей панели.
    /// </remarks>
    /// <param name="error">Пойманное исключение.</param>
    public static string Message(Exception error) =>
        error is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner.Message : error.Message;
}
