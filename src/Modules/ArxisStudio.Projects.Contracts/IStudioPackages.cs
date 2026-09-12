using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Projects;

/// <summary>
/// Пакеты NuGet открытого проекта: поставить, обновить, убрать.
/// </summary>
/// <remarks>
/// Служба одна на студию, живёт в том же модуле <c>arxis.projects</c> и берётся
/// <see cref="StudioProjectsAccess.Packages"/>. Отдельным интерфейсом, как у Visual Studio отделён
/// <c>INuGetProjectService</c>: правка ссылок — не модель и не сборка, и подписчику снимков она не
/// нужна.
/// <para>
/// <b>Что делает установка.</b> Правит файл проекта — или файл централизованных версий, если проект
/// ими управляется, — и восстанавливает пакеты. Провалившееся восстановление отменяет правку:
/// проект возвращается байт в байт, и об этом говорит диагностика. Удачная правка перечитывает
/// модель причиной <see cref="ProjectsLoadReason.Packages"/> — до того, как задача уйдёт с полосы.
/// </para>
/// <para>
/// <b>Установка поверх объявленного — это обновление.</b> Проект, уже назвавший пакет в своём файле,
/// получает новую версию, а не вторую ссылку на тот же пакет. Имена сравниваются без учёта регистра,
/// как их сравнивает NuGet.
/// </para>
/// <para>
/// <b>Ссылку из импорта служба не трогает.</b> <c>Directory.Build.props</c>,
/// <c>GlobalPackageReference</c> или SDK дают проекту ссылку, которой в его собственном файле нет, —
/// и редактор, правящий один файл, её там не найдёт. Такая просьба получает отказ с
/// <see cref="ProjectsDiagnosticCodes.ReferenceNotInProjectFile"/>, а не молчаливую правку не того
/// файла.
/// </para>
/// <para>
/// <b>Одна полоса.</b> Правка идёт той же очередью, что загрузки и сборки: восстановление в её конце
/// — работа того же MSBuild, у которого на процесс одни кэши и одна регистрация.
/// </para>
/// <para>
/// <b>Провал — результат, а не исключение.</b> Файла проекта нет, разметка не разобралась, менять
/// нечего, восстановление не прошло — это <see cref="ProjectOperationResult"/> с диагностиками.
/// Исключения остаются пустому имени пакета, отмене и остановленной службе.
/// </para>
/// </remarks>
public interface IStudioPackages
{
    /// <summary>
    /// Ставит пакет в проект или меняет версию уже объявленного.
    /// </summary>
    /// <param name="project">Какой проект; идентичность — из снимка текущей сессии.</param>
    /// <param name="packageId">Имя пакета, как его пишет NuGet.</param>
    /// <param name="version">
    /// Версия строкой и в том виде, в каком её понимает NuGet: <c>4.1.0</c>, <c>[1.0,2.0)</c>,
    /// <c>1.2.*</c>. Служба её не разбирает и не сравнивает — записывает как дано.
    /// </param>
    /// <param name="cancellationToken">Отмена: и ожидания, и самой работы.</param>
    /// <returns>Итог; провал приходит с диагностиками, а не исключением.</returns>
    /// <remarks>
    /// Ничего не открыто — отказ с <see cref="ProjectsDiagnosticCodes.NothingOpen"/>; проект не из
    /// текущего снимка — <see cref="ProjectsDiagnosticCodes.ProjectNotOpen"/>; ссылка пришла
    /// импортом — <see cref="ProjectsDiagnosticCodes.ReferenceNotInProjectFile"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">Имя пакета или версия пусты.</exception>
    /// <exception cref="OperationCanceledException">Отменено: токеном или крестиком на задаче.</exception>
    /// <exception cref="ObjectDisposedException">Служба остановлена.</exception>
    Task<ProjectOperationResult> InstallAsync(
        ProjectIdentity project,
        string packageId,
        string version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Убирает пакет из проекта.
    /// </summary>
    /// <param name="project">Какой проект; идентичность — из снимка текущей сессии.</param>
    /// <param name="packageId">Имя пакета.</param>
    /// <param name="cancellationToken">Отмена: и ожидания, и самой работы.</param>
    /// <returns>Итог; провал приходит с диагностиками, а не исключением.</returns>
    /// <remarks>
    /// Пакета в проекте нет — это не ошибка вызова, а результат «менять нечего»: правка ничего не
    /// пишет и восстановление не идёт.
    /// </remarks>
    /// <exception cref="ArgumentException">Имя пакета пусто.</exception>
    /// <exception cref="OperationCanceledException">Отменено: токеном или крестиком на задаче.</exception>
    /// <exception cref="ObjectDisposedException">Служба остановлена.</exception>
    Task<ProjectOperationResult> UninstallAsync(
        ProjectIdentity project,
        string packageId,
        CancellationToken cancellationToken = default);
}
