using System.Text.Json;
using System.Text.Json.Nodes;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Shell;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Настройки всех плагинов: две области, два файла.
/// </summary>
/// <remarks>
/// Пользовательская область лежит рядом с настройками самой студии
/// (<c>%APPDATA%/ArxisStudio/plugin-settings.json</c>) — это личное и машинное:
/// токены, пути, привычки; в репозиторий такое не кладут. Проектная лежит в
/// открытом проекте (<c>.arxis/settings.json</c>) и едет вместе с ним: это
/// решение команды, а не человека за конкретным столом.
/// <para>
/// Файл на область, а не на плагин: один файл читается глазами целиком,
/// находится поиском и сравнивается в истории; россыпь по плагинам не даёт
/// ничего из этого. Внутри значения разложены по идентификаторам плагинов,
/// поэтому чужого в своей ветке плагин не увидит.
/// </para>
/// <para>
/// Проектное значение перекрывает пользовательское. Так устроены и VS Code, и
/// IntelliJ: то, о чём договорилась команда, важнее того, что человек однажды
/// поставил себе.
/// </para>
/// </remarks>
public sealed class PluginSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,

        // Файл настроек правят руками, и русский текст в нём должен читаться
        // словами, а не escape-последовательностями: по умолчанию сериализатор
        // экранирует всё за пределами ASCII.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _userFile;
    private string? _projectFile;

    private JsonObject _user;
    private JsonObject _project;

    // Почему файл области не прочитался; null — прочитался или его нет. Такую область не пишут.
    private string? _userBroken;
    private string? _projectBroken;

    /// <summary>Создаёт хранилище.</summary>
    /// <param name="projectPath">Путь к открытому решению или проекту; null — проекта нет.</param>
    /// <param name="userFile">Файл пользовательской области; по умолчанию — рядом с настройками студии.</param>
    public PluginSettingsStore(string? projectPath = null, string? userFile = null)
    {
        _userFile = userFile ?? Path.Combine(StudioPaths.UserData, "plugin-settings.json");
        _projectFile = ProjectFileFor(projectPath);

        (_user, _userBroken) = Read(_userFile);
        (_project, _projectBroken) = Read(_projectFile);
    }

    /// <summary>Настройку изменили; аргументы — плагин и ключ.</summary>
    public event EventHandler<(string PluginId, string Key)>? Changed;

    /// <summary>Файл пользовательской области.</summary>
    public string UserFile => _userFile;

    /// <summary>Файл проектной области; null, если проект не открыт.</summary>
    public string? ProjectFile => _projectFile;

    /// <summary>
    /// Читает значение настройки.
    /// </summary>
    /// <param name="pluginId">Чья настройка.</param>
    /// <param name="declared">Объявление из манифеста: из него берутся область и значение по умолчанию.</param>
    /// <returns>Записанное значение или объявленное по умолчанию.</returns>
    public JsonNode? Read(string pluginId, PluginSetting declared)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);
        ArgumentNullException.ThrowIfNull(declared);

        return Value(_project, pluginId, declared.Key)
            ?? Value(_user, pluginId, declared.Key)
            ?? Node(declared.Default);
    }

    /// <summary>
    /// Записывает значение в объявленную для настройки область.
    /// </summary>
    /// <param name="pluginId">Чья настройка.</param>
    /// <param name="declared">Объявление из манифеста.</param>
    /// <param name="value">Новое значение; null стирает записанное.</param>
    /// <returns>null, если записано, иначе — почему нет.</returns>
    /// <remarks>
    /// Проектную настройку без открытого проекта записать некуда, и делать вид,
    /// что записали, нельзя: плагин прочтёт обратно не своё значение и решит,
    /// что человек его не менял.
    /// <para>
    /// В файл, который не прочитался, запись не идёт: он читается пустым, и запись поверх стёрла бы
    /// всё, чего студия не прочла, — прочие настройки всех плагинов, а у проектного файла и чужие
    /// (запись 383). Так же поступает VS Code. Человек мог поправить файл, пока студия работала,
    /// поэтому перед отказом файл перечитывается.
    /// </para>
    /// </remarks>
    public string? Write(string pluginId, PluginSetting declared, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);
        ArgumentNullException.ThrowIfNull(declared);

        var project = declared.IsProject;

        if (project && _projectFile is null)
            return $"Настройка {declared.Key} проектная, а проект не открыт";

        var file = project ? _projectFile! : _userFile;

        if (project && _projectBroken is not null)
            (_project, _projectBroken) = Read(file);
        else if (!project && _userBroken is not null)
            (_user, _userBroken) = Read(file);

        if ((project ? _projectBroken : _userBroken) is { } broken)
            return $"Настройки не записались: {file} не прочитался ({broken}), и запись поверх стёрла бы его. Поправьте файл — и запись пройдёт";

        var storage = project ? _project : _user;
        var branch = storage[pluginId] as JsonObject;

        if (branch is null)
        {
            branch = [];
            storage[pluginId] = branch;
        }

        if (value is null)
            branch.Remove(declared.Key);
        else
            branch[declared.Key] = Node(value);

        if (Save(file, storage) is { } error)
            return error;

        Changed?.Invoke(this, (pluginId, declared.Key));
        return null;
    }

    /// <summary>
    /// Переводит проектную область на другой проект.
    /// </summary>
    /// <param name="projectPath">Путь к открытому решению или проекту; null — проекта больше нет.</param>
    /// <returns>Плагины и ключи, чьё записанное значение переменилось; пусто — проект тот же.</returns>
    /// <remarks>
    /// Проектная область едет вместе с проектом, и смена проекта меняет значения, которых плагин не
    /// трогал. Сказать ему об этом — дело студии: сам он узнал бы о перемене, только перечитав
    /// настройки без повода. Возвращаются ключи обеих сторон — и появившиеся в новом проекте, и
    /// пропавшие вместе со старым: у вторых значение тоже другое, пусть и вернувшееся к
    /// пользовательскому или объявленному.
    /// </remarks>
    public IReadOnlyList<(string PluginId, string Key)> Follow(string? projectPath)
    {
        var file = ProjectFileFor(projectPath);

        if (string.Equals(file, _projectFile, StringComparison.OrdinalIgnoreCase))
            return [];

        var was = _project;

        _projectFile = file;
        (_project, _projectBroken) = Read(file);

        return Different(was, _project);
    }

    /// <summary>Перечитывает оба файла: их правят и мимо студии.</summary>
    public void Refresh()
    {
        (_user, _userBroken) = Read(_userFile);
        (_project, _projectBroken) = Read(_projectFile);
    }

    /// <summary>
    /// Превращает значение в узел JSON.
    /// </summary>
    /// <remarks>
    /// Перечисление по типам, а не <c>JsonValue.Create(object)</c>: тот заводит
    /// узел, который без настроенного сериализатора не записать, и падает он не
    /// при создании, а при сохранении файла — там, где причину уже не видно.
    /// Настройка по контракту бывает строкой, числом или переключателем; всё
    /// прочее сохраняется своим текстом, а не теряется.
    /// </remarks>
    private static JsonNode? Node(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        JsonElement element => JsonNode.Parse(element.GetRawText()),
        string text => JsonValue.Create(text),
        bool flag => JsonValue.Create(flag),
        int number => JsonValue.Create(number),
        long number => JsonValue.Create(number),
        double number => JsonValue.Create(number),
        decimal number => JsonValue.Create(number),
        _ => JsonValue.Create(value.ToString()),
    };

    /// <summary>Чем два состояния области отличаются: плагин и ключ на каждое расхождение.</summary>
    /// <param name="was">Что было.</param>
    /// <param name="now">Что стало.</param>
    private static List<(string PluginId, string Key)> Different(JsonObject was, JsonObject now)
    {
        var changed = new List<(string PluginId, string Key)>();

        foreach (var pluginId in was.Select(pair => pair.Key).Concat(now.Select(pair => pair.Key)).Distinct(StringComparer.Ordinal))
        {
            var before = was[pluginId] as JsonObject;
            var after = now[pluginId] as JsonObject;

            var keys = (before?.Select(pair => pair.Key) ?? [])
                .Concat(after?.Select(pair => pair.Key) ?? [])
                .Distinct(StringComparer.Ordinal);

            foreach (var key in keys)
            {
                if (!JsonNode.DeepEquals(Value(was, pluginId, key), Value(now, pluginId, key)))
                    changed.Add((pluginId, key));
            }
        }

        return changed;
    }

    private static JsonNode? Value(JsonObject storage, string pluginId, string key) =>
        storage[pluginId] is JsonObject branch && branch.TryGetPropertyValue(key, out var found) ? found : null;

    /// <summary>
    /// Где лежит проектная область.
    /// </summary>
    /// <remarks>
    /// Папка <c>.arxis</c> рядом с решением: настройки принадлежат проекту, а не
    /// файлу решения, и класть их внутрь него значило бы менять то, что
    /// принадлежит MSBuild.
    /// </remarks>
    private static string? ProjectFileFor(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return null;

        var folder = Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath);

        return folder is null ? null : Path.Combine(folder, ".arxis", "settings.json");
    }

    /// <summary>Читает файл области.</summary>
    /// <param name="file">Файл; null — области нет.</param>
    /// <returns>Значения и почему файл не прочитался; причина null — прочитался или его нет.</returns>
    /// <remarks>
    /// Испорченный файл — не повод не запуститься: настройки вернутся к объявленным по умолчанию, а
    /// починить файл человек сможет руками — запись поверх него не пойдёт (<see cref="Write"/>).
    /// </remarks>
    private static (JsonObject Values, string? Broken) Read(string? file)
    {
        if (file is null || !File.Exists(file))
            return ([], null);

        try
        {
            var text = File.ReadAllText(file);

            // Пустой файл терять нечего, и пишется он как отсутствующий.
            if (string.IsNullOrWhiteSpace(text))
                return ([], null);

            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) is JsonObject values
                ? (values, null)
                : ([], "в нём не объект JSON");
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return ([], e.Message);
        }
    }

    private static string? Save(string file, JsonObject storage)
    {
        try
        {
            AtomicFile.WriteAllText(file, storage.ToJsonString(Options));

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Настройки не записались: {e.Message}";
        }
    }
}
