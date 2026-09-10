using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Projects;

/// <summary>
/// Открытое решение или проект: модель, её состояние и то, чем их меняют.
/// </summary>
/// <remarks>
/// Берётся через <see cref="StudioProjectsAccess.Projects"/>. Служба одна на студию и живёт во
/// встроенном модуле <c>arxis.projects</c>; плагин, которому она нужна, объявляет модуль
/// зависимостью — тогда выдерживается и нижняя граница версии, и порядок подъёма.
/// <para>
/// <b>Потоки.</b> <see cref="Status"/> и <see cref="Current"/> читаются из любого потока без
/// замков: это одна неизменяемая запись, которая заменяется целиком. Методы зовутся из любого
/// потока. <see cref="Changed"/> приходит только в поток интерфейса.
/// </para>
/// <para>
/// <b>Порядок.</b> Сначала подписаться, потом прочитать <see cref="Status"/>: перемена,
/// случившаяся между чтением и подпиской, иначе прошла бы мимо. Событие, пришедшее после
/// чтения, может нести ту же запись — <see cref="ProjectsStatus.Sequence"/> скажет об этом:
/// номер не больше прочитанного — нового ничего.
/// </para>
/// <para>
/// <b>Провал — результат, а не исключение.</b> Нет файла, не тот вид файла, нет SDK, проект не
/// разобрался — всё это <see cref="WorkspaceLoadResult"/> с диагностиками, и то же самое лежит в
/// <see cref="ProjectsStatus.LastLoad"/>. Исключения остаются неверному аргументу, отмене и
/// остановленной службе.
/// </para>
/// <para>
/// <b>Долгое.</b> MSBuild читает проект секундами. Служба сама уходит с потока интерфейса и сама
/// показывает человеку задачу, которую можно отменить; вызывающему достаточно дождаться
/// результата. Чтение MSBuild прерывает между проектами, а не посреди проекта, поэтому отмена
/// может прийти не сразу.
/// </para>
/// </remarks>
public interface IStudioProjects
{
    /// <summary>Что открыто и в каком оно состоянии.</summary>
    ProjectsStatus Status { get; }

    /// <summary>Текущий снимок решения; null — показывать нечего.</summary>
    /// <remarks>То же, что <c>Status.Snapshot</c>, одним чтением.</remarks>
    SolutionSnapshot? Current { get; }

    /// <summary>
    /// Состояние службы сменилось.
    /// </summary>
    /// <remarks>
    /// Приходит в поток интерфейса и по порядку: <see cref="ProjectsStatus.Sequence"/> у
    /// <see cref="ProjectsChangedEventArgs.Current"/> от события к событию растёт. Промежуточные
    /// состояния склеиваются: пока поток интерфейса занят, служба копит перемены и отдаёт их
    /// одним событием, а разность в нём считается от прошлого доставленного состояния до нового.
    /// <para>
    /// Упавший обработчик соседям не мешает: остальные получат событие, а его исключение студия
    /// припишет тому, чей код бросил, — как всякое необработанное. Тяжёлого в обработчике не
    /// делают: он держит поток интерфейса, а долгой работе место в <see cref="IStudioTasks"/>.
    /// </para>
    /// <para>
    /// Отписываются в <c>Deactivate</c>. Забытая подписка снимется сама, когда контекст плагина
    /// станут выгружать, — но это страховка, а не способ.
    /// </para>
    /// </remarks>
    event EventHandler<ProjectsChangedEventArgs>? Changed;

    /// <summary>
    /// Открывает решение (<c>.sln</c>, <c>.slnx</c>) или проект (<c>*proj</c>).
    /// </summary>
    /// <param name="entryPoint">Абсолютный путь к файлу.</param>
    /// <param name="cancellationToken">Отмена открытия.</param>
    /// <returns>Итог загрузки; провал приходит с диагностиками, а не исключением.</returns>
    /// <remarks>
    /// Открытое по другому пути закрывается сразу, не дожидаясь нового: его работа отменяется,
    /// снимок уходит, а <see cref="ProjectsStatus.Session"/> меняется. Идентичности проектов у
    /// новой сессии свои — поэтому то, что должно пережить переоткрытие, храните по
    /// <see cref="ProjectSnapshot.ProjectFilePath"/>, а не по <see cref="ProjectIdentity"/>.
    /// <para>
    /// Тот же путь сессию не меняет: это перезагрузка, и идентичности проектов остаются прежними.
    /// Отменённое открытие новой сессии оставляет студию без проекта.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Путь пуст.</exception>
    /// <exception cref="OperationCanceledException">
    /// Открытие отменено: токеном, крестиком на задаче, другим открытием или закрытием.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Служба остановлена.</exception>
    Task<WorkspaceLoadResult> OpenAsync(CanonicalPath entryPoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Перечитывает открытое.
    /// </summary>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    /// <returns>
    /// Итог загрузки. Ничего не открыто — провал с <see cref="ProjectsDiagnosticCodes.NothingOpen"/>.
    /// </returns>
    /// <remarks>
    /// Загрузка, уже стоящая в очереди, не повторяется: вызов присоединяется к ней, и все ждущие
    /// получат один итог. Поэтому токен отменяет ожидание, а саму загрузку — только когда ждать
    /// её больше некому.
    /// <para>
    /// Провалившаяся перезагрузка прежний снимок не трогает: <see cref="Current"/> остаётся, а
    /// провал лежит в <see cref="ProjectsStatus.LastLoad"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Ожидание или сама загрузка отменены.</exception>
    /// <exception cref="ObjectDisposedException">Служба остановлена.</exception>
    Task<WorkspaceLoadResult> ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Меняет активную конфигурацию и перечитывает открытое под неё.
    /// </summary>
    /// <param name="configuration">
    /// Конфигурация — <c>Debug</c>, <c>Release</c>; null — та, которую проект выбирает сам.
    /// </param>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    /// <returns>
    /// Итог загрузки. Ничего не открыто — провал с <see cref="ProjectsDiagnosticCodes.NothingOpen"/>.
    /// </returns>
    /// <remarks>
    /// <see cref="ProjectsStatus.Configuration"/> меняется сразу, до загрузки: выбор принадлежит
    /// человеку, а не итогу чтения. Та же конфигурация ничего не перечитывает — вызов отдаёт итог
    /// последней загрузки или ждёт той, что уже идёт.
    /// <para>
    /// Допустимость не проверяется: MSBuild примет и конфигурацию, которой в решении нет, а
    /// известные перечислены в <see cref="SolutionSnapshot.Configurations"/>. Платформа и целевая
    /// среда остаются за проектом. Открытие другого решения конфигурацию не наследует.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Ожидание или сама загрузка отменены.</exception>
    /// <exception cref="ObjectDisposedException">Служба остановлена.</exception>
    Task<WorkspaceLoadResult> SetConfigurationAsync(string? configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Закрывает открытое.
    /// </summary>
    /// <returns>Задача, которая завершится, когда движок отпустит решение.</returns>
    /// <remarks>
    /// Без токена намеренно: закрытие обязано закончиться. <see cref="Status"/> становится
    /// <see cref="ProjectsState.Closed"/> сразу, а задача ждёт, пока MSBuild дочитает начатый
    /// проект. Закрыть закрытое — не ошибка.
    /// </remarks>
    Task CloseAsync();
}
