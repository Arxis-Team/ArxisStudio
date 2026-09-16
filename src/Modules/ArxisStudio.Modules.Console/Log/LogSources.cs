using System.Collections.Immutable;

namespace ArxisStudio.Modules.Console.Log;

/// <summary>
/// Отбор по источнику: кого из пишущих в журнал не показывать.
/// </summary>
/// <remarks>
/// Названы спрятанные, а не показанные, и это решение, а не оборот речи. Источник — строка,
/// которую называет пишущий, и появиться новый может в любой миг: плагин просыпается щелчком,
/// сборка начинается через полчаса после запуска. Отбор, перечисляющий показанных, спрятал бы
/// такого новичка молча — человек не выбрал его лишь потому, что выбирать было нечего.
/// Перечисляющий спрятанных отвечает иначе: не показан только тот, кого назвали.
/// <para>
/// Значимая структура с равенством по содержимому: отбор сравнивают целиком — «изменился ли он с
/// прошлого перестроения», — и множество внутри сравнивается как множество, а не как ссылка.
/// <c>default</c> не прячет никого, поэтому «показаны все» — это ноль байт и ни одного
/// построенного множества.
/// </para>
/// </remarks>
public readonly struct LogSources : IEquatable<LogSources>
{
    /// <summary>
    /// Пустое множество, которым меряются имена источников.
    /// </summary>
    /// <remarks>
    /// Порядковое сравнение, а не культурное: источник называет пишущий, и <c>Console</c> с
    /// <c>console</c> — разные пишущие, как их ни сравнивай в турецкой локали.
    /// </remarks>
    private static readonly ImmutableHashSet<string> None =
        ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    private readonly ImmutableHashSet<string>? _hidden;

    private LogSources(ImmutableHashSet<string> hidden) => _hidden = hidden;

    /// <summary>Отбор, не прячущий никого, — с него панель начинает.</summary>
    public static LogSources All => default;

    /// <summary>Не спрятан никто.</summary>
    public bool ShowsAll => Hidden.IsEmpty;

    /// <summary>Сколько источников спрятано.</summary>
    public int HiddenCount => Hidden.Count;

    private ImmutableHashSet<string> Hidden => _hidden ?? None;

    /// <summary>Равны ли отборы.</summary>
    /// <param name="left">Первый.</param>
    /// <param name="right">Второй.</param>
    public static bool operator ==(LogSources left, LogSources right) => left.Equals(right);

    /// <summary>Различны ли отборы.</summary>
    /// <param name="left">Первый.</param>
    /// <param name="right">Второй.</param>
    public static bool operator !=(LogSources left, LogSources right) => !left.Equals(right);

    /// <summary>
    /// Отбор, в котором показан один источник из названных.
    /// </summary>
    /// <param name="source">Кого оставить.</param>
    /// <param name="known">Кто вообще писал в журнал к этому мигу.</param>
    /// <returns>Отбор, прячущий всех известных, кроме <paramref name="source"/>.</returns>
    /// <exception cref="ArgumentNullException">Любой из доводов равен <c>null</c>.</exception>
    /// <remarks>
    /// Прячутся известные, а не «все остальные»: множества всех источников не существует — оно
    /// пополняется по мере того, как расширения просыпаются и пишут. Источник, появившийся после
    /// этого выбора, будет показан, и это то самое обещание, ради которого отбор перечисляет
    /// спрятанных.
    /// </remarks>
    public static LogSources Only(string source, IEnumerable<string> known)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(known);

        return new LogSources(None.Union(known.Where(other =>
            !string.Equals(other, source, StringComparison.Ordinal))));
    }

    /// <summary>Показывают ли записи источника.</summary>
    /// <param name="source">Имя источника.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> равен <c>null</c>.</exception>
    public bool Shows(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return !Hidden.Contains(source);
    }

    /// <summary>Отбор, в котором источник показан.</summary>
    /// <param name="source">Имя источника.</param>
    /// <returns>Новый отбор; сам он неизменяем.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> равен <c>null</c>.</exception>
    public LogSources Show(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new LogSources(Hidden.Remove(source));
    }

    /// <summary>Отбор, в котором источник спрятан.</summary>
    /// <param name="source">Имя источника.</param>
    /// <returns>Новый отбор; сам он неизменяем.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> равен <c>null</c>.</exception>
    public LogSources Hide(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new LogSources(Hidden.Add(source));
    }

    /// <summary>Отбор, в котором источник поменял сторону.</summary>
    /// <param name="source">Имя источника.</param>
    /// <returns>Новый отбор; сам он неизменяем.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> равен <c>null</c>.</exception>
    public LogSources Toggle(string source) => Shows(source) ? Hide(source) : Show(source);

    /// <inheritdoc/>
    public bool Equals(LogSources other) => Hidden.SetEquals(other.Hidden);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LogSources other && Equals(other);

    /// <inheritdoc/>
    /// <remarks>
    /// Складывается сложением по модулю два: множество есть множество, и порядок обхода не должен
    /// менять его отпечаток.
    /// </remarks>
    public override int GetHashCode()
    {
        var hash = Hidden.Count;

        foreach (var source in Hidden)
            hash ^= StringComparer.Ordinal.GetHashCode(source);

        return hash;
    }
}
