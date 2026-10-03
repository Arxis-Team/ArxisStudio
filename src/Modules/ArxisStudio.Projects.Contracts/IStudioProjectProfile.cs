using System.Collections.Immutable;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Projects;

/// <summary>Зачем открытое читают ещё раз, своей оценкой.</summary>
/// <remarks>Появилось в версии 1.7.</remarks>
public enum ProjectProfileKind
{
    /// <summary>
    /// Для дизайнера: сборки профиля пишут в <c>bin/ArxisStudio/</c> и <c>obj/ArxisStudio/</c>
    /// каждого проекта — рядом с выходом IDE, а не поверх него.
    /// </summary>
    /// <remarks>
    /// Рядом с дизайнером открыт Rider или Visual Studio, и они собирают те же проекты в те же
    /// папки. Запущенное ими приложение держит выход, который пишет сборка дизайнера, а две сборки,
    /// начатые вместе, пишут одну промежуточную папку. Восстановление у профиля общее с IDE:
    /// <c>obj/project.assets.json</c> читают обе стороны.
    /// </remarks>
    Design,
}

/// <summary>Какой профиль открыть.</summary>
/// <param name="Kind">Зачем.</param>
/// <remarks>Появилось в версии 1.7.</remarks>
public sealed record ProjectProfileRequest(ProjectProfileKind Kind)
{
    /// <summary>
    /// Свойства оценки, которые профилю прочесть сверх обычного — как
    /// <see cref="WorkspaceLoadOptions.AdditionalProperties"/>.
    /// </summary>
    /// <remarks>
    /// Профиль одного вида один на службу, и держатели делят его: читает он то, что просил хоть
    /// один. Новое имя перечитывает профиль — прежние держатели получат снимок, где его тоже видно.
    /// </remarks>
    public ImmutableArray<string> AdditionalProperties
    {
        get => field.IsDefault ? [] : field;
        init;
    }
}

/// <summary>
/// Своя оценка открытого: та же модель, прочитанная со своими глобальными свойствами, и операции
/// над ней.
/// </summary>
/// <remarks>
/// <para>
/// Берётся <see cref="IStudioProjects.OpenProfile"/> и держится, пока нужна: последний держатель,
/// отпустивший профиль, отпускает и его движок. Появилось в версии 1.7.
/// </para>
/// <para>
/// <b>Следует за открытым.</b> Профиль читает то же решение в той же конфигурации и перечитывается
/// следом за каждой загрузкой службы — по той же причине. Открыли другое решение — профиль читает
/// его, и <see cref="ProjectsStatus.Session"/> в <see cref="Status"/> сменилась вместе с номером
/// службы; закрыли — профиль закрыт. Держатель, которому решение важно, смотрит на номер сессии,
/// как и у службы.
/// </para>
/// <para>
/// <b>Та же полоса.</b> Загрузки и операции профиля идут очередью службы, за её загрузками и
/// операциями: MSBuild один на процесс, и второй его владелец гонялся бы с первым за общим
/// восстановлением. Человек видит их задачами студии, а журнал и <see cref="IStudioBuild"/> отличают
/// по <see cref="ProjectOperation.Profile"/>.
/// </para>
/// <para>
/// <b>Потоки</b> — как у службы: <see cref="Status"/> читается откуда угодно, методы зовутся откуда
/// угодно, <see cref="Changed"/> приходит в поток интерфейса.
/// </para>
/// </remarks>
public interface IStudioProjectProfile : IAsyncDisposable
{
    /// <summary>Зачем профиль.</summary>
    ProjectProfileKind Kind { get; }

    /// <summary>
    /// Глобальные свойства, с которыми профиль читает и собирает: у дизайна — папки выхода.
    /// </summary>
    /// <remarks>
    /// <see cref="ExecuteAsync"/> кладёт их в каждую операцию сам, поверх свойств запроса: сборка
    /// профиля не напишет поверх выхода IDE, даже если вызывающий их не назвал.
    /// </remarks>
    ProjectMetadata GlobalProperties { get; }

    /// <summary>Что профиль прочёл и в каком он состоянии — запись того же вида, что у службы.</summary>
    ProjectsStatus Status { get; }

    /// <summary>
    /// Состояние профиля сменилось.
    /// </summary>
    /// <remarks>
    /// Приходит в поток интерфейса, по порядку и со склейкой — как <see cref="IStudioProjects.Changed"/>.
    /// Отписываются до того, как отпустить профиль.
    /// </remarks>
    event EventHandler<ProjectsChangedEventArgs>? Changed;

    /// <summary>Перечитывает профиль.</summary>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    /// <returns>
    /// Итог загрузки. Ничего не открыто — провал с <see cref="ProjectsDiagnosticCodes.NothingOpen"/>.
    /// </returns>
    /// <remarks>
    /// Загрузка профиля, уже стоящая в очереди, не повторяется: вызов присоединяется к ней. Нужна
    /// после того, как диск поменял то, из чего собрана модель, — например, после восстановления.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Ожидание отменено.</exception>
    /// <exception cref="ObjectDisposedException">Профиль отпущен или служба остановлена.</exception>
    Task<WorkspaceLoadResult> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Восстанавливает, собирает или очищает — над оценкой профиля и его свойствами.
    /// </summary>
    /// <param name="request">
    /// Что делать: запрос строят по снимку профиля — его <see cref="SolutionSnapshot.Workspace"/> и
    /// идентичности его проектов.
    /// </param>
    /// <param name="progress">Куда говорить о ходе; null — никуда. Студия своё покажет и так.</param>
    /// <param name="cancellationToken">Отмена: и ожидания, и самой операции.</param>
    /// <returns>Итог; провал приходит с диагностиками.</returns>
    /// <remarks>
    /// <para>
    /// Запрос исполняется как есть: сборке профиль восстановления не добавляет — когда оно нужно,
    /// решает просящий. Удачное восстановление перечитывает модель службы, а за ней и профиль: диск
    /// у них общий.
    /// </para>
    /// <para>
    /// Запрос к прежней оценке — сессия сменилась — отказ <see cref="ProjectsDiagnosticCodes.ProjectNotOpen"/>;
    /// ничего не открыто — <see cref="ProjectsDiagnosticCodes.NothingOpen"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Запроса нет.</exception>
    /// <exception cref="OperationCanceledException">Операция отменена: токеном или крестиком на задаче.</exception>
    /// <exception cref="ObjectDisposedException">Профиль отпущен или служба остановлена.</exception>
    Task<ProjectOperationResult> ExecuteAsync(
        ProjectOperationRequest request,
        IProgress<ProjectOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
