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

    /// <summary>
    /// Путь вне правки файлов: за пределами папок проектов открытого решения, в выходе сборки, или
    /// это сам файл проекта, решение или папка проекта — их правят вместе с решением.
    /// </summary>
    public const string OutsideProjects = "PRJ1004";

    /// <summary>По пути назначения уже что-то лежит.</summary>
    public const string TargetExists = "PRJ1005";

    /// <summary>Папку просят перенести или скопировать в неё саму.</summary>
    public const string IntoItself = "PRJ1006";

    /// <summary>Того, что просят переместить, скопировать или удалить, нет — или нет папки назначения.</summary>
    public const string Missing = "PRJ1007";

    /// <summary>Диск отказал посреди правки: файл занят, прав нет. Сделанное откатывается, где это возможно.</summary>
    public const string FileOperationFailed = "PRJ1008";

    /// <summary>
    /// Локальная история не ведётся — выключена, не открылась или её держит другая студия, — или
    /// названного действия в ней нет: его сняла очистка, или это метка, а метку отменять нечего.
    /// </summary>
    public const string HistoryUnavailable = "PRJ1009";

    /// <summary>
    /// Путь изменился с тех пор, как действие его оставило: отмена затёрла бы новое. Так же — действие
    /// уже отменено.
    /// </summary>
    public const string ChangedSince = "PRJ1010";

    /// <summary>
    /// Содержимого нет в истории: файл больше предела или объект испорчен. Отмена такой файл пропускает
    /// и предупреждает, возврат — отказывает.
    /// </summary>
    public const string NotStored = "PRJ1011";
}
