using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.UiDesigner.Model;

/// <summary>Файл разметки решения и проект, которому он принадлежит.</summary>
/// <param name="Path">Абсолютный путь.</param>
/// <param name="Project">Имя проекта.</param>
internal sealed record FormFile(CanonicalPath Path, string Project);

/// <summary>
/// Какие файлы решения могут быть формами: всё <c>.axaml</c>, что перечисляют проекты.
/// </summary>
/// <remarks>
/// Спрашивается снимок, а не диск: форма на доске — это форма проекта, и файл, лежащий в папке, но
/// исключённый из проекта, на доску не попадает, как не попадает он и в сборку. Вид элемента не
/// спрашивается: SDK Avalonia кладёт разметку в <c>AvaloniaXaml</c>, а проект, собранный руками, — куда
/// угодно, и узнаётся она по расширению.
/// <para>
/// Файл, общий для двух проектов, стоит на доске один раз — под первым из них в порядке решения: на
/// холсте он одна карточка с одним местом, а не две, которые тянули бы врозь.
/// </para>
/// </remarks>
internal static class FormFiles
{
    /// <summary>Расширение разметки Avalonia.</summary>
    public const string Extension = ".axaml";

    /// <summary>Файлы разметки снимка в порядке решения и пути.</summary>
    /// <param name="snapshot">Снимок решения.</param>
    public static IReadOnlyList<FormFile> Of(SolutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var seen = new HashSet<CanonicalPath>();
        var files = new List<FormFile>();

        foreach (var project in snapshot.Projects)
        {
            var own = project.Items
                .Select(item => item.FullPath)
                .Where(path => !path.IsEmpty && path.Extension.Equals(Extension, StringComparison.OrdinalIgnoreCase))
                .Where(seen.Add)
                .Order();

            files.AddRange(own.Select(path => new FormFile(path, project.Name)));
        }

        return files;
    }
}
