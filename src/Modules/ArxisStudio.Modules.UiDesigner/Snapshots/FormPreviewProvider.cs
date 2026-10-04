using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.UiDesigner.Snapshots;

/// <summary>
/// Поставщик превью форм: снимок, который оставила живая вкладка, — для плиток окна проекта и любого
/// другого показывающего.
/// </summary>
/// <remarks>
/// Форму он не строит: построить её значит поднять типы проекта и исполнить код его контролов в потоке
/// интерфейса, а плитки просят превью при каждой прокрутке. Нет снимка — плитка остаётся значком, пока
/// форму не откроют.
/// </remarks>
/// <param name="snapshots">Хранилище снимков.</param>
internal sealed class FormPreviewProvider(FormSnapshots snapshots) : IFilePreviewProvider
{
    /// <inheritdoc/>
    public IReadOnlyList<string> Extensions { get; } = [FormFiles.Extension];

    /// <inheritdoc/>
    /// <remarks>Снимок один, крупнейшего размера плитки: уменьшает его показывающий.</remarks>
    public Task<FilePreview?> GetPreviewAsync(string filePath, int pixels, CancellationToken cancellationToken) =>
        snapshots.ReadAsync(filePath, cancellationToken);
}
