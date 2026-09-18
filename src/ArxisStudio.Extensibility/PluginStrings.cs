using System.Collections.Frozen;
using ArxisStudio.Sdk;
using ArxisStudio.Shell.Localization;
using Avalonia.Data;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Словари одного плагина.
/// </summary>
/// <remarks>
/// Читаются из папки плагина, а не из его сборки: заголовок панели, пункт меню
/// и подпись настройки студия показывает раньше, чем плагин впервые поднимут, —
/// иначе список установленного означал бы загрузку всего установленного.
/// <para>
/// Файл называется кодом языка — <c>lang/en.json</c>, <c>lang/ru.json</c>, <c>lang/de.json</c>, —
/// той же формой, какой названы словари самой студии. Запасной язык тоже её: не нашлось файла
/// текущего языка — читается <c>en.json</c>. Прежде язык автора был отдельным файлом без кода
/// (<c>strings.json</c>), и одна и та же строка называлась в студии и в плагине по-разному.
/// </para>
/// <para>
/// Дорога одна и у плагина, и у встроенного модуля: словарь лежит в папке расширения, и чужих
/// ключей — ни студии, ни соседа — в нём не видно. Ключ вроде <c>panel.main</c> придумают двое, и
/// общий словарь отдал бы его тому, кого раньше загрузили; а разреши мы брать ключи студии,
/// переименование строки внутри студии молча меняло бы текст в чужой панели. Модуль от этого
/// правила не освобождён: тем он и переносится во внешний плагин перекладыванием папки.
/// </para>
/// <para>
/// Словари самой студии отдаёт <see cref="Studio"/> — он и остаётся дорогой к
/// <see cref="Localizer"/> для того, у кого расширения нет вовсе: собственных элементов полосы и
/// меню студии.
/// </para>
/// </remarks>
public sealed class PluginStrings : IStudioStrings, IStringSource
{
    /// <summary>Папка словарей внутри плагина.</summary>
    public const string Folder = "lang";

    /// <summary>
    /// Словарь запасного языка: его читают, когда файла текущего языка у расширения нет.
    /// </summary>
    /// <remarks>
    /// Считается от <see cref="Localizer.FallbackLanguage"/>, а не написан буквой второй раз:
    /// запасной язык у студии и у расширения один, и разойтись им нельзя — иначе студия показывала
    /// бы английский, а панель расширения молчала бы ключами.
    /// </remarks>
    public static string DefaultFile { get; } = FileOf(Localizer.FallbackLanguage);

    /// <summary>Как называется словарь этого языка.</summary>
    /// <param name="language">Код языка.</param>
    public static string FileOf(string language) => $"{language}.json";

    // Так словарь по умолчанию назывался до SDK 7.0. Студия его не читает — только называет, когда
    // своего DefaultFile у расширения нет: собранное раньше она поднимает (Satisfies прежний
    // старший номер пропускает), и без подсказки оно показывало бы ключи молча.
    private const string FormerDefaultFile = "strings.json";

    /// <summary>
    /// У расширения спросили строку, а словаря по умолчанию у него нет.
    /// </summary>
    /// <remarks>
    /// Пустой словарь вместо отказа — правило <see cref="StringFile"/>, и оно остаётся. Но пропуск
    /// перевода и отсутствующий <see cref="DefaultFile"/> — разные беды: без перевода отвечает
    /// английский, а без английского ключами становятся все строки расширения разом. Сказать об
    /// этом, кроме журнала, некому: меню и полоса строятся по манифесту, сборка не загружается, и
    /// отказа, который назвал бы причину, нет.
    /// <para>
    /// Звучит раз на попытку: смена языка перечитывает словари, но сказанного не повторяет, а
    /// перезагрузка расширения — новая попытка автора, и словарь, не нашедшийся и после неё,
    /// звучит снова.
    /// </para>
    /// </remarks>
    public static event EventHandler<MissingDictionary>? Missing;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, PluginStrings> Known =
        new(StringComparer.OrdinalIgnoreCase);

    private static IPluginTranslations? _translations;

    private readonly Lock _lock = new();

    // Словари плагина — источник своих строк и их хозяин. Студия берёт их под
    // присмотр, чтобы обновлять при смене языка, но не владеет ими: плагин
    // уходит, и они уходят с ним.
    private readonly TrackedStrings _tracked;
    private readonly string? _directory;
    private readonly string _pluginId;

    private FrozenDictionary<string, string> _translated = FrozenDictionary<string, string>.Empty;
    private FrozenDictionary<string, string> _packed = FrozenDictionary<string, string>.Empty;
    private FrozenDictionary<string, string> _written = FrozenDictionary<string, string>.Empty;
    private string? _loaded;
    private bool _told;

    /// <summary>Заводит словари плагина.</summary>
    /// <param name="directory">Папка плагина; пусто — словарей нет, текст берётся у студии.</param>
    /// <param name="pluginId">Чей это плагин — по нему ищут перевод из пакета.</param>
    public PluginStrings(string? directory, string? pluginId = null)
    {
        _directory = directory is { Length: > 0 } path ? path : null;
        _pluginId = pluginId ?? string.Empty;
        _tracked = Localizer.Instance.Register(new TrackedStrings(this));
    }

    /// <summary>Словари самой студии — то, чем говорит она сама.</summary>
    public static PluginStrings Studio { get; } = new(null);

    /// <summary>
    /// Словари плагина из этой папки.
    /// </summary>
    /// <param name="directory">Папка плагина; пусто — словари студии.</param>
    /// <param name="pluginId">Чей это плагин — по нему ищут перевод из пакета.</param>
    /// <remarks>
    /// Один набор на папку, а не на каждого спрашивающего: словари читает и
    /// список плагинов, и меню, и сами панели, а файл при этом один.
    /// </remarks>
    public static PluginStrings For(string? directory, string? pluginId = null) =>
        directory is { Length: > 0 } path
            ? Known.GetOrAdd(path, static (value, id) => new PluginStrings(value, id), pluginId)
            : Studio;

    /// <summary>
    /// Ставит источник переводов, пришедших из языковых пакетов.
    /// </summary>
    /// <param name="translations">Источник; null — переводов нет.</param>
    /// <remarks>
    /// Источник один на студию и меняется вместе с набором установленных
    /// пакетов, поэтому прочитанное всеми словарями забывается: перевод,
    /// пришедший минуту назад, должен быть виден сразу, а ушедший —
    /// перестать быть виден.
    /// </remarks>
    public static void UseTranslations(IPluginTranslations? translations)
    {
        _translations = translations;

        foreach (var strings in Known.Values)
            strings.Drop();
    }

    /// <summary>
    /// Забывает прочитанное, чтобы словари перечитались заново.
    /// </summary>
    /// <param name="directory">Папка плагина.</param>
    /// <remarks>
    /// Нужно при перезагрузке плагина: автор правит словарь так же, как код, и
    /// перезагрузка, оставившая прежний текст, была бы перезагрузкой наполовину.
    /// </remarks>
    public static void Forget(string? directory)
    {
        if (directory is { Length: > 0 } path && Known.TryGetValue(path, out var strings))
            strings.Drop(retell: true);
    }

    /// <inheritdoc/>
    public string Language => Localizer.Instance.Language;

    /// <inheritdoc/>
    public string this[string key]
    {
        get
        {
            if (_directory is null)
                return Localizer.Instance[key];

            Reload();

            lock (_lock)
            {
                // Порядок старшинства: свой перевод, потом перевод из
                // пакета, потом язык, на котором плагин написан. Про свой
                // продукт автор знает больше постороннего, и подменять его
                // слова словами пакета студия не станет; но там, где автор
                // молчит, пакет отвечает.
                return _translated.TryGetValue(key, out var translated) ? translated
                    : _packed.TryGetValue(key, out var packed) ? packed
                    : _written.TryGetValue(key, out var written) ? written
                    : $"!{key}!";
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Строки принадлежат этим словарям и уходят вместе с плагином. Держать их
    /// обязан именно источник: привязка Avalonia смотрит на свою модель слабо,
    /// и строка, за которую никто не держится, умрёт на первой же сборке
    /// мусора — а показанный заголовок навсегда останется на прежнем языке.
    /// </remarks>
    public BindingBase Text(string key) =>
        new Binding(nameof(LocalizedString.Value))
        {
            Source = _tracked[key],
            Mode = BindingMode.OneWay,
        };

    /// <summary>
    /// Разворачивает <c>%ключ%</c>; обычный текст возвращается как есть.
    /// </summary>
    /// <param name="text">Строка из манифеста.</param>
    /// <remarks>
    /// Ключи необязательны: плагин, написанный на один язык, пишет текст прямо
    /// в манифест и работает. Локализация — то, что автор добавляет, когда она
    /// ему понадобилась, а не условие, без которого плагина не собрать.
    /// </remarks>
    public string Resolve(string? text) =>
        text is not { Length: > 0 } value ? string.Empty
        : IsKey(value, out var key) ? this[key]
        : value;

    /// <summary>
    /// Ключ ли это — строка вида <c>%panel.main%</c>.
    /// </summary>
    /// <param name="text">Строка из манифеста.</param>
    /// <param name="key">Ключ без процентов.</param>
    public static bool IsKey(string? text, out string key)
    {
        if (text is { Length: > 2 } value && value[0] == '%' && value[^1] == '%')
        {
            key = value[1..^1];
            return true;
        }

        key = string.Empty;
        return false;
    }

    /// <summary>
    /// Забывает прочитанное: словари перечитаются при первом же вопросе.
    /// </summary>
    /// <param name="retell">
    /// Сказать ли снова, что словаря по умолчанию нет. Перезагрузка — новая попытка автора, и об
    /// этом говорят снова; смена набора языковых пакетов словаря расширения не касается.
    /// </param>
    private void Drop(bool retell = false)
    {
        lock (_lock)
        {
            _loaded = null;

            if (retell)
                _told = false;
        }
    }

    /// <summary>
    /// Перечитывает словари, если язык студии сменился с прошлого раза.
    /// </summary>
    /// <remarks>
    /// Ленивая проверка вместо подписки на смену языка: словарей столько,
    /// сколько установлено плагинов, и подписка каждого держала бы в студии
    /// список, за которым надо следить при удалении плагина.
    /// </remarks>
    private void Reload()
    {
        var language = Localizer.Instance.Language;
        MissingDictionary? missing;

        lock (_lock)
        {
            if (_loaded == language)
                return;

            _written = Read(DefaultFile);

            // На запасном языке свой файл и есть словарь по умолчанию: читать его второй раз
            // незачем, а старшинство он сохраняет — слово автора о своём продукте старше слова
            // языкового пакета, на каком бы языке оно ни было сказано.
            _translated = string.Equals(language, Localizer.FallbackLanguage, StringComparison.OrdinalIgnoreCase)
                ? _written
                : Read(FileOf(language));
            _packed = _pluginId is { Length: > 0 } id && _translations is not null
                ? _translations.Read(id, language).ToFrozenDictionary(StringComparer.Ordinal)
                : FrozenDictionary<string, string>.Empty;

            _loaded = language;
            missing = Unwritten();
        }

        // Снаружи замка: слушатель — чужой код, и словарь, запертый на время записи в журнал,
        // заставил бы ждать каждого, кто спрашивает строку.
        if (missing is not null)
            Missing?.Invoke(this, missing);
    }

    /// <summary>
    /// Словаря по умолчанию нет, и об этом ещё не сказано.
    /// </summary>
    /// <returns>Что сказать; <c>null</c> — сказать нечего или некому.</returns>
    private MissingDictionary? Unwritten()
    {
        // Некому слушать — и помечать сказанным нечего, как у непрочитанного файла: первый же
        // слушатель обязан услышать о словаре, которого нет сейчас.
        if (_told || Missing is null)
            return null;

        var expected = PathOf(DefaultFile);

        if (File.Exists(expected))
            return null;

        _told = true;

        var former = PathOf(FormerDefaultFile);

        return new MissingDictionary(
            _pluginId is { Length: > 0 } id ? id : Path.GetFileName(_directory!),
            expected,
            File.Exists(former) ? former : null);
    }

    private string PathOf(string file) => Path.Combine(_directory!, Folder, file);

    private FrozenDictionary<string, string> Read(string file) =>
        StringFile.Read(PathOf(file)).ToFrozenDictionary(StringComparer.Ordinal);
}

/// <summary>Расширение, у которого спросили строку, а словаря по умолчанию у него нет.</summary>
/// <param name="Owner">Идентификатор расширения.</param>
/// <param name="Path">Где словарь ждали.</param>
/// <param name="Former">
/// Словарь под именем, которым он назывался до SDK 7.0, если такой лежит рядом: так выглядит
/// расширение, собранное раньше; иначе <c>null</c>.
/// </param>
public sealed record MissingDictionary(string Owner, string Path, string? Former);
