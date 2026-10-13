using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Xaml.Documents;

/// <summary>Ключ, записанный в файле словаря: где именно и для какой темы.</summary>
/// <param name="Key">Ключ.</param>
/// <param name="ThemeVariant">Тема словаря, в котором ключ; пусто — у всех тем.</param>
/// <param name="File">Файл.</param>
/// <param name="KeySpan">Где в файле записан ключ.</param>
internal sealed record FileResource(string Key, string? ThemeVariant, CanonicalPath File, TextSpan KeySpan);

/// <summary>
/// Словари в файлах решения: файл приложения формы и словари, которые включают он и форма, — разобранные и
/// запомненные до перемены файла.
/// </summary>
/// <remarks>
/// <para>
/// Включение ведёт в проект решения по сборке своего адреса: <c>avares://Сборка/Styles/Colors.axaml</c> — в папку
/// проекта этой сборки, <c>/Styles/Colors.axaml</c> — от корня сборки включающего файла, <c>Colors.axaml</c> — от него
/// самого. Словарь пакета или чужой сборки в файлах решения не найти, и его ключей здесь нет: их знает только объект,
/// который его загрузил.
/// </para>
/// <para>Всё — в потоке интерфейса: файлы словарей маленькие, а разобранное помнится до перемены файла.</para>
/// </remarks>
internal sealed class ResourceFiles
{
    private const string Scheme = "avares://";

    private readonly Dictionary<string, (DateTime Written, XamlDocument Document)> _read = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ключи файла и словарей, которые он включает, — каждый с файлом, где записан, его первым.</summary>
    /// <param name="file">Файл словаря или приложения.</param>
    /// <param name="snapshot">Решение: в его проектах ищутся включённые файлы.</param>
    public List<FileResource> Of(CanonicalPath file, SolutionSnapshot? snapshot)
    {
        var found = new List<FileResource>();

        Walk(file, snapshot, found, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        return found;
    }

    /// <summary>Ключи словарей, которые включает документ, — без его собственных: те говорит его синтаксис.</summary>
    /// <param name="document">Документ.</param>
    /// <param name="file">Его файл.</param>
    /// <param name="snapshot">Решение.</param>
    public List<FileResource> IncludedBy(XamlDocument document, CanonicalPath file, SolutionSnapshot? snapshot)
    {
        var found = new List<FileResource>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { file.Value };

        foreach (var included in Includes(document, file, snapshot))
            Walk(included, snapshot, found, visited);

        return found;
    }

    private void Walk(CanonicalPath file, SolutionSnapshot? snapshot, List<FileResource> found, HashSet<string> visited)
    {
        if (!visited.Add(file.Value) || Read(file) is not { } document)
            return;

        foreach (var declaration in document.GetResourceDeclarations())
        {
            if (declaration.IsTextKey)
                found.Add(new FileResource(declaration.Key, declaration.ThemeVariant, file, declaration.KeySpan));
        }

        foreach (var included in Includes(document, file, snapshot))
            Walk(included, snapshot, found, visited);
    }

    /// <summary>Файлы решения, которые документ включает: словари и стили.</summary>
    private static IEnumerable<CanonicalPath> Includes(XamlDocument document, CanonicalPath file, SolutionSnapshot? snapshot)
    {
        if (snapshot is null || !snapshot.TryGetProjectForFile(file, out var project))
            yield break;

        var assembly = AssemblyOf(project);
        var relative = Path.GetRelativePath(project.ProjectDirectory.Value, file.Value).Replace('\\', '/');
        var including = new Uri(Scheme + assembly + "/" + relative);

        foreach (var reference in document.GetResourceReferences())
        {
            var source = reference.SourceText.Trim();
            Uri? address = null;

            if (source.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
                Uri.TryCreate(source, UriKind.Absolute, out address);
            else if (source.StartsWith('/'))
                Uri.TryCreate(Scheme + assembly + source, UriKind.Absolute, out address);
            else if (source.Length > 0)
                Uri.TryCreate(including, source, out address);

            if (address is not null && Locate(address, snapshot) is { } found)
                yield return found;
        }
    }

    /// <summary>Файл решения по адресу ресурса: проект сборки адреса и путь в его папке; null — не в решении.</summary>
    private static CanonicalPath? Locate(Uri address, SolutionSnapshot snapshot)
    {
        if (XamlUri.GetAssemblyName(address) is not { } assembly ||
            snapshot.Projects.FirstOrDefault(project => string.Equals(AssemblyOf(project), assembly, StringComparison.Ordinal)) is not { } owner)
        {
            return null;
        }

        var path = CanonicalPath.Create(owner.ProjectDirectory, Uri.UnescapeDataString(address.AbsolutePath).TrimStart('/'));

        return File.Exists(path.Value) ? path : null;
    }

    private static string AssemblyOf(ProjectSnapshot project) =>
        project.Properties.TryGetValue("AssemblyName", out var assembly) && assembly.Length > 0 ? assembly : project.Name;

    /// <summary>Разобранный файл — тот же, пока файл не переписали; null — не прочесть.</summary>
    private XamlDocument? Read(CanonicalPath file)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(file.Value);

            if (_read.TryGetValue(file.Value, out var known) && known.Written == written)
                return known.Document;

            var document = XamlDocument.Parse(File.ReadAllText(file.Value));

            _read[file.Value] = (written, document);

            return document;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
