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

    /// <summary>Плагины, которые должны стоять раньше этого.</summary>
    public IList<PluginDependency> Dependencies { get; set; } = [];

    /// <summary>Что плагин отдаёт соседям сверх вкладов.</summary>
    public PluginProvides? Provides { get; set; }

    /// <summary>События, при которых сборка плагина загружается.</summary>
    public IList<string> Activation { get; set; } = [];
}

/// <summary>
/// Зависимость от соседнего плагина.
/// </summary>
/// <remarks>
/// Студия разрешает зависимости по манифестам, до загрузки единой сборки:
/// плагин, чья зависимость не выполнена, не поднимается вовсе и говорит
/// почему — вместо того чтобы упасть на первом обращении к соседу.
/// <para>
/// У версии только нижняя граница, и это осознанно: диапазоны — целая
/// подсистема разрешения версий, её заводят для экосистемы, а не до неё.
/// Правило то же, что у <c>sdk.min</c>: «подойдёт любой сосед не старее».
/// </para>
/// </remarks>
public sealed class PluginDependency
{
    /// <summary>Идентификатор нужного плагина, например <c>arxis.figma</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Минимальная версия соседа; null — важна только установленность.</summary>
    public string? Min { get; set; }

    /// <summary>
    /// Сосед желателен, но не обязателен.
    /// </summary>
    /// <remarks>
    /// Значит ровно одно: если сосед установлен и включён — он поднимется
    /// раньше этого плагина; нет — плагин поднимется без него. Ни на что
    /// больше признак не влияет: есть ли сосед на самом деле, плагин
    /// спрашивает у службы <c>IStudioPlugins</c>.
    /// </remarks>
    public bool Optional { get; set; }
}

/// <summary>
/// Что плагин отдаёт соседям.
/// </summary>
/// <remarks>
/// Контракты — сборки с типами, через которые соседи говорят с плагином:
/// интерфейсы, записи, события. Студия загружает их один раз в общий контекст
/// — иначе тот же интерфейс, загруженный в контекст соседа второй раз, был бы
/// другим типом, и приведение падало бы с бессмысленным «IFoo не приводится к
/// IFoo».
/// <para>
/// Контракт публикует только тот, кто хочет, чтобы его расширяли или брали
/// его объекты типизированно: плагину, живущему сам по себе, объявлять
/// нечего. Цена публикации — контракт не выгружается: его обновление
/// требует перезапуска студии, и об этом она говорит словами. Поэтому
/// контракт держат тощим — одни интерфейсы, отдельной сборкой, без
/// реализации: чем меньше в нём меняется, тем реже перезапуск.
/// </para>
/// </remarks>
public sealed class PluginProvides
{
    /// <summary>Пути к контрактным сборкам относительно папки плагина.</summary>
    public IList<string> Contracts { get; set; } = [];
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

/// <summary>
/// Панель плагина.
/// </summary>
/// <remarks>
/// Класс, а не позиционная запись: у записи отсутствующее в JSON поле молча
/// становится <c>null</c> в свойстве, которое обещало строку, — и падает потом,
/// далеко от манифеста, где про опечатку уже не догадаешься.
/// </remarks>
public sealed class PluginToolWindow
{
    /// <summary>Идентификатор панели — уникальный внутри своего плагина.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Заголовок панели; ключ вида <c>%panel.main%</c> переводится.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Прежнее описание места одним словом.
    /// </summary>
    /// <remarks>
    /// Оставлено ради манифестов, написанных до <see cref="Placement"/>: студия
    /// умеет его читать, но новые плагины пишут <c>placement</c>. Одно поле
    /// вместо другого — не потеря: <c>zone: "left"</c> и есть
    /// <c>placement: { side: "left" }</c>.
    /// </remarks>
    public string? Zone { get; set; }

    /// <summary>Где панель просит её поставить; отсутствует — как решит студия.</summary>
    public PluginPlacement? Placement { get; set; }

    /// <summary>
    /// Место, о котором панель просит на самом деле.
    /// </summary>
    /// <remarks>
    /// Единственное место, знающее про старое поле: остальная студия видит
    /// только <see cref="PluginPlacement"/> и про <c>zone</c> не спрашивает.
    /// </remarks>
    [JsonIgnore]
    public PluginPlacement Wanted =>
        Placement
        ?? (Zone is { Length: > 0 } zone ? new PluginPlacement { Side = zone } : new PluginPlacement());
}

/// <summary>
/// Где панель просит её поставить.
/// </summary>
/// <remarks>
/// Пожелание, а не приказ: место читают, когда панель встречают впервые. Дальше
/// его помнит раскладка, и человек волен увести панель куда угодно — иначе
/// каждый запуск затаскивал бы её обратно.
/// <para>
/// Порядка вкладок здесь нет намеренно. Внутри одного плагина он и так равен
/// порядку в манифесте, а между плагинами его решает очередь загрузки — число
/// в манифесте обещало бы власть, которой у него нет.
/// </para>
/// </remarks>
public sealed class PluginPlacement
{
    /// <summary>Сторона от области документов: <c>left</c>, <c>right</c>, <c>top</c>, <c>bottom</c>.</summary>
    public string Side { get; set; } = "right";

    /// <summary>
    /// Какую долю окна занять — от 0 до 1; 0 значит «как обычно».
    /// </summary>
    /// <remarks>
    /// Доля, а не пиксели: между запусками меняются монитор и масштаб. Слушают
    /// её только у первой панели на пустой стороне — у занятой размер уже есть,
    /// и отбирать его у соседа новичок не вправе.
    /// </remarks>
    public double Size { get; set; }

    /// <summary>
    /// Встать вкладкой рядом с этой панелью; пусто — не важно.
    /// </summary>
    /// <remarks>
    /// Самое точное из пожеланий, поэтому и самое сильное: если названная
    /// панель на экране, сторона и доля уже не спрашиваются. Имя — полное, с
    /// плагином впереди: <c>arxis.hello:hello.tree</c>.
    /// </remarks>
    public string? Near { get; set; }
}

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
