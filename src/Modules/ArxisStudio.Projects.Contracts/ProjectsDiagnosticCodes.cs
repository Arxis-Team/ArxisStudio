namespace ArxisStudio.Projects;

/// <summary>
/// Коды диагностик самой службы проектов.
/// </summary>
/// <remarks>
/// Приставка своя, <c>PRJ</c>: <c>APS</c> принадлежит библиотеке модели и её движкам, а <c>AXS</c>
/// занят учебными находками модуля «Пример». Кода, которого служба не выдаёт, здесь нет: объявить
/// его значило бы дать обещание, которого никто не держит.
/// </remarks>
public static class ProjectsDiagnosticCodes
{
    /// <summary>Перечитывать или собирать нечего: ничего не открыто.</summary>
    public const string NothingOpen = "PRJ1001";

    /// <summary>Названного проекта нет в текущем снимке: он не открыт или открыт в другой сессии.</summary>
    public const string ProjectNotOpen = "PRJ1002";

    /// <summary>Ссылка на пакет пришла импортом, и в файле проекта её нет: править нечего.</summary>
    public const string ReferenceNotInProjectFile = "PRJ1003";
}
