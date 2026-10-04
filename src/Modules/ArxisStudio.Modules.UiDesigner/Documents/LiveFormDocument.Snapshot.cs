using System.Security.Cryptography;
using ArxisStudio.Modules.UiDesigner.Snapshots;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Documents;

// Снимок формы для плиток: картинка того, что показывает вкладка, — в хранилище снимков.
// Часть LiveFormDocument; общее описание типа — в LiveFormDocument.cs.
internal sealed partial class LiveFormDocument
{
    private readonly FormSnapshots? _snapshots;
    private bool _snapshotQueued;
    private string? _pictureWritten;

    /// <summary>Последняя запись снимка — тестам: дождаться, а не спать.</summary>
    internal Task Snapshotting { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Снимает форму, когда она встанет: снимок почти даром — форма уже построена и разложена.
    /// </summary>
    /// <remarks>
    /// Зовут его всё, после чего форма выглядит иначе: встал корень, сменилось приложение, текст
    /// сохранён или принят с диска, вкладка вернулась в окно, холст снова виден. Просьбы сливаются в
    /// одну, после раскладки.
    /// </remarks>
    private void QueueSnapshot()
    {
        if (_snapshots is null || _snapshotQueued || _disposed)
            return;

        _snapshotQueued = true;
        Dispatcher.UIThread.Post(TakeSnapshot, DispatcherPriority.Background);
    }

    /// <summary>
    /// Снимает форму, если она такая же, как на диске: снимок отвечает файлу, а несохранённое показывает
    /// только вкладка.
    /// </summary>
    /// <remarks>
    /// Не снимается замороженный кадр замены — его корень уже отпущен, — вкладка вне окна и холст,
    /// спрятанный видом «XAML», — их не разложить, и снимок вышел бы прежней раскладкой, — и форма, у
    /// которой нет показа. Картинка, совпавшая с записанной, не пишется: показ вкладки снова и снова не
    /// будит плитки зря.
    /// </remarks>
    private void TakeSnapshot()
    {
        _snapshotQueued = false;

        if (_disposed
            || _snapshots is not { } snapshots
            || _frozen is not null
            || _document is not { IsModified: false, HasConflict: false, IsDeleted: false, IsClosed: false } document
            || _shown is not { Root: { } root } shown
            || TopLevel.GetTopLevel(_view) is null
            || Face() is not { IsEffectivelyVisible: true } face
            || FormPicture.Take(face, root, FormSnapshots.Pixels) is not { } picture)
        {
            return;
        }

        var written = Convert.ToHexString(SHA256.HashData(picture));

        if (written == _pictureWritten)
            return;

        _pictureWritten = written;

        var snapshot = FormSnapshots.Describe(_path.Value, document, shown, _form.ApplicationThemeVariant, face.Bounds.Size);

        Snapshotting = WriteSnapshotAsync(Snapshotting, snapshots, snapshot, picture);
    }

    /// <summary>Пишет снимок вслед за прежним и говорит показывающим, что превью формы сменилось.</summary>
    /// <remarks>
    /// Записи вкладки идут по очереди: каждая пишет те же два файла через те же временные, и встречные
    /// разошлись бы картинкой со сведениями.
    /// </remarks>
    private async Task WriteSnapshotAsync(Task previous, FormSnapshots snapshots, FormSnapshot snapshot, byte[] picture)
    {
        try
        {
            await previous;
            await snapshots.WriteAsync(snapshot, picture, _lifetime.Token);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // Не записалось — превью остаётся прежним, а следующий показ снимет снова.
            _pictureWritten = null;
            return;
        }

        _context.GetService<IStudioFilePreviews>()?.Invalidate(_path.Value);
    }

    /// <summary>
    /// Область формы на карточке — её фон и содержимое, без рамки карточки и заголовка окна.
    /// </summary>
    /// <remarks>Часть шаблона карточки Surface: <c>PART_FormBackground</c>.</remarks>
    private Border? Face() =>
        _form.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Name == "PART_FormBackground");
}
