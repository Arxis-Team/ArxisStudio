namespace ArxisStudio.Services;

/// <summary>
/// Что студии сказали в командной строке.
/// </summary>
/// <remarks>
/// Аргумент у студии один и тот же во всех случаях — путь к решению или проекту: так её зовут из
/// проводника, из терминала и из «Открыть с помощью». Ключи (<c>--что-нибудь</c>) пропускаются: их
/// разбирают те, кому они адресованы, а незнакомый ключ — не повод не запуститься.
/// </remarks>
public static class StudioArguments
{
    private static readonly string[] Solutions = [".sln", ".slnx"];

    /// <summary>
    /// Находит в аргументах путь к решению или проекту.
    /// </summary>
    /// <param name="arguments">Аргументы командной строки; null — их нет.</param>
    /// <returns>Путь или жалоба; и то и другое пусто, когда проекта не называли.</returns>
    /// <remarks>
    /// Решает первый аргумент, не похожий на ключ: студия открывает одно, и второй путь ей некуда
    /// деть. Путь приводится к полному — относительный считается от папки, из которой студию
    /// позвали.
    /// </remarks>
    public static ProjectArgument Project(IEnumerable<string>? arguments)
    {
        if (arguments is null)
            return ProjectArgument.None;

        foreach (var argument in arguments)
        {
            if (string.IsNullOrWhiteSpace(argument) || argument.StartsWith('-'))
                continue;

            return Judge(argument);
        }

        return ProjectArgument.None;
    }

    /// <summary>
    /// Похож ли путь на то, что студия умеет открыть.
    /// </summary>
    /// <param name="path">Путь к файлу.</param>
    /// <remarks>
    /// Решение — <c>.sln</c> и <c>.slnx</c>, проект — всё, что кончается на <c>proj</c>: так их
    /// называют и MSBuild, и сама модель проектов, и список этот растёт не у студии.
    /// </remarks>
    public static bool IsProject(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var extension = System.IO.Path.GetExtension(path);

        return Solutions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase);
    }

    private static ProjectArgument Judge(string argument)
    {
        string path;

        try
        {
            path = System.IO.Path.GetFullPath(argument);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new ProjectArgument(null, $"{argument}: это не путь");
        }

        if (!IsProject(path))
            return new ProjectArgument(null, $"{path}: не решение и не проект");

        return File.Exists(path)
            ? new ProjectArgument(path, null)
            : new ProjectArgument(null, $"{path}: файла нет");
    }
}

/// <summary>Проект, который студии велели открыть, — или причина, почему открывать нечего.</summary>
/// <param name="Path">Полный путь к решению или проекту; null — открывать нечего.</param>
/// <param name="Complaint">Что сказать человеку; null — говорить не о чем.</param>
public sealed record ProjectArgument(string? Path, string? Complaint)
{
    /// <summary>Проекта в командной строке не называли — и это не ошибка.</summary>
    public static ProjectArgument None { get; } = new(null, null);
}
