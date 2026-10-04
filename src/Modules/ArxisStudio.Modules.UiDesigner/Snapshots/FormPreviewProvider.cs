using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.UiDesigner.Snapshots;

/// <summary>
/// Поставщик превью форм: снимок, который оставила живая вкладка или фоновая съёмка, — для плиток окна
/// проекта и любого другого показывающего.
/// </summary>
/// <remarks>
/// Сам поставщик форм не строит: плитки спрашивают его при каждой прокрутке, а постройка формы — это
/// типы проекта и код его контролов в потоке интерфейса. Нет снимка или он старше файла — поставщик
/// отвечает тем, что есть, и просит съёмку (<see cref="FormCaptures"/>) снять форму, когда станет тихо;
/// новый снимок придёт показывающим через <c>Changed</c>.
/// </remarks>
/// <param name="snapshots">Хранилище снимков.</param>
/// <param name="captures">Фоновая съёмка; null — форма снимается только своей вкладкой.</param>
internal sealed class FormPreviewProvider(FormSnapshots snapshots, FormCaptures? captures = null) : IFilePreviewProvider
{
    /// <inheritdoc/>
    public IReadOnlyList<string> Extensions { get; } = [FormFiles.Extension];

    /// <inheritdoc/>
    /// <remarks>Снимок один, крупнейшего размера плитки: уменьшает его показывающий.</remarks>
    public async Task<FilePreview?> GetPreviewAsync(string filePath, int pixels, CancellationToken cancellationToken)
    {
        var preview = await snapshots.ReadAsync(filePath, cancellationToken);

        if (preview is null or { IsStale: true })
            captures?.Request(filePath);

        return preview;
    }
}
