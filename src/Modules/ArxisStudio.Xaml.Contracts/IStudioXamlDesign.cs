using System.Collections.Immutable;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Xaml;

/// <summary>
/// Поколение типов проекта, в котором строятся документы: состояние, сборка дизайна и замена.
/// </summary>
/// <remarks>
/// <para>
/// <b>Своя сборка.</b> Типы проекта собираются профилем дизайна службы проектов — в
/// <c>bin/ArxisStudio/</c> и <c>obj/ArxisStudio/</c>, рядом с выходом IDE, а не поверх него, — и
/// загружаются в выгружаемый контекст. Сохранённый код собирается после паузы, и новое поколение
/// заменяет прежнее.
/// </para>
/// <para>
/// <b>Замена ждёт только дизайнер.</b> Всё, что посреди дела, — жест на холсте, недописанное значение,
/// диалог, тяга — придерживает её (<see cref="Defer"/>); замена идёт, как только отпущена последняя
/// отсрочка. Держатели построенного отпускают его по просьбе (<see cref="Register"/>). Не ушло прежнее
/// поколение — нового нет: только новый процесс покажет типы как есть, и служба просит студию о
/// перезапуске.
/// </para>
/// <para>
/// <b>Потоки.</b> Методы зовутся из потока интерфейса, события и участники зовутся там же.
/// </para>
/// </remarks>
public interface IStudioXamlDesign
{
    /// <summary>Где поколение сейчас.</summary>
    XamlDesignState State { get; }

    /// <summary>
    /// Почему: что держит замену, почему поколения нет или почему нужен перезапуск; null — сказать
    /// нечего.
    /// </summary>
    string? StateReason { get; }

    /// <summary>Последняя сборка дизайна этого решения; null — ещё не было.</summary>
    XamlBuildOutcome? LastBuild { get; }

    /// <summary>Сдвинулись <see cref="State"/>, <see cref="StateReason"/> или <see cref="LastBuild"/>.</summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Ставит участника: на замену поколения он отпускает построенное из прежнего и берёт построенное
    /// из нового.
    /// </summary>
    /// <param name="participant">Участник.</param>
    /// <returns>Снятие участника.</returns>
    /// <remarks>
    /// Участники зовутся по порядку постановки; упавший соседям не мешает. Показы отпускают корни
    /// раньше участников и берут новые раньше них — участник, взявшийся за своё, видит уже новый корень.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Участника нет.</exception>
    IDisposable Register(IXamlDesignParticipant participant);

    /// <summary>Придерживает замену поколения, пока отсрочка не отпущена.</summary>
    /// <param name="reason">Что идёт — тому, кто спросит, почему типы ещё прежние.</param>
    /// <returns>Отсрочка; отпущенная дважды отпускает один раз.</returns>
    /// <remarks>
    /// Отсрочки переживают решение: взятая до открытия документа держит и поколение, которое поднимется
    /// потом.
    /// </remarks>
    /// <exception cref="ArgumentException">Причина пуста.</exception>
    IDisposable Defer(string reason);

    /// <summary>Собирает типы проекта сейчас и заменяет поколение, как только замену ничто не держит.</summary>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Что сделала сборка.</returns>
    /// <exception cref="InvalidOperationException">Поколения нет: ни один документ решения не открыт.</exception>
    Task<XamlBuildOutcome> RebuildAsync(CancellationToken cancellationToken = default);
}

/// <summary>Где поколение типов проекта.</summary>
public enum XamlDesignState
{
    /// <summary>Поколения нет: ни один документ решения не открыт, или решение закрыто.</summary>
    Idle,

    /// <summary>Поколение поднимается: оценка дизайна, сборка устаревшего, загрузка.</summary>
    Starting,

    /// <summary>Поколение живое и свежее.</summary>
    Live,

    /// <summary>Идёт сборка дизайна; поколение пока живое.</summary>
    Building,

    /// <summary>Типы собраны заново, и поколение заменится, как только отпущены отсрочки.</summary>
    SwapPending,

    /// <summary>Поколение заменяется.</summary>
    Swapping,

    /// <summary>Прежнее поколение не ушло или пакеты сменили версию: только новый процесс покажет типы.</summary>
    RestartRequired,

    /// <summary>
    /// Проект собран против другого старшего номера Avalonia, чем у студии: поколения нет, документы
    /// открываются текстом.
    /// </summary>
    Unsupported,

    /// <summary>Поколение не поднялось: решение не прочлось профилем дизайна или подъём упал.</summary>
    Failed,
}

/// <summary>Что сделала сборка дизайна.</summary>
/// <param name="Succeeded">Собралось.</param>
/// <param name="Reason">Зачем собирали.</param>
/// <param name="Diagnostics">Что сказала сборка.</param>
/// <param name="TypesChanged">Сборка переписала то, из чего загружено живое поколение, — его заменят.</param>
/// <param name="Duration">Сколько шла.</param>
public sealed record XamlBuildOutcome(
    bool Succeeded,
    string Reason,
    ImmutableArray<ProjectDiagnostic> Diagnostics,
    bool TypesChanged,
    TimeSpan Duration);

/// <summary>
/// Держатель построенного из поколения — холст, выбор, инспектор, — отпускающий это на замену.
/// </summary>
/// <remarks>
/// Поколение уходит, только когда в процессе его не держит никто, а заглянуть внутрь чужого держателя
/// служба не может. Поэтому каждый, кто держит корень, контрол, тип или член поколения, ставится
/// участником и отпускает всё по просьбе. Оба вызова — в потоке интерфейса.
/// <para>
/// Службу внутри них не ждут: замена держит её очередь, пока участники не вернутся, и ожидание показа,
/// открытия или пересборки отсюда не кончилось бы никогда. Нужное после замены начинают, не дожидаясь.
/// </para>
/// </remarks>
public interface IXamlDesignParticipant
{
    /// <summary>
    /// Отпускает всё построенное из нынешнего поколения: показанные корни, выбранные контролы,
    /// запомненные типы. Холст здесь замораживает последний кадр — человек видит формы, а не пустоту.
    /// </summary>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Задача, кончающаяся, когда из поколения не держится ничего.</returns>
    ValueTask ReleaseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Берёт новое поколение: показы уже стоят на нём. Не зовётся, если прежнее не ушло, — тогда
    /// показывается замороженное, пока студия не перезапустится.
    /// </summary>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Задача, кончающаяся, когда участник показывает новое.</returns>
    ValueTask RestoreAsync(CancellationToken cancellationToken);
}
