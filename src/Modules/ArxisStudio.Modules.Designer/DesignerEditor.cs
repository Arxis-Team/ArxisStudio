using ArxisStudio.Sdk;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.Modules.Designer;

/// <summary>
/// Редактор документов дизайнера: берётся за файлы <c>.axaml</c>.
/// </summary>
public sealed class DesignerEditor : DocumentEditor
{
    /// <inheritdoc/>
    public override bool CanOpen(string filePath) =>
        string.Equals(Path.GetExtension(filePath), ".axaml", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override async Task<(DocumentView? View, string? Error)> OpenAsync(string filePath)
    {
        if (Context.GetService<IDesignerWorkspace>() is not { Snapshot: { } snapshot } workspace)
            return (null, Localizer.Instance["editor.loadfailed"]);

        if (workspace.FindProjectForFile(filePath) is not { } project)
            return (null, Localizer.Instance["editor.loadfailed"]);

        // Живые объекты создаются на потоке интерфейса — иначе загрузчик
        // откажется их отдавать.
        var (document, error) = await DesignDocument.OpenAsync(filePath, snapshot, project);

        if (document is null)
            return (null, error);

        return (new DesignerDocumentView(document), null);
    }
}
