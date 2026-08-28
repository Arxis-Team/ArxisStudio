using System.Text.Json.Serialization;

namespace ArxisStudio.Sdk.Plugins;

/// <summary>
/// Манифест плагина или встроенного модуля (<c>plugin.json</c> / <c>module.json</c>).
/// Хост читает его, не загружая сборку: по нему строятся меню, списки и ассоциации
/// файлов, а сама сборка поднимается только при первом событии активации.
/// </summary>
public sealed class PluginManifest
{
    /// <summary>Идентификатор вида <c>vendor.name</c>; совпадает с именем папки плагина.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Отображаемое имя.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Версия плагина.</summary>
    public string Version { get; set; } = "0.0.0";

    /// <summary>Автор или организация.</summary>
    public string? Publisher { get; set; }

    /// <summary>Краткое описание для менеджера плагинов.</summary>
    public string? Description { get; set; }

    /// <summary>Путь к иконке относительно папки плагина.</summary>
    public string? Icon { get; set; }

    /// <summary>Требования к версии SDK.</summary>
    public PluginSdkRequirement? Sdk { get; set; }

    /// <summary>Путь к entry-сборке относительно папки плагина.</summary>
    public string? Entry { get; set; }

    /// <summary>Что плагин добавляет в студию.</summary>
    public PluginContributions Contributions { get; set; } = new();

    /// <summary>События, при которых сборка плагина загружается.</summary>
    public IList<string> Activation { get; set; } = [];
}

/// <summary>Требования плагина к версии SDK.</summary>
public sealed class PluginSdkRequirement
{
    /// <summary>Минимальная версия SDK.</summary>
    public string Min { get; set; } = "1.0";
}

/// <summary>
/// Декларативная часть плагина. Всё перечисленное здесь хост показывает до
/// загрузки сборки — иначе список плагинов означал бы загрузку их всех.
/// </summary>
public sealed class PluginContributions
{
    /// <summary>Команды плагина.</summary>
    public IList<PluginCommand> Commands { get; set; } = [];

    /// <summary>Пункты меню, вызывающие команды.</summary>
    public IList<PluginMenuItem> Menus { get; set; } = [];

    /// <summary>Панели плагина.</summary>
    public IList<PluginToolWindow> ToolWindows { get; set; } = [];

    /// <summary>Типы файлов, которые плагин берётся открывать.</summary>
    public IList<PluginFileType> FileTypes { get; set; } = [];

    /// <summary>Настройки плагина, показываемые на экране Settings.</summary>
    public IList<PluginSetting> Settings { get; set; } = [];
}

/// <summary>Команда плагина.</summary>
/// <param name="Id">Идентификатор команды.</param>
/// <param name="Title">Название; <c>%ключ%</c> берётся из словарей плагина.</param>
public sealed record PluginCommand(string Id, string Title);

/// <summary>Пункт меню.</summary>
/// <param name="Path">Путь вида <c>Tools/Figma/Import…</c>.</param>
/// <param name="Command">Идентификатор вызываемой команды.</param>
public sealed record PluginMenuItem(string Path, string Command);

/// <summary>Панель плагина.</summary>
/// <param name="Id">Идентификатор панели.</param>
/// <param name="Title">Заголовок панели.</param>
/// <param name="Zone">Зона размещения: <c>left</c>, <c>right</c>, <c>bottom</c>.</param>
public sealed record PluginToolWindow(string Id, string Title, string Zone);

/// <summary>Тип файла, поддерживаемый плагином.</summary>
/// <param name="Ext">Расширение с точкой, например <c>.fig</c>.</param>
/// <param name="Name">Название типа документа.</param>
public sealed record PluginFileType(string Ext, string Name);

/// <summary>
/// Настройка плагина: то, что о ней знает студия, не загружая его сборки.
/// </summary>
/// <param name="Key">Ключ настройки.</param>
/// <param name="Type">Тип значения: <c>string</c>, <c>bool</c>, <c>number</c>.</param>
/// <param name="Scope">
/// Область: <c>user</c> — личное и машинное, остаётся здесь; <c>project</c> —
/// решение команды, едет вместе с проектом в его репозитории.
/// </param>
/// <param name="Title">Подпись в настройках студии; без неё показывается ключ.</param>
/// <param name="Default">Значение, пока человек ничего не выбрал.</param>
public sealed record PluginSetting(
    string Key,
    string Type,
    [property: JsonPropertyName("scope")] string Scope,
    string? Title = null,
    object? Default = null)
{
    /// <summary>Настройка едет вместе с проектом, а не остаётся на машине.</summary>
    public bool IsProject => string.Equals(Scope, "project", StringComparison.OrdinalIgnoreCase);

    /// <summary>Значение — переключатель.</summary>
    public bool IsBool => string.Equals(Type, "bool", StringComparison.OrdinalIgnoreCase);

    /// <summary>Значение — число.</summary>
    public bool IsNumber => string.Equals(Type, "number", StringComparison.OrdinalIgnoreCase);

    /// <summary>Как назвать настройку человеку: подпись, а без неё — ключ.</summary>
    public string Label => Title is { Length: > 0 } title ? title : Key;
}
