using System.Globalization;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects.Files;

/// <summary>Что служба файлов говорит человеку: отказы и сбои, словами модуля.</summary>
/// <param name="strings">Словари модуля.</param>
internal sealed class FileWords(IStudioStrings strings)
{
    /// <summary>Путь вне папок проектов открытого решения.</summary>
    public string Outside(string path) => Format("module.projects.files.outside", path);

    /// <summary>Файл проекта, решение, папка проекта или выход сборки.</summary>
    public string Protected(string path) => Format("module.projects.files.protected", path);

    /// <summary>По пути назначения уже что-то лежит.</summary>
    public string Exists(string path) => Format("module.projects.files.exists", path);

    /// <summary>Папку просят перенести в неё саму.</summary>
    public string IntoItself(string path) => Format("module.projects.files.intoItself", path);

    /// <summary>Пути нет.</summary>
    public string Missing(string path) => Format("module.projects.files.missing", path);

    /// <summary>Диск отказал.</summary>
    public string Failed(string reason) => Format("module.projects.files.failed", reason);

    /// <summary>Ничего не открыто.</summary>
    public string NothingOpen => strings["module.projects.nothingOpen"];

    private string Format(string key, string value) =>
        string.Format(CultureInfo.CurrentCulture, strings[key], value);
}
