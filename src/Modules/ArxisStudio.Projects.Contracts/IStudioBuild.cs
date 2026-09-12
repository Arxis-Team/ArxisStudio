using System.Collections.Immutable;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Projects;

/// <summary>
/// Восстановление, сборка и очистка открытого.
/// </summary>
/// <remarks>
/// Служба одна на студию, живёт в том же модуле <c>arxis.projects</c> и берётся
/// <see cref="StudioProjectsAccess.Build"/>. Отдельным интерфейсом, а не методами модели: сборка
/// меняет диск, а не то, что проект говорит о себе, и подписчику модели её события не нужны. Так
/// же разведены <c>IVsSolution</c> и <c>IVsSolutionBuildManager</c> у Visual Studio.
/// <para>
/// <b>Одна полоса.</b> MSBuild держит на процесс общие кэши, окружение и единственную регистрацию,
/// поэтому операции идут той же очередью, что и загрузки: операция дождётся идущей загрузки, а
/// загрузка — операции. Двух операций сразу не бывает, и <see cref="Running"/> говорит, какая идёт.
/// </para>
/// <para>
/// <b>Потоки.</b> <see cref="Running"/> читается из любого потока, методы зовутся из любого, а
/// события приходят только в поток интерфейса. Они не склеиваются: у каждой операции свой номер, и
/// на каждое <see cref="Started"/> приходится ровно одно <see cref="Completed"/>.
/// </para>
/// <para>
/// <b>Провал — результат, а не исключение.</b> Проект не собрался, восстановление не прошло,
/// собирать нечего — это <see cref="ProjectOperationResult"/> с диагностиками, и они же уходят в
/// «Проблемы». Исключения остаются неверному виду операции, отмене и остановленной службе.
/// </para>
/// </remarks>
public interface IStudioBuild
{
    /// <summary>Операция, которая идёт сейчас; null — ни одной.</summary>
    /// <remarks>
    /// Стоящая в очереди — ещё не идущая: <see cref="Running"/> появляется вместе с
    /// <see cref="Started"/> и исчезает вместе с <see cref="Completed"/>.
    /// </remarks>
    ProjectOperation? Running { get; }

    /// <summary>
    /// Операция началась.
    /// </summary>
    /// <remarks>
    /// Приходит в поток интерфейса. Отписываются в <c>Deactivate</c>; забытая подписка снимется
    /// сама, когда контекст плагина станут выгружать, — но это страховка, а не способ.
    /// </remarks>
    event EventHandler<ProjectOperationEventArgs>? Started;

    /// <summary>
    /// Операция кончилась — итогом или отменой.
    /// </summary>
    /// <remarks>
    /// У отменённой <see cref="ProjectOperationEventArgs.IsCancelled"/> поднят, а итога нет: MSBuild
    /// останавливают между проектами, и сказать, что успело собраться, он уже не может.
    /// </remarks>
    event EventHandler<ProjectOperationEventArgs>? Completed;

    /// <summary>
    /// Восстанавливает, собирает, пересобирает или очищает открытое.
    /// </summary>
    /// <param name="kind">Что делать.</param>
    /// <param name="projects">
    /// Какие проекты; пусто — открытое целиком. Идентичности берутся из снимка текущей сессии.
    /// </param>
    /// <param name="progress">Куда говорить, что идёт; null — никуда. Студия своё покажет и так.</param>
    /// <param name="cancellationToken">Отмена: и ожидания, и самой операции.</param>
    /// <returns>Итог; провал приходит с диагностиками, а не исключением.</returns>
    /// <remarks>
    /// <b>Сборка начинается с восстановления.</b> Цель <c>Build</c> у MSBuild пакетов не
    /// восстанавливает — в отличие от <c>dotnet build</c>, который делает это сам, — поэтому
    /// <see cref="ProjectOperationKind.Build"/> и <see cref="ProjectOperationKind.Rebuild"/> сначала
    /// восстанавливают, и провал восстановления отменяет сборку: собирать по ненайденным пакетам
    /// значило бы показать человеку ошибки компилятора вместо настоящей причины.
    /// <para>
    /// <b>Удачное восстановление перечитывает модель.</b> Восстановление меняет то, из чего модель
    /// собирается, поэтому <see cref="ProjectOperationKind.Restore"/> кончается перезагрузкой с
    /// причиной <see cref="ProjectsLoadReason.Restore"/> — до того, как задача уйдёт с полосы.
    /// Сборка и очистка модель не трогают: файлы проекта они не меняют.
    /// </para>
    /// <para>
    /// Ничего не открыто — провал с <see cref="ProjectsDiagnosticCodes.NothingOpen"/>; названный
    /// проект не из текущего снимка — <see cref="ProjectsDiagnosticCodes.ProjectNotOpen"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Вид операции неизвестен.</exception>
    /// <exception cref="OperationCanceledException">Операция отменена: токеном или крестиком на задаче.</exception>
    /// <exception cref="ObjectDisposedException">Служба остановлена.</exception>
    Task<ProjectOperationResult> RunAsync(
        ProjectOperationKind kind,
        ImmutableArray<ProjectIdentity> projects = default,
        IProgress<ProjectOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
