using System.Diagnostics;
using System.Text;

namespace ArxisStudio.Services;

/// <summary>
/// Каталог шаблонов <c>dotnet new</c>: читает установленные шаблоны и создаёт по
/// ним проекты. Разбор идёт по позициям колонок из строки-разделителя, а не по
/// заголовкам — заголовки переведены на язык системы, а разделитель одинаков.
/// </summary>
public sealed class TemplateCatalog
{
    /// <summary>Возвращает установленные шаблоны.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<IReadOnlyList<ProjectTemplate>> ListAsync(CancellationToken cancellationToken = default)
    {
        var (exitCode, output, _) = await RunDotnetAsync(["new", "list"], null, cancellationToken);
        return exitCode == 0 ? Parse(output) : [];
    }

    /// <summary>
    /// Создаёт проект по шаблону и возвращает файл, который студии открывать:
    /// решение, если шаблон его создал, иначе — проект.
    /// </summary>
    /// <param name="template">Шаблон.</param>
    /// <param name="name">Имя проекта.</param>
    /// <param name="location">Папка, внутри которой создать проект.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<(string? EntryPoint, string? Error)> CreateAsync(
        ProjectTemplate template,
        string name,
        string location,
        CancellationToken cancellationToken = default)
    {
        var target = Path.Combine(location, name);

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            return (null, $"Папка уже существует и не пуста: {target}");

        var (exitCode, output, error) = await RunDotnetAsync(
            ["new", template.ShortName, "-o", target, "-n", name],
            location,
            cancellationToken);

        if (exitCode != 0)
            return (null, string.IsNullOrWhiteSpace(error) ? output : error);

        var entry =
            FirstFile(target, "*.sln") ??
            FirstFile(target, "*.slnx") ??
            FirstFile(target, "*.csproj");

        return entry is null
            ? (null, $"Шаблон отработал, но проект в {target} не найден")
            : (entry, null);
    }

    internal static IReadOnlyList<ProjectTemplate> Parse(string output)
    {
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        var separatorIndex = lines.FindIndex(IsSeparator);
        if (separatorIndex < 0)
            return [];

        var columns = ColumnRanges(lines[separatorIndex]);
        if (columns.Count < 2)
            return [];

        var templates = new List<ProjectTemplate>();

        foreach (var line in lines.Skip(separatorIndex + 1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var name = Cell(line, columns[0]);
            var shortNames = Cell(line, columns[1]);
            if (name.Length == 0 || shortNames.Length == 0)
                continue;

            var languages = columns.Count > 2 ? Cell(line, columns[2]) : string.Empty;
            var tags = columns.Count > 3 ? Cell(line, columns[3]) : string.Empty;

            templates.Add(new ProjectTemplate(
                name,
                shortNames.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0],
                languages.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim('[', ']'))
                    .ToList(),
                tags.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
        }

        return templates;
    }

    private static bool IsSeparator(string line) =>
        line.Length > 0 &&
        line.Contains("--", StringComparison.Ordinal) &&
        line.All(c => c is '-' or ' ');

    private static List<(int Start, int Length)> ColumnRanges(string separator)
    {
        var ranges = new List<(int, int)>();
        var start = -1;

        for (var i = 0; i < separator.Length; i++)
        {
            if (separator[i] == '-')
            {
                if (start < 0)
                    start = i;
            }
            else if (start >= 0)
            {
                ranges.Add((start, i - start));
                start = -1;
            }
        }

        if (start >= 0)
            ranges.Add((start, separator.Length - start));

        return ranges;
    }

    private static string Cell(string line, (int Start, int Length) column)
    {
        if (column.Start >= line.Length)
            return string.Empty;

        // Последняя колонка может быть шире разделителя, поэтому берём остаток строки.
        var available = Math.Min(column.Length, line.Length - column.Start);
        var text = line.Substring(column.Start, available);

        var tail = column.Start + column.Length;
        if (tail < line.Length)
        {
            var rest = line[tail..];
            if (rest.Length > 0 && rest[0] != ' ')
                text += rest.Split(' ')[0];
        }

        return text.Trim();
    }

    private static string? FirstFile(string directory, string pattern) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
                .OrderBy(p => p.Count(c => c is '/' or '\\'))
                .FirstOrDefault()
            : null;

    private static async Task<(int ExitCode, string Output, string Error)> RunDotnetAsync(
        IEnumerable<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(info);
            if (process is null)
                return (-1, string.Empty, "Не удалось запустить dotnet");

            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, await output, await error);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (-1, string.Empty, $"dotnet не найден: {e.Message}");
        }
    }
}
