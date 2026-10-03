using System.Globalization;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Xaml;

/// <summary>Что служба XAML говорит человеку: отказы, причины и имена шагов истории, словами модуля.</summary>
/// <param name="strings">Словари модуля.</param>
internal sealed class XamlWords(IStudioStrings strings)
{
    /// <summary>Шаг истории документа: текст, который пришёл из файла после чужой записи.</summary>
    public string ChangedOutside => strings["module.xaml.changedOutside"];

    /// <summary>Действие локальной истории: запись документа дизайнером.</summary>
    public string Save(string file) => Format("module.xaml.save", file);

    /// <summary>Зачем собирали, когда пересобрать попросили.</summary>
    public string Rebuild => strings["module.xaml.rebuild"];

    /// <summary>Почему нужен перезапуск.</summary>
    public string Restart(ProjectDesignRestartReason reason) => reason switch
    {
        ProjectDesignRestartReason.PackagesChanged => strings["module.xaml.restart.packages"],
        _ => strings["module.xaml.restart.held"],
    };

    /// <summary>Решение не открыто.</summary>
    public string NothingOpen => strings["module.xaml.nothingOpen"];

    /// <summary>Службы проектов нет.</summary>
    public string NoProjects => strings["module.xaml.noProjects"];

    /// <summary>Файл — не разметка.</summary>
    public string NotMarkup(string path) => Format("module.xaml.notMarkup", path);

    /// <summary>Файл не принадлежит решению.</summary>
    public string NotInSolution(string path) => Format("module.xaml.notInSolution", path);

    /// <summary>Поколение не поднялось.</summary>
    public string StartFailed(string reason) => Format("module.xaml.startFailed", reason);

    /// <summary>В дизайнере ничего не открыто.</summary>
    public string NoGeneration => strings["module.xaml.noGeneration"];

    /// <summary>Документ уже показан.</summary>
    public string AlreadyShown(string file) => Format("module.xaml.alreadyShown", file);

    /// <summary>Документ закрыт.</summary>
    public string Closed(string file) => Format("module.xaml.closed", file);

    /// <summary>Сохранение отказало.</summary>
    public string SaveRefused(string file, string reason) =>
        string.Format(CultureInfo.CurrentCulture, strings["module.xaml.saveRefused"], file, reason);

    /// <summary>Что держит замену.</summary>
    public string SwapHeld(string reasons) => Format("module.xaml.swapHeld", reasons);

    /// <summary>Текст не читается значением члена.</summary>
    public string ValueRejected(string text, string valueType) =>
        string.Format(CultureInfo.CurrentCulture, strings["module.xaml.valueRejected"], text, valueType);

    private string Format(string key, string value) =>
        string.Format(CultureInfo.CurrentCulture, strings[key], value);
}
