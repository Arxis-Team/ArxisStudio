using System.Collections.Frozen;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace ArxisStudio.Shell.Localization;

/// <summary>
/// Строки интерфейса. Словари накладываются слоями, и каждый следующий
/// сильнее предыдущего: встроенные ресурсы сборки, файлы
/// <c>lang/&lt;код&gt;.json</c> рядом со студией, установленные языковые
/// пакеты, такие же файлы в данных пользователя. Смена языка обновляет уже
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
/// <para>
/// Порядок слоёв — «чем ближе к человеку, тем сильнее». Установленный пакет
/// сильнее того, что возим мы, а положенный руками файл сильнее пакета:
/// правка руками — способ починить что угодно, включая чужой перевод, и
/// отнимать его у человека незачем.
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

    // Слабо — и это здесь уместно: у каждой записи есть свой хозяин
    // (источник, который её завёл), а список лишь обходит их при смене языка.
    // Слабая ссылка вместо хозяина — то, из-за чего строки прежде умирали.
    private readonly List<WeakReference<TrackedStrings>> _sources = [];

    private string _shared;
    private string _user;
    private ILanguageSource? _packs;
    private FrozenDictionary<string, string> _fallback = FrozenDictionary<string, string>.Empty;
    private FrozenDictionary<string, string> _strings = FrozenDictionary<string, string>.Empty;

    /// <summary>Общий экземпляр, к которому привязан интерфейс студии.</summary>
    public static Localizer Instance { get; } = new();

    /// <summary>Строки самой студии: их хозяин — она сама.</summary>
    private TrackedStrings Own => _own ??= Register(new TrackedStrings(this));

    private TrackedStrings? _own;

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
    /// Свойства вычисляемые, а не статические поля: <see cref="Instance"/>
    /// заводится инициализатором того же типа, а инициализаторы выполняются
    /// в порядке объявления — поле, объявленное ниже, к этому моменту ещё
    /// пустое.
    /// </remarks>
    public static string SharedFolder => Path.Combine(AppContext.BaseDirectory, Folder);

    /// <summary>Папка словарей пользователя — самый сильный слой.</summary>
    public static string UserFolder => StudioPaths.Languages;

    private Localizer()
    {
        _shared = SharedFolder;
        _user = UserFolder;
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
    /// Все ключи студии — по ним считают полноту перевода.
    /// </summary>
    /// <remarks>
    /// Берутся из запасного языка: это язык, на котором студия написана, и
    /// потому его словарь полон по определению. Считать по текущему языку
    /// значило бы мерить неполный перевод неполным же списком.
    /// </remarks>
    public IReadOnlyCollection<string> Keys => _fallback.Keys;

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

        // Язык мог исчезнуть, пока студия работала: пакет, который его
        // принёс, выключили или удалили. Оставить его выбранным значило бы
        // показывать весь интерфейс на запасном языке, называя его чужим.
        if (_strings.Count == 0)
        {
            _strings = _fallback;
            Language = FallbackLanguage;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        }

        Languages = Scan();

        RefreshTracked();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Languages)));
    }

    /// <summary>
    /// Меняет папки, в которых студия ищет словари.
    /// </summary>
    /// <param name="shared">Папка рядом со студией; null — обычная.</param>
    /// <param name="user">Папка пользователя; null — обычная.</param>
    /// <remarks>
    /// Встроенные словари остаются в любом случае: это основание, на которое
    /// кладут файлы, а не одна из равноправных папок.
    /// </remarks>
    public void UseFolders(string? shared = null, string? user = null)
    {
        _shared = shared ?? SharedFolder;
        _user = user ?? UserFolder;

        Reload();
    }

    /// <summary>
    /// Ставит источник языковых пакетов.
    /// </summary>
    /// <param name="packs">Источник; null — пакетов нет.</param>
    /// <remarks>
    /// Кто собрал этот источник, Shell не знает: языки приносят плагины, а
    /// плагины — забота Extensibility. Сюда приходит уже готовое «коды и
    /// строки по коду», и потому список языков в настройках не отличает
    /// установленный пакет от того, что возим мы сами.
    /// </remarks>
    public void UsePacks(ILanguageSource? packs)
    {
        _packs = packs;

        Reload();
    }

    /// <summary>
    /// Строка студии, обновляющаяся при смене языка.
    /// </summary>
    /// <remarks>
    /// Разметке хватает <c>{loc:Loc}</c>, но интерфейс, собранный кодом, — списки,
    /// разделы, пункты меню — заводит свои строки сам.
    /// <para>
    /// Строка принадлежит студии и живёт с ней; на один ключ она одна. Держать
    /// её вызывающему не нужно, но и рассчитывать, что её удержит привязка,
    /// нельзя: привязка Avalonia смотрит на свой источник слабо.
    /// </para>
    /// </remarks>
    /// <param name="key">Ключ строки.</param>
    public LocalizedString Track(string key) => Own[key];

    /// <summary>
    /// Берёт под присмотр строки чужого источника — например, словарей плагина.
    /// </summary>
    /// <param name="strings">Строки источника; хозяин у них свой.</param>
    /// <returns>Их же, чтобы вызов читался как присваивание поля.</returns>
    /// <remarks>
    /// Язык меняется разом, и обновиться должно всё показанное, а не только
    /// написанное студией. Держать сами строки студия не может: словари
    /// плагина уходят вместе с ним, и вечная ссылка отсюда была бы утечкой.
    /// Поэтому здесь только присмотр, а владение остаётся у источника.
    /// </remarks>
    public TrackedStrings Register(TrackedStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        lock (_sources)
            _sources.Add(new WeakReference<TrackedStrings>(strings));

        return strings;
    }

    private void RefreshTracked()
    {
        TrackedStrings[] alive;

        lock (_sources)
        {
            alive = _sources
                .Select(reference => reference.TryGetTarget(out var value) ? value : null)
                .OfType<TrackedStrings>()
                .ToArray();

            _sources.RemoveAll(reference => !reference.TryGetTarget(out _));
        }

        foreach (var strings in alive)
            strings.Refresh();
    }

    /// <summary>Собирает словарь языка: встроенный, а поверх него — файлы.</summary>
    private FrozenDictionary<string, string> Load(string language)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in Embedded(language))
            merged[pair.Key] = pair.Value;

        foreach (var pair in StringFile.Read(Path.Combine(_shared, $"{language}.json")))
            merged[pair.Key] = pair.Value;

        if (_packs is not null)
        {
            foreach (var pair in _packs.Read(language))
                merged[pair.Key] = pair.Value;
        }

        foreach (var pair in StringFile.Read(Path.Combine(_user, $"{language}.json")))
            merged[pair.Key] = pair.Value;

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

        foreach (var folder in new[] { _shared, _user })
        {
            foreach (var file in Files(folder))
                codes.Add(Path.GetFileNameWithoutExtension(file));
        }

        if (_packs is not null)
            codes.UnionWith(_packs.Codes);

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
}
