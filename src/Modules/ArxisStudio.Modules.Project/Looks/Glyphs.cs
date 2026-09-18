using ArxisStudio.Icons;
using ArxisStudio.Modules.Project.Model;
using Avalonia.Media;

namespace ArxisStudio.Modules.Project.Looks;

/// <summary>
/// Значок и цвет узла.
/// </summary>
/// <remarks>
/// Тип файла различается цветом значка, а не формой: глифов в наборе на каждый тип нет и не
/// будет, а цвет значка типа документа — его законное место (§5 дизайн-системы). Код C# зелёный,
/// разметка синяя, XML и JSON оранжевые, картинки фиолетовые, прочее — цветом подписи. Проект
/// зелёный, как код, на котором он написан. Всё остальное — папки, решение, зависимости — идёт
/// цветом подписи: своего цвета у них нет, как нет его у значка панели.
/// </remarks>
internal static class Glyphs
{
    /// <summary>Значок узла.</summary>
    /// <param name="node">Узел.</param>
    /// <param name="expanded">Раскрыт ли узел: раскрытая папка рисуется открытой.</param>
    public static Geometry Of(Node node, bool expanded) => node.Kind switch
    {
        NodeKind.Solution => AxIcons.Solution,
        NodeKind.SolutionFolder or NodeKind.Folder => expanded ? AxIcons.FolderOpen : AxIcons.Folder,
        NodeKind.Project => AxIcons.Project,
        NodeKind.Dependencies => AxIcons.Dependencies,
        NodeKind.DependencyGroup or NodeKind.Dependency => node.Dependency switch
        {
            DependencyKind.Frameworks => AxIcons.Framework,
            DependencyKind.Packages => AxIcons.Package,
            DependencyKind.Projects => AxIcons.Project,
            DependencyKind.Assemblies => AxIcons.Assembly,
            _ => AxIcons.Analyzer,
        },
        _ => node.FileKind switch
        {
            FileKind.CSharp or FileKind.Markup or FileKind.Xml => AxIcons.DocumentCode,
            FileKind.Image => AxIcons.Image,
            _ => AxIcons.Document,
        },
    };

    /// <summary>
    /// Силуэт плитки: папка — папкой, всё прочее на диске — документом.
    /// </summary>
    /// <remarks>
    /// Силуэт — только у того, что лежит на диске; у предмета модели — проекта, зависимости,
    /// группы — формы на диске нет, и его плитка — подложка со значком <see cref="Of"/>.
    /// </remarks>
    /// <param name="node">Узел папки или файла.</param>
    public static Geometry TileOf(Node node) =>
        node.Kind is NodeKind.Folder or NodeKind.SolutionFolder ? AxIcons.FolderTile : AxIcons.DocumentTile;

    /// <summary>Ключ кисти темы для значка узла; <c>null</c> — значок идёт цветом подписи.</summary>
    /// <param name="node">Узел.</param>
    public static string? TintOf(Node node) => node.Kind switch
    {
        NodeKind.Project => "AxTintGreenBrush",
        NodeKind.File => node.FileKind switch
        {
            FileKind.CSharp => "AxTintGreenBrush",
            FileKind.Markup => "AxTintBlueBrush",
            FileKind.Xml or FileKind.Json => "AxTintOrangeBrush",
            FileKind.Image => "AxTintPurpleBrush",
            _ => null,
        },
        _ => null,
    };
}
