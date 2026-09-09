using ArxisStudio.Sdk;
using Avalonia.Media;

namespace ArxisStudio.Modules.Console.Problems;

/// <summary>
/// Строка находки на экране.
/// </summary>
/// <remarks>
/// Неизменяемая, в отличие от строки журнала: список находок источник заменяет
/// целиком, поэтому строке нечего менять в себе — её просто строят заново.
/// </remarks>
public sealed class ProblemRow
{
    /// <summary>Заводит строку по находке.</summary>
    /// <param name="problem">Находка.</param>
    /// <param name="tint">
    /// Цвет уровня; null — обычный цвет текста.
    /// </param>
    /// <remarks>
    /// Кисть приходит снаружи, а не выбирается здесь: цвет принадлежит теме, а
    /// какой из них взять — уровню находки. Панель спрашивает кисти у темы один
    /// раз и раздаёт их строкам, потому что строк много, а тема одна.
    /// </remarks>
    public ProblemRow(StudioProblem problem, IBrush? tint = null)
    {
        ArgumentNullException.ThrowIfNull(problem);

        Problem = problem;
        Text = FirstLine(problem.Message);
        Tint = tint;
    }

    /// <summary>Цвет значка уровня; null — обычный цвет текста.</summary>
    public IBrush? Tint { get; }

    /// <summary>Сама находка — её отдаёт панель подробностей.</summary>
    public StudioProblem Problem { get; }

    /// <summary>Устойчивый код находки.</summary>
    public string Code => Problem.Code;

    /// <summary>Первая строка объяснения; остальное показывают подробности.</summary>
    public string Text { get; }

    /// <summary>Место находки: имя файла и строка.</summary>
    public string Where => Problem.Where;

    /// <summary>Ошибка.</summary>
    public bool IsError => Problem.Severity == StudioProblemSeverity.Error;

    /// <summary>Предупреждение.</summary>
    public bool IsWarning => Problem.Severity == StudioProblemSeverity.Warning;

    /// <summary>Замечание к сведению.</summary>
    public bool IsInfo => Problem.Severity == StudioProblemSeverity.Info;

    /// <summary>
    /// Есть ли файл, который можно открыть.
    /// </summary>
    /// <remarks>
    /// Находка без файла — обычное дело: так сообщают о том, что относится ко
    /// всему проекту. Открывать в ней нечего, и предлагать это не следует.
    /// </remarks>
    public bool HasFile => Problem.FilePath is { Length: > 0 };

    /// <summary>
    /// Строка целиком — тем же видом, каким её показывает таблица.
    /// </summary>
    /// <remarks>
    /// Этим текстом строку называет программа чтения с экрана: без него она
    /// читает имя класса, и все находки звучат одинаково.
    /// </remarks>
    public override string ToString() =>
        Where is { Length: > 0 } where ? $"{Code} {Text} {where}" : $"{Code} {Text}";

    private static string FirstLine(string message)
    {
        if (message is not { Length: > 0 })
            return string.Empty;

        var end = message.IndexOfAny(['\r', '\n']);

        return end < 0 ? message : message[..end];
    }
}
