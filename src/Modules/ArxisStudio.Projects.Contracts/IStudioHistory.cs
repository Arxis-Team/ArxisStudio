using System.Collections.Immutable;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Projects;

/// <summary>Кто сделал действие локальной истории.</summary>
public enum LocalHistoryOrigin
{
    /// <summary>Студия — по просьбе человека или плагина: перенос, удаление, возврат, отмена.</summary>
    Studio,

    /// <summary>Кто-то мимо студии: другой редактор, система контроля версий, сборка.</summary>
    External,
}

/// <summary>Что случилось с путём.</summary>
public enum LocalHistoryChangeKind
{
    /// <summary>Путь появился.</summary>
    Created,

    /// <summary>Изменилось содержимое файла.</summary>
    Modified,

    /// <summary>Путь пропал.</summary>
    Deleted,

    /// <summary>Путь переехал или переименован; откуда — <see cref="LocalHistoryChange.From"/>.</summary>
    Moved,
}

/// <summary>
/// Содержимое файла в локальной истории — ручка, по которой его читают, а не сами байты.
/// </summary>
/// <param name="Id">Адрес содержимого.</param>
/// <remarks>
/// Байты читает <see cref="IStudioHistory.ReadAsync"/>: правок в истории тысячи, и нести содержимое
/// каждой вместе со списком значило бы читать с диска то, чего никто не откроет. Пустая ручка —
/// <see cref="None"/> — значит, что содержимого нет: его не было или история его не хранит.
/// </remarks>
public readonly record struct LocalHistoryContent(string Id)
{
    /// <summary>Содержимого нет.</summary>
    public static LocalHistoryContent None => default;

    /// <summary>Ручка пуста.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Id);
}

/// <summary>Одна правка пути внутри действия.</summary>
/// <remarks>
/// Содержимое до и после — ручками. Пустой ручки нет у правки, у которой содержимое было и история
/// его хранит; пуста она до появления, после удаления и у файла больше предела — об этом говорит
/// <see cref="TooLarge"/>, чтобы окно могло сказать честно, почему вернуть нечего.
/// </remarks>
public sealed record LocalHistoryChange
{
    /// <summary>Что случилось.</summary>
    public required LocalHistoryChangeKind Kind { get; init; }

    /// <summary>Путь — нынешний на момент правки, а у переезда — куда.</summary>
    public required CanonicalPath Path { get; init; }

    /// <summary>Откуда переехал; у остальных правок — <see cref="CanonicalPath.None"/>.</summary>
    public CanonicalPath From { get; init; }

    /// <summary>Содержимое до правки.</summary>
    public LocalHistoryContent Before { get; init; }

    /// <summary>Содержимое после правки.</summary>
    public LocalHistoryContent After { get; init; }

    /// <summary>Путь — папка.</summary>
    public bool IsDirectory { get; init; }

    /// <summary>Файл больше предела, и его содержимое история не хранит.</summary>
    public bool TooLarge { get; init; }
}

/// <summary>
/// Действие локальной истории: одна или несколько правок, случившихся вместе и под одной меткой.
/// </summary>
/// <remarks>
/// Переименование файла вместе с вложенным в него — одно действие: отменяют его тоже целиком.
/// Действие без правок — метка, поставленная человеком: отметка на времени, к которой потом
/// возвращаются.
/// </remarks>
public sealed record LocalHistoryAction
{
    /// <summary>Номер: растёт с каждым действием и не повторяется.</summary>
    public required long Id { get; init; }

    /// <summary>Когда.</summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>Метка для человека: «Переименование MainWindow.axaml», «Внешнее изменение».</summary>
    public required string Label { get; init; }

    /// <summary>Кто сделал.</summary>
    public required LocalHistoryOrigin Origin { get; init; }

    /// <summary>Правки по порядку; у метки — пусто.</summary>
    public ImmutableArray<LocalHistoryChange> Changes
    {
        get => field.IsDefault ? [] : field;
        init;
    }

    /// <summary>Какое действие это отменяет; null — это не отмена.</summary>
    public long? Undoes { get; init; }

    /// <summary>Действие отменено, и отмену не отменили: на диске его уже нет.</summary>
    /// <remarks>Так было, когда службу спросили; следующая отмена может это поменять.</remarks>
    public bool IsUndone { get; init; }

    /// <summary>Это метка, а не правка.</summary>
    public bool IsLabel => Changes.IsEmpty;
}

/// <summary>Строка истории пути: действие и то, что оно сделало с этим путём.</summary>
public sealed record LocalHistoryRevision
{
    /// <summary>Действие целиком.</summary>
    public required LocalHistoryAction Action { get; init; }

    /// <summary>
    /// Правки действия, задевшие путь: у файла — его собственная или переезд папки, в которой он
    /// лежал; у папки — всё, что в ней появилось, пропало, поменялось, приехало или уехало; у метки —
    /// пусто.
    /// </summary>
    public ImmutableArray<LocalHistoryChange> Changes
    {
        get => field.IsDefault ? [] : field;
        init;
    }
}

/// <summary>
/// Локальная история файлов решения: что с ними было, как вернуть и как отменить.
/// </summary>
/// <remarks>
/// <para>
/// Служба одна на студию, живёт в модуле <c>arxis.projects</c> и берётся
/// <see cref="StudioProjectsAccess.History"/>; появилась в версии 1.5. История — как Local History у
/// IntelliJ: студия пишет каждое своё действие над файлами и каждую правку, которую увидела на
/// диске, с содержимым до и после. Хранится она на машине, несколько дней, и к системе контроля
/// версий отношения не имеет.
/// </para>
/// <para>
/// <b>Отмена</b> (<see cref="UndoAsync"/>) возвращает всё действие: переименованное — под прежнее
/// имя, удалённое — на место с прежним содержимым, скопированное — прочь, правку — к тому, что было
/// до неё, и ссылки в файлах проектов — туда же. Отмена сама — действие истории, и её тоже можно
/// отменить. Затирать новое она не станет: путь, изменившийся с тех пор, — отказ
/// <see cref="ProjectsDiagnosticCodes.ChangedSince"/>, и не тронуто ничего. Файл, содержимого которого
/// история не хранит, отмена пропускает и говорит об этом предупреждением
/// <see cref="ProjectsDiagnosticCodes.NotStored"/>.
/// </para>
/// <para>
/// <b>Возврат</b> (<see cref="RevertAsync"/>) переписывает один файл содержимым из истории — тоже
/// новым действием, так что возврат отменяется, как всякое другое.
/// </para>
/// <para>
/// Правка идёт той же очередью, что у службы файлов: проверки до первого байта, откат сделанного,
/// если диск отказал посередине, перечитывание модели, когда правка могла её поменять, и
/// <see cref="IStudioFiles.Changed"/> о том, что переехало и что удалено. Провал — результат с
/// диагностиками, а не исключение.
/// </para>
/// </remarks>
public interface IStudioHistory
{
    /// <summary>Ведётся ли история сейчас: включена, открылась и не занята другой студией.</summary>
    bool IsOn { get; }

    /// <summary>Самый большой файл, чьё содержимое история хранит, в байтах.</summary>
    /// <remarks>Файл больше предела история помнит — появился, пропал, переехал, — но вернуть его нечем.</remarks>
    long MaxFileBytes { get; }

    /// <summary>
    /// Последнее действие студии, которое можно отменить, — то, что отменит Ctrl+Z в окне проекта.
    /// </summary>
    /// <remarks>
    /// Действие этого запуска студии, над файлами открытого решения, не метка, не отмена и ещё не
    /// отменённое. Правки мимо студии сюда не попадают: их отменяют из окна истории, выбрав. Null —
    /// отменять нечего.
    /// </remarks>
    LocalHistoryAction? LastStudioAction { get; }

    /// <summary>История пути от нового к старому — вместе с метками, которые на нём видно.</summary>
    /// <param name="path">Файл или папка — в том числе удалённые.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Строки истории; история не ведётся — пусто.</returns>
    /// <remarks>
    /// История файла идёт сквозь его переименования и переезды — и сквозь переезды папки, в которой он
    /// лежал. Папкой путь считается, если он папка на диске или был ею в истории.
    /// </remarks>
    Task<IReadOnlyList<LocalHistoryRevision>> RevisionsAsync(CanonicalPath path, CancellationToken cancellationToken = default);

    /// <summary>Читает содержимое из истории.</summary>
    /// <param name="content">Ручка из правки.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Байты; null — ручка пуста, содержимого нет или оно испорчено.</returns>
    Task<byte[]?> ReadAsync(LocalHistoryContent content, CancellationToken cancellationToken = default);

    /// <summary>Отменяет действие целиком — новым действием «Отмена: …».</summary>
    /// <param name="actionId">Номер действия.</param>
    /// <param name="cancellationToken">Отмена — пока правка не началась.</param>
    /// <returns>
    /// Итог. Отказ — <see cref="ProjectsDiagnosticCodes.ChangedSince"/> (путь изменился с тех пор или
    /// действие уже отменено), <see cref="ProjectsDiagnosticCodes.HistoryUnavailable"/> (истории нет,
    /// действия в ней нет или это метка), <see cref="ProjectsDiagnosticCodes.OutsideProjects"/> (путь не
    /// из открытого решения). Удача может нести предупреждения о пропущенном.
    /// </returns>
    /// <exception cref="OperationCanceledException">Отменено до начала.</exception>
    /// <exception cref="ObjectDisposedException">Служба остановлена.</exception>
    Task<ProjectOperationResult> UndoAsync(long actionId, CancellationToken cancellationToken = default);

    /// <summary>Переписывает файл содержимым из истории, а пропавший — заводит заново.</summary>
    /// <param name="path">Файл.</param>
    /// <param name="content">Содержимое из правки.</param>
    /// <param name="label">Метка действия для человека: «Возврат MainWindow.axaml к 10:05».</param>
    /// <param name="cancellationToken">Отмена — пока правка не началась.</param>
    /// <returns>
    /// Итог. Отказ — <see cref="ProjectsDiagnosticCodes.NotStored"/> (содержимого нет в истории или
    /// нынешний файл больше предела, и после возврата его было бы не вернуть),
    /// <see cref="ProjectsDiagnosticCodes.OutsideProjects"/> (путь не из открытого решения).
    /// </returns>
    /// <exception cref="ArgumentException">Путь, ручка или метка пусты.</exception>
    /// <exception cref="OperationCanceledException">Отменено до начала.</exception>
    /// <exception cref="ObjectDisposedException">Служба остановлена.</exception>
    Task<ProjectOperationResult> RevertAsync(
        CanonicalPath path,
        LocalHistoryContent content,
        string label,
        CancellationToken cancellationToken = default);

    /// <summary>Ставит метку на открытом решении — её видно в истории всего, что лежит в его папке.</summary>
    /// <param name="label">Текст метки.</param>
    /// <param name="cancellationToken">Отмена — пока метка не записана.</param>
    /// <returns>Итог; ничего не открыто или истории нет — отказ.</returns>
    /// <exception cref="ArgumentException">Текст пуст.</exception>
    /// <exception cref="OperationCanceledException">Отменено до начала.</exception>
    /// <exception cref="ObjectDisposedException">Служба остановлена.</exception>
    Task<ProjectOperationResult> PutLabelAsync(string label, CancellationToken cancellationToken = default);

    /// <summary>В истории прибавилось: действие студии, правка мимо неё, отмена или метка.</summary>
    /// <remarks>Приходит в поток интерфейса; частые правки склеиваются в одно событие.</remarks>
    event EventHandler? Changed;
}
