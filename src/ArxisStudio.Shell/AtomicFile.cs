namespace ArxisStudio.Shell;

/// <summary>
/// Запись файла данных человека целиком или никак.
/// </summary>
/// <remarks>
/// Текст пишется в соседний временный файл и переносится на место одним шагом: оборванная запись —
/// закрытый процесс, кончившееся место, выключенная машина — оставляет прежний файл, а не обрезок.
/// Обрезок читается испорченным, а настройки студии, раскладку и недавние их хранилища в таком случае
/// переписывают своими умолчаниями — один обрыв стоил бы человеку всего файла (запись 383).
/// <para>
/// Имя временного файла своё на каждую запись: рядом бывает вторая студия, и общий черновик достался
/// бы обеим. Модули и локальная история пишут так же своими копиями — студийных сборок они не видят.
/// </para>
/// </remarks>
public static class AtomicFile
{
    /// <summary>Записывает текст в файл, заводя его папку.</summary>
    /// <param name="path">Куда.</param>
    /// <param name="text">Что; UTF-8 без метки порядка байт.</param>
    /// <exception cref="IOException">Не записалось; прежний файл остался как был.</exception>
    /// <exception cref="UnauthorizedAccessException">Нет прав; прежний файл остался как был.</exception>
    public static void WriteAllText(string path, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(text);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            Discard(temporary);
        }
    }

    // Уборка черновика не должна заслонять собой причину, по которой запись не прошла.
    private static void Discard(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Остался черновик рядом — не беда: имя у него своё, и следующей записи он не мешает.
        }
    }
}
