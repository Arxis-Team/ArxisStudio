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

    /// <summary>
    /// Снимает всё, что сообщало расширение.
    /// </summary>
    /// <remarks>
    /// Зовётся, когда расширение выгружают. Само оно этого уже не сделает, а
    /// находки без источника висели бы в панели до конца сеанса: исправить их
    /// некому, перепроверить некому, убрать нечем.
    /// <para>
    /// Узнаются они по имени источника: у расширения оно начинается с его
    /// идентификатора — так же, как имя панели в раскладке и имя элемента в
    /// полосе. Ставит приставку не расширение, а тот, кто выдал ему контекст.
    /// </para>
    /// </remarks>
    /// <param name="pluginId">Кто ушёл.</param>
    public void RemoveOwnedBy(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        var prefix = Owned(pluginId, string.Empty);
        var mine = _order.Where(source => source.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        if (mine.Count == 0)
            return;

        foreach (var source in mine)
        {
            _bySource.Remove(source);
            _order.Remove(source);
        }

        _flattened = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Имя источника с хозяином впереди.</summary>
    /// <param name="pluginId">Хозяин.</param>
    /// <param name="source">Как источник назвал себя сам.</param>
    public static string Owned(string pluginId, string source) => $"{pluginId}:{source}";

    private List<StudioProblem> Flatten() =>
        [.. _order
            .SelectMany(source => _bySource[source])
            .OrderByDescending(problem => problem.Severity)];
}
