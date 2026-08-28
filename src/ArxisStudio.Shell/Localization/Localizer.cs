using System.Collections.Frozen;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace ArxisStudio.Shell.Localization;

/// <summary>
/// Строки интерфейса. Словари ищутся в трёх местах, и позднее сильнее:
/// встроенные ресурсы сборки, файлы <c>lang/&lt;код&gt;.json</c> рядом со
/// студией и такие же файлы в данных пользователя. Смена языка обновляет уже
/// показанный интерфейс.
/// </summary>
/// <remarks>
/// Встроенные словари не убрать: студия обязана говорить даже тогда, когда
/// рядом с ней не осталось ни одного файла. Файлы поверх них нужны, чтобы
/// добавить язык стало «положить файл», а не «пересобрать студию»: язык — это
/// данные, и требовать ради них сборки не за что.
/// <para>
/// Наложение поключевое, а не «файл есть — берём всё из него». Перевод,
/// закрывающий сто ключей из ста двадцати, оставляет остальные двадцать на
/// запасном языке, а не превращает их в <c>!ключ!</c>: студия растёт, ключей
/// прибавляется, и иначе любой перевод протухал бы на первом же нашем релизе.
/// </para>
/// </remarks>
public sealed class Localizer : INotifyPropertyChanged, IStringSource
{
    /// <summary>
    /// Язык, на котором написана студия: на него падает всё непереведённое.
    /// </summary>
    /// <remarks>
    /// Правило то же, что у словарей плагина, где запасной — язык автора:
    /// у непереведённой строки должен быть один понятный источник, а не
    /// очередь из языков, в которой её ищут.
    /// </remarks>
    public const string FallbackLanguage = "en";

    /// <summary>Папка со словарями — и рядом со студией, и в данных пользователя.</summary>
    public const string Folder = "lang";

    /// <summary>Ключ, которым словарь называет свой язык.</summary>
    public const string NameKey = "language.name";

    private readonly List<WeakReference<LocalizedString>> _tracked = [];

    private IReadOnlyList<string> _folders;
    private FrozenDictionary<string, string> _fallback = FrozenDictionary<string, string>.Empty;
    private FrozenDictionary<string, string> _strings = FrozenDictionary<string, string>.Empty;

    /// <summary>Общий экземпляр, к которому привязан интерфейс студии.</summary>
    public static Localizer Instance { get; } = new();

    /// <summary>
    /// Где студия ищет словари: сперва рядом с собой, потом в данных
    /// пользователя.
    /// </summary>
    /// <remarks>
    /// Папка рядом со студией — то, что мы поставляем: оттуда переводчик
    /// берёт список ключей. Папка пользователя сильнее: правят её, а установку
    /// студии на общей машине может быть и нечем.
    /// </remarks>
    /// <remarks>
    /// Свойство вычисляемое, а не статическое поле: <see cref="Instance"/>
    /// заводится инициализатором того же типа, а инициализаторы выполняются
    /// в порядке объявления — поле, объявленное ниже, к этому моменту ещё
    /// пустое.
    /// </remarks>
    public static IReadOnlyList<string> DefaultFolders =>
    [
        Path.Combine(AppContext.BaseDirectory, Folder),
        StudioPaths.Languages,
    ];

    private Localizer()
    {
        _folders = DefaultFolders;
        Language = FallbackLanguage;

        Reload();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Текущий код языка.</summary>
    public string Language { get; private set; }

    /// <summary>Языки, которые студия сейчас умеет показать.</summary>
    public IReadOnlyList<StudioLanguage> Languages { get; private set; } = [];

    /// <summary>
    /// Строка по ключу. Отсутствующий ключ возвращается как <c>!ключ!</c> —
    /// пропуск виден в интерфейсе и не притворяется текстом.
    /// </summary>
    public string this[string key] =>
        _strings.TryGetValue(key, out var value) ? value
        : _fallback.TryGetValue(key, out var back) ? back
        : $"!{key}!";

    /// <summary>
    /// Переключает язык интерфейса.
    /// </summary>
    /// <param name="language">Код культуры, например <c>ru</c> или <c>en</c>.</param>
    /// <returns><c>false</c>, если такого языка у студии нет.</returns>
    /// <remarks>
    /// Язык, для которого не нашлось ни одного словаря, не выбирается. Выбрав
    /// его, студия показала бы весь интерфейс на запасном языке, а в настройках
    /// — выбранным тот, которого нет; отказ честнее. Случай не выдуманный: язык
    /// записан в настройках, а словарь к нему могли удалить.
    /// </remarks>
    public bool SetLanguage(string language)
    {
        if (string.Equals(language, Language, StringComparison.OrdinalIgnoreCase))
            return true;

        var strings = Load(language);

        if (strings.Count == 0)
            return false;

        _strings = strings;
        Language = language;

        RefreshTracked();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));

        return true;
    }

    /// <summary>
    /// Перечитывает словари и заново собирает список языков.
    /// </summary>
    /// <remarks>
    /// Нужно потому, что словари теперь лежат файлами: положенный или
    /// исправленный файл должен становиться виден без перезапуска студии —
    /// иначе перевод правят с перезапуском на каждую строку.
    /// </remarks>
    public void Reload()
    {
        _fallback = Load(FallbackLanguage);
        _strings = string.Equals(Language, FallbackLanguage, StringComparison.OrdinalIgnoreCase)
            ? _fallback
            : Load(Language);

        Languages = Scan();

        RefreshTracked();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Languages)));
    }

    /// <summary>
    /// Меняет папки, в которых студия ищет словари.
    /// </summary>
    /// <param name="folders">Папки, поздняя сильнее; пусто — вернуть обычные.</param>
    /// <remarks>
    /// Встроенные словари остаются в любом случае: это основание, на которое
    /// кладут файлы, а не одна из равноправных папок.
    /// </remarks>
    public void UseFolders(params string[] folders)
    {
        _folders = folders is { Length: > 0 } given ? [.. given] : DefaultFolders;

        Reload();
    }

    /// <summary>
    /// Заводит строку, которая обновляется при смене языка. Ссылка на неё слабая:
    /// строка живёт, пока на неё смотрит привязка, и уходит вместе с окном.
    /// </summary>
    /// <remarks>
    /// Разметке хватает <c>{loc:Loc}</c>, но интерфейс, собранный кодом, — списки,
    /// разделы, пункты меню — заводит свои строки сам.
    /// </remarks>
    /// <param name="key">Ключ строки.</param>
    public LocalizedString Track(string key) => Track(this, key);

    /// <summary>
    /// Заводит строку чужого источника — например, словарей плагина.
    /// </summary>
    /// <remarks>
    /// Список следящих строк один на студию, хотя словари у всех свои: язык
    /// меняется разом, и обновиться должно всё показанное, а не только то, что
    /// написала студия.
    /// </remarks>
    /// <param name="source">Откуда брать текст.</param>
    /// <param name="key">Ключ строки.</param>
    public LocalizedString Track(IStringSource source, string key)
    {
        var tracked = new LocalizedString(source, key);

        lock (_tracked)
            _tracked.Add(new WeakReference<LocalizedString>(tracked));

        return tracked;
    }

    private void RefreshTracked()
    {
        LocalizedString[] alive;

        lock (_tracked)
        {
            alive = _tracked
                .Select(reference => reference.TryGetTarget(out var value) ? value : null)
                .OfType<LocalizedString>()
                .ToArray();

            _tracked.RemoveAll(reference => !reference.TryGetTarget(out _));
        }

        foreach (var value in alive)
            value.Refresh();
    }

    /// <summary>Собирает словарь языка: встроенный, а поверх него — файлы.</summary>
    private FrozenDictionary<string, string> Load(string language)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in Embedded(language))
            merged[pair.Key] = pair.Value;

        foreach (var folder in _folders)
        {
            foreach (var pair in FromFile(Path.Combine(folder, $"{language}.json")))
                merged[pair.Key] = pair.Value;
        }

        return merged.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// Собирает список языков из встроенных словарей и найденных файлов.
    /// </summary>
    private IReadOnlyList<StudioLanguage> Scan()
    {
        var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in Assembly.GetExecutingAssembly().GetManifestResourceNames())
        {
            if (ResourceCode(resource) is { } code)
                codes.Add(code);
        }

        foreach (var folder in _folders)
        {
            foreach (var file in Files(folder))
                codes.Add(Path.GetFileNameWithoutExtension(file));
        }

        return codes.Select(code => new StudioLanguage(code, Name(code))).ToList();
    }

    /// <summary>Как язык называет себя сам; без такого ключа — просто код.</summary>
    private string Name(string code)
    {
        var strings = string.Equals(code, Language, StringComparison.OrdinalIgnoreCase) ? _strings : Load(code);

        return strings.TryGetValue(NameKey, out var name) && name.Length > 0 ? name : code;
    }

    private static IReadOnlyList<string> Files(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.json").ToList() : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? ResourceCode(string resource)
    {
        var prefix = $"{typeof(Localizer).Namespace}.Strings.";

        return resource.StartsWith(prefix, StringComparison.Ordinal) &&
               resource.EndsWith(".json", StringComparison.Ordinal)
            ? resource[prefix.Length..^".json".Length]
            : null;
    }

    private static Dictionary<string, string> Embedded(string language)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = $"{typeof(Localizer).Namespace}.Strings.{language}.json";

        using var stream = assembly.GetManifestResourceStream(name);

        if (stream is null)
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Читает словарь из файла; испорченный файл не отменяет встроенный.
    /// </summary>
    /// <remarks>
    /// Словарь правит человек, и запятая не на месте — обычное дело. Студия,
    /// не запустившаяся из-за неё, была бы наказанием, несоразмерным поводу:
    /// непрочитанный файл просто не накладывается.
    /// </remarks>
    private static Dictionary<string, string> FromFile(string path)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
