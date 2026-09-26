using System.Security.Cryptography;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.Watching;

/// <summary>Слежение за диском одной сессии.</summary>
internal interface IProjectsWatch : IDisposable
{
    /// <summary>
    /// Следит за тем, из чего собран снимок, и за папками его проектов.
    /// </summary>
    /// <param name="snapshot">Снимок, опубликованный последним.</param>
    /// <remarks>Зовётся после каждой публикации: набор входов меняется вместе с моделью.</remarks>
    void Follow(SolutionSnapshot snapshot);

    /// <summary>
    /// Придерживает приговоры слежения, пока служба правит диск и перечитывает модель.
    /// </summary>
    /// <returns>Придержку: её отпускают, когда перечитывание кончилось — удачно или нет.</returns>
    /// <remarks>
    /// Пачка, разобранная посреди перечитывания, сверилась бы со снимком, который оно вот-вот
    /// заменит, и попросила бы вторую загрузку ради того, что первая уже прочла. Придержанное
    /// сверяется, когда придержку отпустили, — со снимком, опубликованным последним.
    /// </remarks>
    IWatchHold Hold();
}

/// <summary>Придержка слежения на время правки службы.</summary>
internal interface IWatchHold : IDisposable
{
    /// <summary>
    /// Запоминает входы оценки такими, какими их оставила правка, — до перечитывания.
    /// </summary>
    /// <param name="inputs">Входы, которые правка могла переписать.</param>
    /// <remarks>
    /// Входы снимок сверяет по пути, а не по содержимому, и переписанный правкой файл проекта
    /// просил бы загрузку и после перечитывания, которое его уже прочло. Перечитывание начинается
    /// позже, чем снят отпечаток, и читает файл таким или новее: совпал отпечаток — модель знает
    /// этот файл, не совпал — файл трогали после, и загрузка нужна.
    /// </remarks>
    void Expect(IEnumerable<CanonicalPath> inputs);

    /// <summary>Модель перечитана после правки: запомненное прочла она.</summary>
    /// <remarks>Неподтверждённое забывается: неудачное перечитывание не прочло ничего.</remarks>
    void Confirm();
}

/// <summary>Придержка, которой нечего придерживать: слежения нет.</summary>
internal sealed class NoWatchHold : IWatchHold
{
    /// <summary>Единственный экземпляр: состояния у него нет.</summary>
    public static NoWatchHold Instance { get; } = new();

    /// <inheritdoc/>
    public void Expect(IEnumerable<CanonicalPath> inputs)
    {
    }

    /// <inheritdoc/>
    public void Confirm()
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>
/// Диск глазами проверки состава: что сейчас лежит по пути.
/// </summary>
/// <remarks>
/// Шов ради тестов разбора: состав проверяется по диску в момент разбора пачки, и проверять это
/// правило на настоящих файлах значило бы проверять ещё и файловую систему.
/// </remarks>
internal interface IDiskView
{
    /// <summary>По пути лежит файл.</summary>
    /// <param name="path">Путь.</param>
    bool IsFile(CanonicalPath path);

    /// <summary>По пути лежит папка.</summary>
    /// <param name="path">Путь.</param>
    bool IsDirectory(CanonicalPath path);

    /// <summary>Файлы в папке и во всех вложенных.</summary>
    /// <param name="directory">Папка.</param>
    IEnumerable<CanonicalPath> FilesUnder(CanonicalPath directory);

    /// <summary>Отпечаток содержимого файла: совпал — значит, файл тот же.</summary>
    /// <param name="path">Путь.</param>
    /// <param name="print">Отпечаток; null — файла нет.</param>
    /// <returns><c>false</c> — прочесть не вышло, и о файле ничего не известно.</returns>
    bool TryFingerprint(CanonicalPath path, out string? print);
}

/// <summary>Настоящий диск.</summary>
internal sealed class DiskView : IDiskView
{
    /// <summary>Единственный экземпляр: состояния у него нет.</summary>
    public static DiskView Instance { get; } = new();

    /// <inheritdoc/>
    public bool IsFile(CanonicalPath path) => File.Exists(path.Value);

    /// <inheritdoc/>
    public bool IsDirectory(CanonicalPath path) => Directory.Exists(path.Value);

    /// <inheritdoc/>
    /// <remarks>
    /// Папка может пропасть посреди обхода — её переименовали следом за созданием. Это не сбой, а
    /// ответ «файлов больше нет»: следующая пачка принесёт новое имя.
    /// </remarks>
    public IEnumerable<CanonicalPath> FilesUnder(CanonicalPath directory)
    {
        IEnumerator<string> files;

        try
        {
            files = Directory.EnumerateFiles(
                directory.Value,
                "*",
                new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).GetEnumerator();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            yield break;
        }

        using (files)
        {
            while (true)
            {
                string file;

                try
                {
                    if (!files.MoveNext())
                        yield break;

                    file = files.Current;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    yield break;
                }

                if (CanonicalPath.TryCreate(file, out var path))
                    yield return path;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Читается с разрешением писать и удалять: отпечаток не должен мешать тому, кто правит файл
    /// следом. Файл, пропавший или занятый посреди чтения, — «не известно», а не «нет».
    /// </remarks>
    public bool TryFingerprint(CanonicalPath path, out string? print)
    {
        print = null;

        try
        {
            if (!File.Exists(path.Value))
                return true;

            using var stream = new FileStream(path.Value, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            print = Convert.ToHexString(SHA256.HashData(stream));

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
