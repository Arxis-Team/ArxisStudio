namespace ArxisStudio.Sdk;

/// <summary>Насколько серьёзна находка.</summary>
public enum StudioProblemSeverity
{
    /// <summary>К сведению.</summary>
    Info,

    /// <summary>Предупреждение.</summary>
    Warning,

    /// <summary>Ошибка: сделать задуманное не вышло.</summary>
    Error,
}

/// <summary>Находка, которую студия показывает на панели «Проблемы».</summary>
/// <param name="Severity">Насколько серьёзна.</param>
/// <param name="Code">Устойчивый код вида <c>AXM3041</c>; по нему находку ищут.</param>
/// <param name="Message">Объяснение для человека.</param>
/// <param name="FilePath">Файл, о котором речь, или null.</param>
/// <param name="Line">Строка, считая с единицы; 0, если находка не точнее файла.</param>
/// <param name="Column">Столбец, считая с единицы; 0, если неизвестен.</param>
public sealed record StudioProblem(
    StudioProblemSeverity Severity,
    string Code,
    string Message,
    string? FilePath = null,
    int Line = 0,
    int Column = 0)
{
    /// <summary>Место находки одной строкой: имя файла и номер строки.</summary>
    public string Where => FilePath is not { Length: > 0 } path
        ? string.Empty
        : Line > 0
            ? $"{System.IO.Path.GetFileName(path)}:{Line}"
            : System.IO.Path.GetFileName(path);
}

/// <summary>
/// Панель «Проблемы» с той стороны, с которой в неё пишут.
/// </summary>
/// <remarks>
/// Находки приходят от разных: модель решения не собралась, разметка не
/// разобралась, плагин-проверяльщик нашёл своё. Каждый отвечает за свой список
/// целиком — <see cref="Report"/> заменяет всё, что источник сообщал прежде.
/// Иначе устаревшую находку пришлось бы снимать поимённо, а её ещё нужно
/// вспомнить: список после исправления должен просто перестать её показывать.
/// </remarks>
public interface IStudioProblems
{
    /// <summary>Все находки: сначала ошибки, потом предупреждения.</summary>
    IReadOnlyList<StudioProblem> All { get; }

    /// <summary>Список находок изменился.</summary>
    event EventHandler? Changed;

    /// <summary>Заменяет всё, что источник сообщал прежде.</summary>
    /// <param name="source">Кто сообщает; свой ключ у каждого документа и каждой проверки.</param>
    /// <param name="problems">Что он нашёл сейчас; пустой список снимает прежние находки.</param>
    void Report(string source, IEnumerable<StudioProblem> problems);
}
