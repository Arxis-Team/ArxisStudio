using ArxisStudio.Modules.Project.Model;
using ArxisStudio.ProjectSystem;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace ArxisStudio.Modules.Project.Browse;

/// <summary>
/// Превью картинок для плиток: декод в фоне, по размеру плитки, с кэшем и владением растром.
/// </summary>
/// <remarks>
/// Образец — <c>PluginIcons</c> студии: предел размера, декод сразу в нужную ширину, запомненные
/// неудачи, широкий перехват. Отличий три, и все от того, что картинок в папке бывают сотни, а
/// значок у плагина один. Декод идёт в фоне и не больше двух разом: плитки не виртуализированы, и
/// синхронный декод из привязки держал бы поток интерфейса, пока не прочтётся вся папка. Кэш
/// выбрасывает давнее: окно живёт весь сеанс, и папки, в которые заходили, иначе копились бы в
/// памяти до конца. Растр освобождается явно: нативную память Skia сборщик мусора не видит, и без
/// <see cref="Bitmap.Dispose"/> она росла бы, пока до растра не дойдёт финализатор.
/// <para>
/// Растр держит служба, а плитка на него ссылается. Колонка закрепляет то, что сейчас стоит в
/// плитках (<see cref="Hold"/>), и отпускает то, что с них снято (<see cref="Release"/>);
/// незакреплённое живёт до <see cref="IdleCapacity"/> по давности — так возврат в папку не
/// декодирует её заново. Освобождает служба только незакреплённое, поэтому растр не уходит из-под
/// плитки, которая его рисует.
/// </para>
/// <para>
/// Всё, кроме самого чтения файла, идёт в потоке интерфейса: просьбы приходят оттуда, продолжения
/// возвращаются туда же, и замок не нужен. Освобождать растр тоже надо там — вне его освобождение
/// ждёт диспетчера.
/// </para>
/// </remarks>
internal sealed class Previews : IDisposable
{
    /// <summary>
    /// Самая большая картинка, которую окно берётся уменьшать: больше — плитка остаётся силуэтом.
    /// </summary>
    /// <remarks>
    /// Предел в точках, а не в байтах: PNG декодер разворачивает целиком прежде, чем уменьшить, и
    /// память на это уходит по числу точек, а не по весу файла. Сжатая заливка 8000×8000 весит
    /// килобайты и разворачивается в четверть гигабайта. 4096 по стороне — текстура и снимок экрана
    /// 4K проходят, а два декода разом не отнимают больше сотни мегабайт.
    /// </remarks>
    internal const long MaxPixels = 4096L * 4096;

    /// <summary>Сколько незакреплённых превью служба держит для возврата в папку.</summary>
    internal const int IdleCapacity = 256;

    /// <summary>Сколько декодов идёт разом.</summary>
    private const int Lanes = 2;

    private readonly Dictionary<Key, Entry> _entries = [];
    private readonly Dictionary<Key, Flight> _flight = [];
    private readonly Dictionary<Bitmap, Entry> _owners = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<Entry> _idle = [];
    private readonly SemaphoreSlim _lanes = new(Lanes);

    /// <summary>Поколение кэша: декод, начатый до очистки, в кэш после неё не ложится.</summary>
    private int _generation;
    private bool _disposed;

    /// <summary>Сколько файлов служба прочла декодером — тестам: превью просят только видимые плитки.</summary>
    internal int Decoded { get; private set; }

    /// <summary>Сколько растров служба держит сейчас — тестам.</summary>
    internal int Kept => _entries.Values.Count(entry => entry.Bitmap is not null);

    /// <summary>Будет ли у узла превью: файл-картинка, которую декодер умеет прочесть.</summary>
    /// <param name="node">Узел.</param>
    /// <remarks>
    /// SVG — картинка, но декодера векторной графики в студии нет, и решено это намеренно: плитка SVG
    /// остаётся силуэтом.
    /// </remarks>
    public static bool Decodes(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node is { Kind: NodeKind.File, FileKind: FileKind.Image, Path.IsEmpty: false }
            && !string.Equals(node.Path.Extension, ".svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Превью файла в размер <paramref name="pixels"/> по длинной стороне; пусто — картинки не будет.
    /// </summary>
    /// <param name="path">Файл.</param>
    /// <param name="pixels">Длинная сторона в точках экрана.</param>
    /// <param name="token">Отмена ожидания.</param>
    /// <remarks>
    /// Ключ — путь, время записи и длина: перезаписанный файл получает новый ключ и новое превью. Отмена
    /// снимает только ожидание: начатый декод доходит до конца и ложится в кэш, и следующая просьба
    /// того же файла его находит.
    /// </remarks>
    public async Task<Bitmap?> RequestAsync(CanonicalPath path, int pixels, CancellationToken token)
    {
        if (_disposed || path.IsEmpty || pixels <= 0)
            return null;

        var file = path.Value;
        var key = await Task.Run(() => KeyOf(file, pixels), token).ConfigureAwait(true);

        if (key is not { } found || _disposed)
            return null;

        if (_entries.TryGetValue(found, out var entry))
        {
            Touch(entry);
            return entry.Bitmap;
        }

        // Декод, начатый до очистки, ответит пустотой: его растр в кэш уже не ляжет. Такой не ждут, а
        // начинают свой.
        if (!_flight.TryGetValue(found, out var flight) || flight.Generation != _generation)
        {
            flight = new Flight(DecodeAsync(found, _generation), _generation);
            _flight[found] = flight;
        }

        return await flight.Task.WaitAsync(token).ConfigureAwait(true);
    }

    /// <summary>Закрепляет растр: плитка его рисует, и освобождать его нельзя.</summary>
    /// <param name="bitmap">Растр, выданный службой.</param>
    public void Hold(Bitmap bitmap)
    {
        if (!_owners.TryGetValue(bitmap, out var entry))
            return;

        entry.Holds++;

        if (entry.Idle is { } node)
        {
            _idle.Remove(node);
            entry.Idle = null;
        }
    }

    /// <summary>Отпускает растр: плитка его больше не рисует.</summary>
    /// <param name="bitmap">Растр, выданный службой.</param>
    public void Release(Bitmap bitmap)
    {
        if (!_owners.TryGetValue(bitmap, out var entry) || entry.Holds == 0)
            return;

        if (--entry.Holds > 0)
            return;

        Rest(entry);
        Trim();
    }

    /// <summary>
    /// Освобождает все растры. Зовут, когда превью выключили: память отдаётся сразу.
    /// </summary>
    /// <remarks>
    /// Колонка снимает превью с плиток раньше, чем зовёт очистку: иначе последний кадр рисовал бы
    /// освобождённый растр.
    /// </remarks>
    public void Clear()
    {
        _generation++;

        foreach (var entry in _entries.Values)
            entry.Bitmap?.Dispose();

        _entries.Clear();
        _owners.Clear();
        _idle.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Clear();
    }

    private async Task<Bitmap?> DecodeAsync(Key key, int generation)
    {
        Bitmap? bitmap;

        await _lanes.WaitAsync().ConfigureAwait(true);

        try
        {
            bitmap = await Task.Run(() => Decode(key)).ConfigureAwait(true);
        }
        finally
        {
            _lanes.Release();
        }

        if (_flight.TryGetValue(key, out var flight) && flight.Generation == generation)
            _flight.Remove(key);

        Decoded++;

        // Очистка или закрытие окна пришли, пока файл читался: такой растр не нужен никому.
        if (_disposed || generation != _generation)
        {
            bitmap?.Dispose();
            return null;
        }

        var entry = new Entry(key, bitmap);

        _entries[key] = entry;

        if (bitmap is not null)
            _owners[bitmap] = entry;

        // Новое ложится в хвост незакреплённого: вытеснение берёт с головы, и до закрепления его
        // колонкой растр не уйдёт — за один проход колонка не просит больше, чем помещается.
        Rest(entry);
        Trim();

        return bitmap;
    }

    private void Touch(Entry entry)
    {
        if (entry.Idle is not { } node)
            return;

        _idle.Remove(node);
        _idle.AddLast(node);
    }

    private void Rest(Entry entry)
    {
        if (entry.Idle is not null)
            return;

        entry.Idle = _idle.AddLast(entry);
    }

    private void Trim()
    {
        while (_idle.Count > IdleCapacity && _idle.First is { } oldest)
        {
            _idle.RemoveFirst();

            var entry = oldest.Value;

            entry.Idle = null;
            _entries.Remove(entry.Key);

            if (entry.Bitmap is { } bitmap)
            {
                _owners.Remove(bitmap);
                bitmap.Dispose();
            }
        }
    }

    private static Key? KeyOf(string path, int pixels)
    {
        try
        {
            var file = new FileInfo(path);

            return file.Exists ? new Key(path, file.LastWriteTimeUtc, file.Length, pixels) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>Читает картинку не крупнее <see cref="Key.Pixels"/> по длинной стороне.</summary>
    /// <remarks>
    /// Размер спрашивается у заголовка раньше декода, и спрашивает его Skia — тот же декодер, которым
    /// читает Avalonia. Без этого вопроса нельзя: <see cref="Bitmap.DecodeToWidth"/> не только
    /// уменьшает, но и растягивает, и иконка 16×16 выходила бы мыльным квадратом во всю плитку.
    /// Картинка не крупнее плитки читается как есть, крупная — сразу в размер по длинной стороне:
    /// высокий кадр, уменьшенный по ширине, вышел бы втрое выше плитки.
    /// </remarks>
    private static Bitmap? Decode(Key key)
    {
        try
        {
            var (width, height) = Measure(key.Path);

            if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
                return null;

            using var stream = File.OpenRead(key.Path);

            if (Math.Max(width, height) <= key.Pixels)
                return new Bitmap(stream);

            return width >= height
                ? Bitmap.DecodeToWidth(stream, key.Pixels, BitmapInterpolationMode.HighQuality)
                : Bitmap.DecodeToHeight(stream, key.Pixels, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            // Перехват широкий намеренно, как у значков плагинов: файл в папку положил кто угодно, и
            // чем декодер ответит на мусор — его дело. Skia на нераспознанном файле бросает
            // NullReferenceException, а не что-нибудь про формат, и список исключений означал бы,
            // что окно падает на всём, чего не угадали, — на чужой картинке.
            return null;
        }
    }

    /// <summary>Размер картинки по заголовку, без декода.</summary>
    private static (int Width, int Height) Measure(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);

        return codec is null ? (0, 0) : (codec.Info.Width, codec.Info.Height);
    }

    /// <summary>Идущий декод и поколение кэша, в котором его начали.</summary>
    private readonly record struct Flight(Task<Bitmap?> Task, int Generation);

    /// <summary>Версия файла в размере: путь, время записи, длина и длинная сторона.</summary>
    private readonly record struct Key(string Path, DateTime Written, long Length, int Pixels);

    /// <summary>Прочитанное: растр или неудача, и кто его держит.</summary>
    private sealed class Entry(Key key, Bitmap? bitmap)
    {
        /// <summary>Версия файла, которую прочли.</summary>
        public Key Key { get; } = key;

        /// <summary>Растр; пусто — файл не прочитался, и читать его снова не надо.</summary>
        public Bitmap? Bitmap { get; } = bitmap;

        /// <summary>Сколько плиток его рисует.</summary>
        public int Holds { get; set; }

        /// <summary>Место в очереди незакреплённого; пусто — закреплён.</summary>
        public LinkedListNode<Entry>? Idle { get; set; }
    }
}
