using ArxisStudio.Inspector;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.UiDesigner.Inspection;

/// <summary>
/// Картинки решения, которые форма может назвать: файлы проектов, ставшие ресурсами Avalonia, — и текст, которым их
/// пишет разметка формы.
/// </summary>
/// <remarks>
/// <para>
/// Ресурс — элемент <c>AvaloniaResource</c>: его сборка кладёт в себя, и <c>avares://</c> его находит; файл, лежащий в
/// папке проекта без отметки, собранная форма не найдёт, и в списке его нет. Путь в сборке — <c>Link</c> элемента, а без
/// него — путь от папки проекта, как его берёт Avalonia SDK и как его читает служба XAML.
/// </para>
/// <para>
/// Картинку своего проекта форма пишет от корня сборки — <c>/Assets/logo.png</c>, — чужого — полным адресом:
/// <c>avares://Shared/Assets/logo.png</c>. Свои идут первыми.
/// </para>
/// </remarks>
internal static class ProjectImages
{
    /// <summary>Типы членов, которые читают картинку по имени её ресурса (ADR 0031 Markup).</summary>
    public static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.Ordinal)
    {
        "Avalonia.Media.IImage",
        "Avalonia.Media.IImageBrushSource",
        "Avalonia.Media.Imaging.Bitmap",
        "Avalonia.Controls.WindowIcon",
    };

    /// <summary>Расширения картинок, которые читает декодер Avalonia.</summary>
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".webp"];

    /// <summary>Картинка ли это по расширению.</summary>
    /// <param name="path">Путь к файлу.</param>
    public static bool IsImage(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Картинки решения вариантами: значение — текст для разметки формы, подпись — имя файла, описание — путь к нему.
    /// </summary>
    /// <param name="solution">Снимок решения.</param>
    /// <param name="document">Файл формы: от его проекта зависит, как писать путь.</param>
    /// <param name="typed">Набранное для отбора: имя или путь содержит его; пусто — все.</param>
    public static IReadOnlyList<InspectorChoice> Choices(SolutionSnapshot solution, CanonicalPath document, string typed)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(typed);

        solution.TryGetProjectForFile(document, out var own);

        var found = new List<(bool Own, string Path, InspectorChoice Choice)>();

        foreach (var project in solution.Projects)
        {
            var assembly = AssemblyNameOf(project);
            var mine = own is not null && project.Identity.Equals(own.Identity);

            foreach (var item in project.Items)
            {
                if (!string.Equals(item.ItemType, "AvaloniaResource", StringComparison.OrdinalIgnoreCase) || item.FullPath.IsEmpty ||
                    !IsImage(item.FullPath.Value) || ResourcePath(project, item) is not { Length: > 0 } path)
                {
                    continue;
                }

                if (typed.Length > 0 &&
                    !path.Contains(typed, StringComparison.CurrentCultureIgnoreCase) &&
                    !item.FullPath.FileName.Contains(typed, StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                var text = mine ? "/" + path : $"avares://{assembly}/{path}";

                found.Add((mine, path, new InspectorChoice(text) { Title = item.FullPath.FileName, Description = item.FullPath.Value }));
            }
        }

        return
        [
            .. found
                .GroupBy(static entry => entry.Choice.Value, StringComparer.Ordinal)
                .Select(static group => group.First())
                .OrderByDescending(static entry => entry.Own)
                .ThenBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .Select(static entry => entry.Choice),
        ];
    }

    /// <summary>Имя сборки проекта — хозяин адреса <c>avares://</c>.</summary>
    private static string AssemblyNameOf(ProjectSnapshot project) =>
        project.Properties.TryGetValue("AssemblyName", out var assembly) && assembly.Length > 0 ? assembly : project.Name;

    /// <summary>Путь элемента в сборке: <c>Link</c>, а без него — путь от папки проекта; вне папки и без ссылки — нет.</summary>
    private static string? ResourcePath(ProjectSnapshot project, ProjectItem item)
    {
        if (item.Link is { Length: > 0 } link)
            return Normalise(link);

        return item.FullPath.StartsWith(project.ProjectDirectory)
            ? Normalise(item.FullPath.Value[project.ProjectDirectory.Value.Length..])
            : null;
    }

    private static string Normalise(string path) => path.Replace('\\', '/').TrimStart('/');
}
