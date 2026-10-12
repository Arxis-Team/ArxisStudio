using System.Collections.Immutable;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.Xaml.Session;

/// <summary>
/// Классы проекта, которыми документ может задать данные: каталог живого поколения и то, что объявляют файлы кода.
/// </summary>
/// <remarks>
/// <para>
/// Каталог (<see cref="XamlTypeCatalog"/>) знает построенное — именами, без типов, — а файлы знают и то, что ещё не
/// собрано: модель, которую только что написали. Файл кода читается текстом (<see cref="CodeClasses"/>) и помнится по
/// размеру и времени записи: окно проекта спрашивает о нём на каждом движении тяги.
/// </para>
/// <para>Зовётся из любого потока: память о файлах — под замком.</para>
/// </remarks>
internal sealed class DataTypes
{
    private readonly Lock _gate = new();
    private readonly Dictionary<CanonicalPath, (long Size, DateTime Written, IReadOnlyList<DeclaredClass> Classes)> _files = [];

    /// <summary>Классы, которые объявляет файл кода; пусто — его нет, он не прочёлся или классов в нём нет.</summary>
    /// <param name="file">Файл.</param>
    public IReadOnlyList<DeclaredClass> In(CanonicalPath file)
    {
        FileInfo info;

        try
        {
            info = new FileInfo(file.Value);

            if (!info.Exists || !string.Equals(info.Extension, ".cs", StringComparison.OrdinalIgnoreCase))
                return [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }

        lock (_gate)
        {
            if (_files.TryGetValue(file, out var known) && known.Size == info.Length && known.Written == info.LastWriteTimeUtc)
                return known.Classes;
        }

        IReadOnlyList<DeclaredClass> classes;

        try
        {
            classes = CodeClasses.Read(File.ReadAllText(file.Value));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        lock (_gate)
            _files[file] = (info.Length, info.LastWriteTimeUtc, classes);

        return classes;
    }

    /// <summary>
    /// Классы файлов кода проекта и проектов, на которые он ссылается: полное имя — как объявлен и в каких файлах.
    /// </summary>
    /// <param name="snapshot">Решение.</param>
    /// <param name="project">Проект документа.</param>
    public IReadOnlyDictionary<string, (DeclaredClass Class, ImmutableArray<CanonicalPath> Files)> Declared(
        SolutionSnapshot snapshot, ProjectSnapshot project)
    {
        var declared = new Dictionary<string, (DeclaredClass Class, ImmutableArray<CanonicalPath> Files)>(StringComparer.Ordinal);

        foreach (var member in Closure(snapshot, project))
        {
            foreach (var item in member.Items.Where(static item => item.ItemType == ProjectItemTypes.Compile))
            {
                foreach (var found in In(item.FullPath))
                {
                    declared[found.FullName] = declared.TryGetValue(found.FullName, out var known)
                        ? (known.Class, known.Files.Contains(item.FullPath) ? known.Files : known.Files.Add(item.FullPath))
                        : (found, [item.FullPath]);
                }
            }
        }

        return declared;
    }

    /// <summary>Сводит построенное с объявленным: сперва каталог, затем то, чего он ещё не знает.</summary>
    /// <param name="catalog">Каталог живого поколения.</param>
    /// <param name="declared">Объявленное файлами кода.</param>
    public static IReadOnlyList<XamlDataTypeInfo> Merge(
        XamlTypeCatalog catalog, IReadOnlyDictionary<string, (DeclaredClass Class, ImmutableArray<CanonicalPath> Files)> declared)
    {
        var types = new List<XamlDataTypeInfo>();

        foreach (var entry in catalog.Entries)
            types.Add(Built(entry, declared.TryGetValue(entry.FullName, out var known) ? known.Files : []));

        foreach (var (fullName, (found, files)) in declared)
        {
            if (catalog.Find(fullName) is null)
                types.Add(NotBuilt(found, files));
        }

        return types;
    }

    /// <summary>Построенный класс — сведениями каталога.</summary>
    /// <param name="entry">Запись каталога.</param>
    /// <param name="files">Где он объявлен.</param>
    public static XamlDataTypeInfo Built(XamlTypeEntry entry, ImmutableArray<CanonicalPath> files) => new(
        entry.FullName,
        entry.Name,
        entry.ClrNamespace,
        entry.XmlNamespace,
        entry.SuggestedPrefix,
        IsBuilt: true,
        CanCreate: entry.Kinds.HasFlag(XamlTypeKinds.Creatable),
        IsData: entry.Kinds.HasFlag(XamlTypeKinds.Data),
        IsControl: entry.Kinds.HasFlag(XamlTypeKinds.Control),
        IsFormClass: entry.Kinds.HasFlag(XamlTypeKinds.CompiledMarkup),
        Notifies: entry.Kinds.HasFlag(XamlTypeKinds.Notifies),
        IsConverter: entry.Kinds.HasFlag(XamlTypeKinds.Converter),
        IsTemplate: entry.Kinds.HasFlag(XamlTypeKinds.DataTemplate),
        files);

    /// <summary>
    /// Класс, которого живое поколение ещё не знает, — тем, что сказал текст: создать можно всякий не статический, не
    /// абстрактный и не обобщённый, а кто он, скажет сборка.
    /// </summary>
    /// <param name="found">Объявление.</param>
    /// <param name="files">Где он объявлен.</param>
    public static XamlDataTypeInfo NotBuilt(DeclaredClass found, ImmutableArray<CanonicalPath> files) => new(
        found.FullName,
        found.Name,
        found.Namespace,
        "using:" + found.Namespace,
        null,
        IsBuilt: false,
        CanCreate: !found.IsStatic && !found.IsAbstract && !found.IsGeneric,
        IsData: true,
        IsControl: false,
        IsFormClass: false,
        Notifies: false,
        IsConverter: false,
        IsTemplate: false,
        files);

    /// <summary>Проект и те, на которые он ссылается, — каждый один раз.</summary>
    private static IEnumerable<ProjectSnapshot> Closure(SolutionSnapshot snapshot, ProjectSnapshot project)
    {
        var seen = new HashSet<CanonicalPath>();
        var queue = new Queue<ProjectSnapshot>([project]);

        while (queue.TryDequeue(out var next))
        {
            if (!seen.Add(next.ProjectFilePath))
                continue;

            yield return next;

            foreach (var reference in next.ProjectReferences)
            {
                if (snapshot.TryGetProject(reference.ProjectFilePath, out var referenced))
                    queue.Enqueue(referenced);
            }
        }
    }
}
