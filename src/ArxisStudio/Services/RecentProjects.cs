using System.Text.Json;
using ArxisStudio.Shell;

namespace ArxisStudio.Services;

/// <summary>Проект, который студия уже открывала.</summary>
/// <param name="Path">Полный путь к <c>.sln</c>, <c>.slnx</c> или <c>.csproj</c>.</param>
/// <param name="OpenedAt">Когда его открывали в последний раз.</param>
public sealed record RecentProject(string Path, DateTimeOffset OpenedAt)
{
    /// <summary>Имя проекта без расширения.</summary>
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>Папка проекта.</summary>
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? Path;

    /// <summary>Существует ли файл проекта на диске.</summary>
    public bool Exists => File.Exists(Path);

    /// <summary>Одна-две буквы для плитки списка.</summary>
    public string Initials
    {
        get
        {
            var words = Name.Split(['.', '-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
            return words.Length switch
            {
                0 => "?",
                1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
                _ => string.Concat(words[0][..1], words[1][..1]).ToUpperInvariant(),
            };
        }
    }
}

/// <summary>
/// Список недавних проектов в файле пользователя. Порядок — от свежего к старому,
/// повторное открытие поднимает проект наверх, а не добавляет второй записью.
/// </summary>
public sealed class RecentProjects
{
    private const int Capacity = 20;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly List<RecentProject> _items;

    /// <summary>Создаёт список над файлом пользователя.</summary>
    /// <param name="path">Путь к файлу; по умолчанию — <see cref="StudioPaths.RecentProjectsFile"/>.</param>
    public RecentProjects(string? path = null)
    {
        _path = path ?? StudioPaths.RecentProjectsFile;
        _items = Load(_path);
    }

    /// <summary>Проекты от недавно открытого к давнему.</summary>
    public IReadOnlyList<RecentProject> Items => _items;

    /// <summary>Помечает проект открытым: поднимает его наверх списка.</summary>
    /// <param name="projectPath">Путь к файлу проекта или решения.</param>
    public void Touch(string projectPath)
    {
        var full = Path.GetFullPath(projectPath);
        _items.RemoveAll(p => string.Equals(p.Path, full, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, new RecentProject(full, DateTimeOffset.Now));

        if (_items.Count > Capacity)
            _items.RemoveRange(Capacity, _items.Count - Capacity);

        Save();
    }

    /// <summary>Убирает проект из списка, не трогая его на диске.</summary>
    /// <param name="projectPath">Путь к файлу проекта или решения.</param>
    public void Remove(string projectPath)
    {
        if (_items.RemoveAll(p => string.Equals(p.Path, projectPath, StringComparison.OrdinalIgnoreCase)) > 0)
            Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_items, Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Список недавних — удобство, а не данные пользователя.
        }
    }

    private static List<RecentProject> Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<List<RecentProject>>(File.ReadAllText(path), Options)
                       ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Испорченный файл заменится при первом же открытии проекта.
        }

        return [];
    }
}
