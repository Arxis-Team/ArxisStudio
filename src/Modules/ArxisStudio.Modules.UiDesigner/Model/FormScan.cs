using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.UiDesigner.Model;

/// <summary>Форма, найденная в решении: файл и его корень.</summary>
/// <param name="File">Файл и проект.</param>
/// <param name="Root">Корень разметки.</param>
internal sealed record FoundForm(FormFile File, FormRoot Root);

/// <summary>
/// Что уже прочитано: корень файла при известном времени его записи.
/// </summary>
/// <param name="Stamp">Время записи, при котором корень прочитан.</param>
/// <param name="Root">Корень; null — файл не форма.</param>
internal readonly record struct ReadRoot(DateTime Stamp, FormRoot? Root);

/// <summary>
/// Чтение корней разметки решения — вне потока интерфейса и без повторов.
/// </summary>
/// <remarks>
/// Служба проектов перечитывает модель на всякую перемену состава, а файлы разметки при этом
/// большей частью не менялись. Прочитанное помнится по времени записи файла, и перечитывание решения
/// на сотню форм открывает только те файлы, что трогали. Память — значение, а не поле: каждое чтение
/// получает прежнюю и отдаёт новую, так что два чтения подряд друг другу не мешают, а отменённое
/// просто не отдаёт свою.
/// </remarks>
internal static class FormScan
{
    /// <summary>
    /// Читает корни файлов.
    /// </summary>
    /// <param name="files">Файлы разметки решения.</param>
    /// <param name="known">Прочитанное прежде.</param>
    /// <param name="cancellation">Отмена: пришёл новый снимок.</param>
    /// <returns>Формы в порядке файлов и прочитанное теперь.</returns>
    public static (IReadOnlyList<FoundForm> Forms, IReadOnlyDictionary<CanonicalPath, ReadRoot> Known) Run(
        IReadOnlyList<FormFile> files,
        IReadOnlyDictionary<CanonicalPath, ReadRoot> known,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(known);

        var forms = new List<FoundForm>(files.Count);
        var now = new Dictionary<CanonicalPath, ReadRoot>(files.Count);

        foreach (var file in files)
        {
            cancellation.ThrowIfCancellationRequested();

            var stamp = Stamp(file.Path.Value);
            var read = known.TryGetValue(file.Path, out var was) && was.Stamp == stamp && stamp != default
                ? was
                : new ReadRoot(stamp, FormRoot.Read(file.Path.Value));

            now[file.Path] = read;

            if (read.Root is { } root)
                forms.Add(new FoundForm(file, root));
        }

        return (forms, now);
    }

    /// <summary>Время записи; у пропавшего или недоступного файла — пусто, и он читается снова.</summary>
    private static DateTime Stamp(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }
}
