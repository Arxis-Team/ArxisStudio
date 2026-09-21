using System.Buffers.Binary;
using System.IO.Compression;

namespace ArxisStudio.LocalHistory;

/// <summary>
/// Содержимое по адресу: <c>objects/ab/cdef…</c>, сжатое Brotli, под коротким заголовком.
/// </summary>
/// <remarks>
/// <para>
/// Заголовок — метка формата и длина несжатых байт. Метка отличает объект от чужого файла, а длина
/// позволяет прочитать ровно столько, сколько записали, и узнать усечённый объект, а не вернуть
/// человеку половину его файла.
/// </para>
/// <para>
/// Объект пишется во временный файл рядом и переносится на место одним переименованием: оборванная
/// запись оставляет временный файл, который уберёт очистка, а не объект с правильным именем и
/// неправильным содержимым. Уже лежащий объект не переписывается — его байты те же по определению
/// адреса.
/// </para>
/// </remarks>
internal sealed class ContentStore
{
    /// <summary>Сколько живёт осиротевший объект, прежде чем очистка его заберёт.</summary>
    /// <remarks>
    /// Объект кладут раньше, чем на него сошлётся журнал или известное состояние: снять содержимое и
    /// записать правку — два шага. Очистка, прошедшая между ними, забрала бы только что положенное.
    /// </remarks>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    private const string Temporary = ".tmp";
    private const int HeaderLength = 12;

    private static ReadOnlySpan<byte> Magic => "AXH1"u8;

    private readonly string _root;

    /// <summary>Заводит хранилище в этой папке.</summary>
    /// <param name="root">Папка объектов.</param>
    public ContentStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(root);
    }

    /// <summary>Где лежит объект.</summary>
    /// <param name="id">Адрес.</param>
    public string PathOf(ContentId id) => Path.Combine(_root, id.Value[..2], id.Value[2..]);

    /// <summary>Кладёт байты и возвращает их адрес; уже лежащее не переписывается.</summary>
    /// <param name="bytes">Содержимое.</param>
    public ContentId Put(ReadOnlySpan<byte> bytes)
    {
        var id = ContentId.Of(bytes);
        var target = PathOf(id);

        if (File.Exists(target))
            return id;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var temporary = $"{target}.{Guid.NewGuid():N}{Temporary}";

        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            Span<byte> header = stackalloc byte[HeaderLength];

            Magic.CopyTo(header);
            BinaryPrimitives.WriteInt64LittleEndian(header[Magic.Length..], bytes.Length);
            stream.Write(header);

            using (var compressed = new BrotliStream(stream, CompressionLevel.Fastest, leaveOpen: true))
                compressed.Write(bytes);

            stream.Flush(flushToDisk: true);
        }

        try
        {
            File.Move(temporary, target);
        }
        catch (IOException) when (File.Exists(target))
        {
            // Тот же объект положили рядом, пока писали этот: байты у них одни, оставляем лежащий.
            File.Delete(temporary);
        }

        return id;
    }

    /// <summary>Читает объект; null — его нет или он испорчен.</summary>
    /// <param name="id">Адрес.</param>
    public byte[]? Read(ContentId id)
    {
        try
        {
            using var stream = new FileStream(PathOf(id), FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[HeaderLength];

            stream.ReadExactly(header);

            if (!header[..Magic.Length].SequenceEqual(Magic))
                return null;

            var length = BinaryPrimitives.ReadInt64LittleEndian(header[Magic.Length..]);

            if (length is < 0 or > int.MaxValue)
                return null;

            var bytes = new byte[length];

            using (var compressed = new BrotliStream(stream, CompressionMode.Decompress))
                compressed.ReadExactly(bytes);

            // Адрес — это и проверка: байты, не совпавшие с ним, человеку не отдаются.
            return ContentId.Of(bytes) == id ? bytes : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Сколько занимают объекты, в байтах.</summary>
    public long Size => Files().Sum(file => file.Length);

    /// <summary>
    /// Убирает объекты, на которые никто не ссылается, и брошенные временные файлы.
    /// </summary>
    /// <param name="alive">Адреса, на которые ссылаются журнал и известное состояние.</param>
    /// <returns>Сколько убрано.</returns>
    /// <remarks>
    /// Льготный срок меряется настоящими часами, а не часами истории: он защищает от гонки с тем,
    /// кто кладёт объект прямо сейчас, а сейчас бывает только одно.
    /// </remarks>
    public int Sweep(IReadOnlySet<ContentId> alive)
    {
        var removed = 0;
        var now = DateTime.UtcNow;

        foreach (var file in Files())
        {
            if (now - file.LastWriteTimeUtc < Grace)
                continue;

            var name = Path.GetFileName(Path.GetDirectoryName(file.FullName)) + file.Name;

            if (!file.Name.EndsWith(Temporary, StringComparison.Ordinal)
                && ContentId.TryParse(name, out var id)
                && alive.Contains(id))
            {
                continue;
            }

            try
            {
                file.Delete();
                removed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Держит антивирус или индексатор — заберём в следующий раз.
            }
        }

        return removed;
    }

    private IEnumerable<FileInfo> Files() =>
        new DirectoryInfo(_root).EnumerateFiles("*", SearchOption.AllDirectories);
}
