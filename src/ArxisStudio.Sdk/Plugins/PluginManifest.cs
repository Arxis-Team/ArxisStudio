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

    /// <summary>Настройки плагина, показываемые на экране Settings.</summary>
    public IList<PluginSetting> Settings { get; set; } = [];

    /// <summary>Языки интерфейса, которые плагин приносит студии.</summary>
    public IList<PluginLanguage> Languages { get; set; } = [];
}

/// <summary>
/// Команда плагина.
/// </summary>
/// <remarks>
/// Кроме идентификатора, у команды в манифесте ничего нет, и это осознанно.
/// Текст пункта несёт путь в <c>menus</c> — там же, где решается, куда пункт
/// встанет; название у самой команды было бы вторым местом для той же строки,
/// и рано или поздно они разошлись бы. Понадобится палитра команд — название
/// вернётся вместе с ней и с тем, кто его показывает.
/// <para>
/// Список нужен студии не для показа: по нему она снимает обработчики, когда
/// плагин выгружают.
/// </para>
/// </remarks>
/// <param name="Id">Идентификатор команды.</param>
public sealed record PluginCommand(string Id);

/// <summary>Пункт меню.</summary>
/// <param name="Path">Путь вида <c>Tools/Figma/Import…</c>.</param>
/// <param name="Command">Идентификатор вызываемой команды.</param>
public sealed record PluginMenuItem(string Path, string Command);

/// <summary>Панель плагина.</summary>
/// <param name="Id">Идентификатор панели.</param>
/// <param name="Title">Заголовок панели.</param>
/// <param name="Zone">Зона размещения: <c>left</c>, <c>right</c>, <c>bottom</c>.</param>
public sealed record PluginToolWindow(string Id, string Title, string Zone);

/// <summary>
/// Язык интерфейса, который приносит плагин.
/// </summary>
/// <remarks>
/// Языковой пакет — плагин без единой сборки: ни <c>entry</c>, ни событий
/// активации у него нет, и это не оплошность, а суть. Перевод — данные, и
/// выполнять ему нечего; заодно пакет от незнакомого человека ничего не может
/// сделать с машиной.
/// <para>
/// Путь к словарю задан явно, а не выведен из кода: один пакет вполне может
/// везти и <c>zh-hans</c>, и <c>zh-hant</c>, и держать их где ему удобно.
/// </para>
/// </remarks>
/// <param name="Code">Код языка, например <c>de</c>.</param>
/// <param name="Name">Название на нём самом, например <c>Deutsch</c>.</param>
/// <param name="File">Путь к словарю относительно папки плагина.</param>
/// <param name="Translations">Переводы чужих плагинов на этот язык.</param>
public sealed record PluginLanguage(
    string Code,
    string Name,
    string File,
    IList<PluginTranslation>? Translations = null);

/// <summary>
/// Перевод чужого плагина, который везёт языковой пакет.
/// </summary>
/// <remarks>
/// Автор плагина переводит его на те языки, которые знает сам, и на этом
/// его силы кончаются. Дальше нужен кто-то третий: пакет закрывает то, до
/// чего у автора не дошли руки, — иначе немец с русско-английским плагином
/// так и остался бы с чужим языком в своей панели.
/// <para>
/// Свой перевод плагина при этом сильнее: про свой продукт автор знает
/// больше, и подменять его словами постороннего студия не будет.
/// </para>
/// </remarks>
/// <param name="Id">Идентификатор переводимого плагина.</param>
/// <param name="File">Путь к словарю относительно папки пакета.</param>
public sealed record PluginTranslation(string Id, string File);

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
