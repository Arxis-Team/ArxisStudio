using ArxisStudio.Sdk;

namespace ArxisStudio.Services;

/// <summary>
/// Находки студии: то, что показывает панель «Проблемы».
/// </summary>
/// <remarks>
/// Список один на всю студию, а источников много, и каждый отвечает за свой
/// участок целиком. Порядок хранения — порядок появления источников: в пределах
/// одной серьёзности находки не переставляются сами собой, иначе строка
/// уезжала бы из-под курсора при каждой перепроверке соседнего файла.
/// </remarks>
public sealed class StudioProblems : IStudioProblems
{
    private readonly Dictionary<string, IReadOnlyList<StudioProblem>> _bySource = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    private List<StudioProblem>? _flattened;

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public IReadOnlyList<StudioProblem> All => _flattened ??= Flatten();

    /// <inheritdoc/>
    public void Report(string source, IEnumerable<StudioProblem> problems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(problems);

        var found = problems.ToList();

        if (found.Count == 0)
        {
            if (!_bySource.Remove(source))
                return;

            _order.Remove(source);
        }
        else
        {
            if (!_bySource.ContainsKey(source))
                _order.Add(source);

            _bySource[source] = found;
        }

        _flattened = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }


    private List<StudioProblem> Flatten() =>
        [.. _order
            .SelectMany(source => _bySource[source])
            .OrderByDescending(problem => problem.Severity)];
}
