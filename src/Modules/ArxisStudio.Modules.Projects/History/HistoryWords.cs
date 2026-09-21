using System.Globalization;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects.History;

/// <summary>Что история говорит человеку: метки своих действий и отказы отмены и возврата.</summary>
/// <param name="strings">Словари модуля.</param>
internal sealed class HistoryWords(IStudioStrings strings)
{
    /// <summary>Правка мимо студии, увиденная на ходу.</summary>
    public string External => strings["module.projects.history.external"];

    /// <summary>Правки, сделанные, пока студия не смотрела, — найденные опорным снимком.</summary>
    public string Offline => strings["module.projects.history.offline"];

    /// <summary>Метка отмены.</summary>
    /// <param name="label">Метка отменённого.</param>
    public string Undo(string label) => Format("module.projects.history.undo", label);

    /// <summary>История не ведётся.</summary>
    public string Off => strings["module.projects.history.off"];

    /// <summary>Действия нет в истории.</summary>
    public string Unknown => strings["module.projects.history.unknown"];

    /// <summary>Метку просят отменить.</summary>
    public string Label => strings["module.projects.history.label"];

    /// <summary>Действие уже отменено.</summary>
    /// <param name="label">Метка действия.</param>
    public string Undone(string label) => Format("module.projects.history.undone", label);

    /// <summary>Отменять нечего: всё уже так, как было до действия.</summary>
    /// <param name="label">Метка действия.</param>
    public string Nothing(string label) => Format("module.projects.history.nothing", label);

    /// <summary>Файл изменился с тех пор.</summary>
    /// <param name="path">Путь.</param>
    public string Changed(string path) => Format("module.projects.history.changed", path);

    /// <summary>Путь с тех пор пропал или переехал.</summary>
    /// <param name="path">Путь.</param>
    public string Gone(string path) => Format("module.projects.history.gone", path);

    /// <summary>На месте, куда возвращать, уже что-то лежит.</summary>
    /// <param name="path">Путь.</param>
    public string Occupied(string path) => Format("module.projects.history.occupied", path);

    /// <summary>Содержимое больше предела: вернуть нечем.</summary>
    /// <param name="path">Путь.</param>
    public string TooLarge(string path) => Format("module.projects.history.tooLarge", path);

    /// <summary>Файл больше предела: проверить, что его не меняли, нечем, и он оставлен.</summary>
    /// <param name="path">Путь.</param>
    public string Unverified(string path) => Format("module.projects.history.unverified", path);

    /// <summary>Содержимое в истории испорчено.</summary>
    /// <param name="path">Путь.</param>
    public string Corrupted(string path) => Format("module.projects.history.corrupted", path);

    /// <summary>Файл проекта менялся с тех пор, и ссылок, снятых удалением, не вернуть.</summary>
    /// <param name="path">Путь файла проекта.</param>
    public string References(string path) => Format("module.projects.history.references", path);

    /// <summary>Нынешний файл больше предела: после возврата его было бы не вернуть.</summary>
    /// <param name="path">Путь.</param>
    public string CurrentTooLarge(string path) => Format("module.projects.history.currentTooLarge", path);

    /// <summary>На месте файла — папка.</summary>
    /// <param name="path">Путь.</param>
    public string Folder(string path) => Format("module.projects.history.folder", path);

    /// <summary>Файл не прочесть: его держит другая программа.</summary>
    /// <param name="path">Путь.</param>
    public string Busy(string path) => Format("module.projects.history.busy", path);

    /// <summary>Путь вне папок проектов открытого решения — словами службы файлов.</summary>
    /// <param name="path">Путь.</param>
    public string Outside(string path) => Format("module.projects.files.outside", path);

    /// <summary>Диск отказал — словами службы файлов.</summary>
    /// <param name="reason">Что сказал диск.</param>
    public string Failed(string reason) => Format("module.projects.files.failed", reason);

    private string Format(string key, string value) =>
        string.Format(CultureInfo.CurrentCulture, strings[key], value);
}
