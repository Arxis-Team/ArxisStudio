using ArxisStudio.Modules.UiDesigner.Snapshots;
using ArxisStudio.Sdk;
using Avalonia.Media.Imaging;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>
/// Снимки форм доски — только у форм на виду, с растром, который уходит вместе с ними с вида.
/// </summary>
/// <remarks>
/// <para>
/// Снимок форме ставится, когда она показалась на холсте (<see cref="BoardSight"/>), и снимается, когда
/// она ушла с виду. Растр не живёт дольше того, что его показывает, а доска из сотни форм держит в памяти
/// столько снимков, сколько форм видно.
/// </para>
/// <para>
/// Картинку отдаёт служба превью студии — та же, что у окна проекта. Снимка нет или он старше файла —
/// её поставщик, дизайнер, снимет форму в фоне и скажет об этом <see cref="IStudioFilePreviews.Changed"/>:
/// карточка спросит снова. Декод — вне потока интерфейса, освобождение — в нём: нативную память Skia
/// сборщик мусора не видит, а вне потока интерфейса освобождение ждёт диспетчера.
/// </para>
/// </remarks>
internal sealed class BoardPreviews : IDisposable
{
    /// <summary>
    /// Длинная сторона снимка, который спрашивает доска: самый крупный, какой хранит дизайнер. Снимок стоит
    /// на месте формы в её размер, пока она не встала живой и пока её держит вкладка на экране.
    /// </summary>
    private const int Pixels = FormSnapshots.Pixels;

    private readonly BoardSight _sight;
    private readonly IStudioFilePreviews _previews;
    private readonly Dictionary<FormCard, int> _shown = [];
    private bool _disposed;

    /// <summary>Заводит снимки над холстом доски.</summary>
    /// <param name="sight">Какие формы на виду.</param>
    /// <param name="previews">Превью файлов студии.</param>
    public BoardPreviews(BoardSight sight, IStudioFilePreviews previews)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(previews);

        _sight = sight;
        _previews = previews;

        sight.Came += OnCame;
        sight.Went += OnWent;
        previews.Changed += OnChanged;

        foreach (var card in sight.Seen)
            Show(card);
    }

    /// <summary>Последняя загрузка снимка — тестам: дождаться, а не спать.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sight.Came -= OnCame;
        _sight.Went -= OnWent;
        _previews.Changed -= OnChanged;

        foreach (var card in _shown.Keys)
            Put(card, null, stale: false);

        _shown.Clear();
    }

    private void OnCame(object? sender, FormCard card) => Show(card);

    private void OnWent(object? sender, FormCard card)
    {
        if (_shown.Remove(card))
            Put(card, null, stale: false);
    }

    /// <summary>Поставщик сменил снимок формы: видимая её спрашивает заново.</summary>
    private void OnChanged(object? sender, FilePreviewChangedEventArgs e)
    {
        foreach (var card in _shown.Keys.Where(card => string.Equals(card.Path.Value, e.FilePath, StringComparison.OrdinalIgnoreCase)).ToList())
            Show(card);
    }

    /// <summary>Спрашивает снимок карточки; прежний стоит, пока не придёт новый.</summary>
    private void Show(FormCard card)
    {
        var turn = _shown.GetValueOrDefault(card) + 1;

        _shown[card] = turn;
        Loading = LoadAsync(card, turn);
    }

    private async Task LoadAsync(FormCard card, int turn)
    {
        FilePreview? preview;

        try
        {
            preview = await _previews.GetAsync(card.Path.Value, Pixels);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var bitmap = preview is null ? null : await Task.Run(() => Decode(preview.Image));

        // Пока читали, форма ушла с виду или её спросили снова: этот растр уже никому не нужен.
        if (_disposed || !_shown.TryGetValue(card, out var current) || current != turn)
        {
            bitmap?.Dispose();
            return;
        }

        Put(card, bitmap, preview?.IsStale == true);
    }

    /// <summary>Ставит карточке снимок и освобождает прежний.</summary>
    private static void Put(FormCard card, Bitmap? bitmap, bool stale)
    {
        var old = card.Preview;

        card.Preview = bitmap;
        card.IsPreviewStale = stale && bitmap is not null;

        if (!ReferenceEquals(old, bitmap))
            old?.Dispose();
    }

    /// <summary>Читает картинку поставщика; не читается — снимка нет.</summary>
    private static Bitmap? Decode(ReadOnlyMemory<byte> image)
    {
        try
        {
            using var stream = new MemoryStream(image.ToArray(), writable: false);

            return new Bitmap(stream);
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            // Перехват широкий, как у превью окна проекта: картинку отдал поставщик, и чем декодер
            // ответит на испорченную, знает только он.
            return null;
        }
    }
}
