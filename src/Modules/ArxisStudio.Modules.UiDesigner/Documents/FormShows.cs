using System.Runtime.CompilerServices;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>Кто показывает форму — по старшинству: младший уступает старшему.</summary>
internal enum FormShowRank
{
    /// <summary>Фоновый снимок для плиток: уступает всем.</summary>
    Capture,

    /// <summary>Доска: уступает вкладке.</summary>
    Board,

    /// <summary>Вкладка формы: не уступает никому.</summary>
    Tab,
}

/// <summary>
/// Кто показывает форму: показ у документа один, а показать её хотят вкладка, доска и фоновый снимок.
/// </summary>
/// <remarks>
/// <para>
/// <b>Старший берёт, младший уступает.</b> Вкладка, открывшая форму, забирает её у доски и у снимка: им
/// говорится уступить (<see cref="FormHold"/> с отзывом), и вкладка ждёт, пока они отпустят показ. Доска
/// забирает форму у снимка, а форму вкладки не берёт вовсе — стоит её снимком. Отпустил старший — младшие
/// узнают об этом (<see cref="Freed"/>) и берут форму снова, если она им ещё нужна.
/// </para>
/// <para>
/// Один учёт на контекст модуля и всегда — даже без снимков: вкладке и доске делить показ нужно и тогда,
/// когда фоновой съёмки нет. Всё — в потоке интерфейса.
/// </para>
/// </remarks>
internal sealed class FormShows
{
    private static readonly ConditionalWeakTable<IStudioContext, FormShows> Owners = new();

    private readonly Dictionary<string, List<FormHold>> _holds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Форму отпустил тот, кто её держал: тот, кто ждал её, может взять снова.</summary>
    public event EventHandler<string>? Freed;

    /// <summary>Учёт показов этого контекста модуля.</summary>
    /// <param name="context">Контекст модуля.</param>
    public static FormShows Of(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Owners.GetValue(context, static _ => new FormShows());
    }

    /// <summary>
    /// Берёт показ формы, если никто старше его не держит; младших просит уступить.
    /// </summary>
    /// <param name="formPath">Путь к форме.</param>
    /// <param name="rank">Кто берёт.</param>
    /// <param name="yield">Что сделать, когда форму попросит старший: отпустить показ и освободить взятое.</param>
    /// <returns>Взятое; null — форму держит старший.</returns>
    public FormHold? TryHold(string formPath, FormShowRank rank, Action? yield = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(formPath);

        if (!_holds.TryGetValue(formPath, out var holds))
            _holds[formPath] = holds = [];

        if (holds.Any(hold => hold.Rank > rank))
            return null;

        var lower = holds.Where(hold => hold.Rank < rank).ToList();
        var hold = new FormHold(this, formPath, rank, yield, Task.WhenAll(lower.Select(held => held.Finished)));

        holds.Add(hold);

        foreach (var held in lower)
            held.Yield();

        return hold;
    }

    /// <summary>Держит ли форму кто-то старше названного.</summary>
    /// <param name="formPath">Путь к форме.</param>
    /// <param name="rank">С кем сравнивать.</param>
    public bool IsHeldAbove(string formPath, FormShowRank rank) =>
        _holds.TryGetValue(formPath, out var holds) && holds.Any(hold => hold.Rank > rank);

    /// <summary>Взятое освобождено.</summary>
    internal void Drop(FormHold hold)
    {
        if (!_holds.TryGetValue(hold.FormPath, out var holds) || !holds.Remove(hold))
            return;

        if (holds.Count == 0)
            _holds.Remove(hold.FormPath);

        Freed?.Invoke(this, hold.FormPath);
    }
}

/// <summary>
/// Показ формы, взятый у учёта (<see cref="FormShows"/>): пока он не освобождён, младшие форму не берут.
/// </summary>
internal sealed class FormHold : IDisposable
{
    private readonly FormShows _owner;
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action? _yield;
    private bool _disposed;

    internal FormHold(FormShows owner, string formPath, FormShowRank rank, Action? yield, Task released)
    {
        _owner = owner;
        _yield = yield;
        FormPath = formPath;
        Rank = rank;
        Released = released;
    }

    /// <summary>Путь к форме.</summary>
    public string FormPath { get; }

    /// <summary>Кто держит.</summary>
    public FormShowRank Rank { get; }

    /// <summary>Младшие отпустили показ: форму можно показывать.</summary>
    /// <remarks>Не падает никогда.</remarks>
    public Task Released { get; }

    /// <summary>Попросили ли уступить: форму взял старший.</summary>
    public bool Yielding { get; private set; }

    /// <summary>Кончается, когда взятое освобождено: старший, попросивший уступить, ждёт этого.</summary>
    internal Task Finished => _finished.Task;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _yield = null;
        _finished.TrySetResult();
        _owner.Drop(this);
    }

    /// <summary>Старший взял форму: держащий отпускает показ и освобождает взятое.</summary>
    internal void Yield()
    {
        if (_disposed || Yielding)
            return;

        Yielding = true;
        _yield?.Invoke();
    }
}
