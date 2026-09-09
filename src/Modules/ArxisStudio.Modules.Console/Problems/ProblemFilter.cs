using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Console.Problems;

/// <summary>
/// Отбор находок: какие уровни показывать и что искать.
/// </summary>
/// <remarks>
/// Устроен так же, как отбор журнала, и по тем же соображениям: значимая
/// структура, живёт в панели, с сеансом сбрасывается. Источника здесь нет —
/// у находки его не видно: имя источника внутреннее, с приставкой владельца,
/// и показывать его человеку незачем.
/// </remarks>
/// <param name="Info">Показывать замечания.</param>
/// <param name="Warning">Показывать предупреждения.</param>
/// <param name="Error">Показывать ошибки.</param>
/// <param name="Query">Что искать в коде, объяснении и месте; пусто — всё.</param>
public readonly record struct ProblemFilter(bool Info, bool Warning, bool Error, string Query)
{
    /// <summary>Отбор, пропускающий всё, — с него панель начинает.</summary>
    public static ProblemFilter Everything { get; } = new(true, true, true, string.Empty);

    /// <summary>Подходит ли находка.</summary>
    /// <param name="problem">Находка.</param>
    public bool Matches(StudioProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        if (!Allows(problem.Severity))
            return false;

        if (Query is not { Length: > 0 } query)
            return true;

        return problem.Code.Contains(query, StringComparison.OrdinalIgnoreCase)
            || problem.Message.Contains(query, StringComparison.OrdinalIgnoreCase)
            || problem.Where.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private bool Allows(StudioProblemSeverity severity) => severity switch
    {
        StudioProblemSeverity.Error => Error,
        StudioProblemSeverity.Warning => Warning,
        _ => Info,
    };
}

/// <summary>
/// Сколько находок каждого уровня сообщено.
/// </summary>
/// <remarks>
/// Как и у журнала, считается по всему списку, а не по показанному.
/// </remarks>
/// <param name="Info">Замечаний.</param>
/// <param name="Warning">Предупреждений.</param>
/// <param name="Error">Ошибок.</param>
public readonly record struct ProblemCounts(int Info, int Warning, int Error)
{
    /// <summary>Считает находки по уровням.</summary>
    /// <param name="problems">Весь список находок.</param>
    public static ProblemCounts Of(IReadOnlyList<StudioProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        var counts = new ProblemCounts();

        foreach (var problem in problems)
        {
            counts = problem.Severity switch
            {
                StudioProblemSeverity.Error => counts with { Error = counts.Error + 1 },
                StudioProblemSeverity.Warning => counts with { Warning = counts.Warning + 1 },
                _ => counts with { Info = counts.Info + 1 },
            };
        }

        return counts;
    }
}
