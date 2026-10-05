using System.Runtime.CompilerServices;
using ArxisStudio.Sdk;
using Avalonia.Threading;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>Кто показывает форму — по старшинству: младший уступает старшему.</summary>
internal enum FormShowRank
{
    /// <summary>Фоновый снимок для плиток: уступает всем.</summary>
    Capture,

    /// <summary>
    /// Вкладка формы, ушедшая с экрана: показ держит, пока он никому не нужен, — уступает доске, но не снимку.
    /// </summary>
    HiddenTab,

    /// <summary>Доска: уступает вкладке на экране.</summary>
    Board,

    /// <summary>Вкладка формы на экране: не уступает никому.</summary>
    Tab,
}

/// <summary>
/// Кто показывает форму: показ у документа один, а показать её хотят вкладка, доска и фоновый снимок.
/// </summary>
/// <remarks>
/// <para>
/// <b>Старший берёт, младший уступает.</b> Вкладка на экране забирает форму у доски и у снимка: им говорится
/// уступить (<see cref="FormHold"/> с отзывом), и вкладка ждёт, пока они отпустят показ. Доска забирает форму
/// у снимка и у вкладки, ушедшей с экрана, а у вкладки на экране не берёт вовсе — стоит её снимком. Отпустил
/// старший — младшие узнают об этом (<see cref="Freed"/>) и берут форму снова, если она им ещё нужна.
/// </para>
/// <para>
/// <b>Старшинство живое.</b> Вкладка, ушедшая с экрана, опускает свою заявку (<see cref="Rerank"/>) и показ
/// держит дальше: вернётся на экран — форма стоит, а не строится заново. Взять его может только доска, у
/// которой форма на виду, — ей говорят о том же <see cref="Freed"/>.
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
    private readonly HashSet<string> _dressed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Форму отпустил тот, кто её держал, или опустил ниже: тот, кто ждал её, может взять снова.
    /// </summary>
    /// <remarks>
    /// Приходит после прохода диспетчера, а не сразу: отпускают посреди чужого взятия — старший берёт форму,
    /// младший уступает, — и тот, кто взялся бы за неё по этому слову, вошёл бы в учёт, пока взятие ещё не
    /// записано за взявшим, и начал бы второй показ документа.
    /// </remarks>
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
    /// <remarks>
    /// Взятое ждёт и тех, кто уже уходит (<see cref="FormHold.Yielding"/>), — того же старшинства тоже: их показ
    /// ещё идёт, а служба второго показа документа не даст. Старший отказывает и уходя: его показ ещё стоит, а
    /// снимок, который не ждёт взятого, наткнулся бы на него.
    /// </remarks>
    public FormHold? TryHold(string formPath, FormShowRank rank, Action? yield = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(formPath);

        if (!_holds.TryGetValue(formPath, out var holds))
            _holds[formPath] = holds = [];

        if (holds.Any(hold => hold.Rank > rank))
            return null;

        var lower = holds.Where(hold => hold.Rank < rank && !hold.Yielding).ToList();
        var leaving = holds.Where(hold => hold.Rank < rank || hold.Yielding).Select(held => held.Finished);
        var hold = new FormHold(this, formPath, rank, yield, Task.WhenAll(leaving));

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

    /// <summary>Переставляет взятое: вкладка ушла с экрана или вернулась на него.</summary>
    /// <param name="hold">Взятое.</param>
    /// <param name="rank">Новое старшинство.</param>
    /// <remarks>
    /// Опущенное говорит <see cref="Freed"/>: тот, кто теперь старше его и ждёт форму, берёт её. Уходящее и
    /// освобождённое не переставляются.
    /// </remarks>
    public void Rerank(FormHold hold, FormShowRank rank)
    {
        ArgumentNullException.ThrowIfNull(hold);

        if (hold.Yielding
            || hold.Rank == rank
            || !_holds.TryGetValue(hold.FormPath, out var holds)
            || !holds.Contains(hold))
        {
            return;
        }

        var lowered = rank < hold.Rank;

        hold.Rank = rank;

        if (lowered)
            Announce(hold.FormPath);
    }

    /// <summary>Пришло ли к последнему показу формы её приложение.</summary>
    /// <param name="formPath">Путь к форме.</param>
    /// <remarks>
    /// Приложение служба строит на каждый показ и отдаёт после корня. Форма, у которой оно было, встаёт на
    /// новом месте вместе с ним, а не на миг без его стилей (<see cref="FormSession"/>).
    /// </remarks>
    public bool IsDressed(string formPath) => _dressed.Contains(formPath);

    /// <summary>Запоминает, пришло ли к показу формы её приложение.</summary>
    /// <param name="formPath">Путь к форме.</param>
    /// <param name="dressed">Пришло.</param>
    public void Dress(string formPath, bool dressed)
    {
        ArgumentException.ThrowIfNullOrEmpty(formPath);

        if (dressed)
            _dressed.Add(formPath);
        else
            _dressed.Remove(formPath);
    }

    /// <summary>Взятое освобождено.</summary>
    internal void Drop(FormHold hold)
    {
        if (!_holds.TryGetValue(hold.FormPath, out var holds) || !holds.Remove(hold))
            return;

        if (holds.Count == 0)
            _holds.Remove(hold.FormPath);

        Announce(hold.FormPath);
    }

    /// <summary>Говорит <see cref="Freed"/> после прохода диспетчера.</summary>
    private void Announce(string formPath) => Dispatcher.UIThread.Post(() => Freed?.Invoke(this, formPath));
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
    /// <remarks>Меняет его учёт (<see cref="FormShows.Rerank"/>).</remarks>
    public FormShowRank Rank { get; internal set; }

    /// <summary>Младшие и уходящие отпустили показ: форму можно показывать.</summary>
    /// <remarks>Не падает никогда.</remarks>
    public Task Released { get; }

    /// <summary>
    /// Уходит ли взятое: форму взял старший — или держащий её отпустил, а показ, начатый под ним, ещё идёт.
    /// </summary>
    /// <remarks>Уходящее своим больше не считают: нового показа под ним не начинают, а новые заявки ждут его.</remarks>
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

    /// <summary>
    /// Держащий отпустил форму, а показ, начатый под взятым, ещё идёт: взятое уходит, когда он кончится.
    /// </summary>
    internal void Retire()
    {
        if (!_disposed)
            Yielding = true;
    }
}
