using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Project.Model;

/// <summary>
/// Какие элементы одного проекта дерево показывает и где.
/// </summary>
/// <remarks>
/// Снимок — вычисление MSBuild, а не содержимое папки, и элементов в нём больше, чем файлов у
/// проекта: служебные элементы SDK, кандидаты <c>PotentialEditorConfigFiles</c> на каждую папку с
/// исходниками — их MSBuild и перечисляет затем, чтобы проверить, есть ли они, — выход сборки,
/// один и тот же файл под двумя типами элементов. Здесь отсекается всё, что показывать нельзя
/// независимо от диска; что файла на самом деле нет, говорит <see cref="DiskProbe"/>, и обе стороны
/// спрашивают одно и то же правило — иначе одна показывала бы то, о чём другая не спросила. Пустая
/// папка, которой в снимке нет вовсе, спрашивает своё правило — <see cref="ShowsFolder"/>, — и его же
/// спрашивает слежение окна за папками.
/// <para>
/// Заводится один раз на проект: пути выхода сборки у проекта одни, а элементов у него тысячи.
/// </para>
/// </remarks>
public sealed class ItemFilter
{
    private static readonly string[] OutputProperties =
        ["OutputPath", "BaseOutputPath", "BaseIntermediateOutputPath", "IntermediateOutputPath"];

    private readonly ProjectSnapshot _project;
    private readonly HashSet<string> _output = new(["bin", "obj"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Заводит правило для проекта.</summary>
    /// <param name="project">Проект, чьи элементы решаются.</param>
    /// <remarks>
    /// Выход сборки — <c>bin</c>, <c>obj</c> и первые сегменты путей, которые проект переопределил
    /// своими свойствами: проект, собирающийся в <c>build/</c>, иначе показал бы весь свой выход.
    /// </remarks>
    public ItemFilter(ProjectSnapshot project)
    {
        ArgumentNullException.ThrowIfNull(project);

        _project = project;

        foreach (var key in OutputProperties)
        {
            if (project.Properties.GetValueOrDefault(key) is not { Length: > 0 } value)
                continue;

            var first = value.Replace('\\', '/').Trim('/').Split('/')[0];

            if (first.Length > 0 && first != "." && first != "..")
                _output.Add(first);
        }
    }

    /// <summary>Явно объявленная папка ли это: <c>&lt;Folder Include="Models\"/&gt;</c>.</summary>
    /// <param name="item">Элемент.</param>
    public static bool IsFolder(ProjectItem item) =>
        string.Equals(item?.ItemType, ProjectItemTypes.Folder, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Показывает ли дерево элемент, и где он стоит в проекте.
    /// </summary>
    /// <param name="item">Элемент.</param>
    /// <param name="relative">Путь в проекте через прямую черту, без хвостовой; пусто, если не показывается.</param>
    /// <returns>Стоит ли элемент в дереве.</returns>
    /// <remarks>
    /// Место — ссылка <c>Link</c>, если она есть: так элемент, взятый из чужой папки, стоит там,
    /// куда его поставил проект. Без ссылки место — путь от папки проекта, а файл вне её дерево не
    /// показывает: он не лежит ни в одной папке проекта. Папка с точкой — <c>.idea</c>, <c>.vs</c>,
    /// <c>.git</c> — служебная и не показывается вместе со всем содержимым; файл с точкой — обычный
    /// файл проекта.
    /// <para>
    /// Два соглашения MSBuild дерево берёт как есть. Тип с подчёркиванием — внутренний элемент
    /// целей, а не файл: <c>_KnownRuntimeIdentiferPlatforms</c> перечисляет платформы, и путём его
    /// имя становится только потому, что MSBuild достраивает каждое имя до пути в папке проекта.
    /// Метаданные <c>Visible="false"</c> — элемент, который проект сам спрятал от среды; так его
    /// прячут VS и Rider. В снимке решения студии таких элементов почти половина — 8 782 из 18 246.
    /// </para>
    /// </remarks>
    public bool Shows(ProjectItem item, out string relative)
    {
        ArgumentNullException.ThrowIfNull(item);

        relative = string.Empty;

        if (item.FullPath.IsEmpty
            || item.ItemType.StartsWith('_')
            || string.Equals(item.Metadata.GetValueOrDefault("Visible"), "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string place;

        if (item.Link is { Length: > 0 } link)
            place = link;
        else if (item.FullPath.StartsWith(_project.ProjectDirectory))
            place = item.FullPath.Value[_project.ProjectDirectory.Value.Length..];
        else
            return false;

        place = place.Replace('\\', '/').Trim('/');

        if (place.Length == 0)
            return false;

        var segments = place.Split('/');

        for (var at = 0; at < segments.Length; at++)
        {
            var segment = segments[at];

            if (segment.Length == 0 || segment is "." or "..")
                return false;

            if (at < segments.Length - 1 && segment[0] == '.')
                return false;
        }

        if (_output.Contains(segments[0]))
            return false;

        relative = place;

        return true;
    }

    /// <summary>
    /// Может ли папка с диска стоять в дереве проекта, и где.
    /// </summary>
    /// <param name="path">Папка.</param>
    /// <param name="relative">Путь в проекте через прямую черту; пусто, если не показывается.</param>
    /// <returns>Может ли папка стоять в дереве.</returns>
    /// <remarks>
    /// Правила элемента, и одно строже: папка с точкой не показывается и последним сегментом. Файл
    /// <c>.editorconfig</c> — обычный файл проекта, а пустые <c>.vs</c> и <c>.idea</c> — служебные
    /// папки среды, и места в проекте у них нет. Ссылок <c>Link</c> у папки с диска не бывает: её
    /// место — путь от папки проекта.
    /// </remarks>
    public bool ShowsFolder(CanonicalPath path, out string relative)
    {
        relative = string.Empty;

        var home = _project.ProjectDirectory;

        if (path.IsEmpty || path == home || !path.StartsWith(home))
            return false;

        var place = path.Value[home.Value.Length..].Replace('\\', '/').Trim('/');

        if (place.Length == 0)
            return false;

        var segments = place.Split('/');

        if (segments.Any(segment => segment.Length == 0 || segment[0] == '.') || _output.Contains(segments[0]))
            return false;

        relative = place;

        return true;
    }
}
