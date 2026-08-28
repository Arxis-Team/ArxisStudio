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
/// У встроенного модуля своей папки нет, и словарём ему служат словари самой
/// студии: его строки студия и написала. Внешнему плагину эта дорога закрыта —
/// ключи студии внутренние, их переименование не должно менять текст в чужой
/// панели.
/// </para>
/// </remarks>
public sealed class PluginStrings : IStudioStrings, IStringSource
{
    /// <summary>Папка словарей внутри плагина.</summary>
    public const string Folder = "lang";

    /// <summary>Словарь языка, на котором плагин написан.</summary>
    public const string DefaultFile = "strings.json";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, PluginStrings> Known =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _lock = new();
    private readonly string? _directory;

    private FrozenDictionary<string, string> _translated = FrozenDictionary<string, string>.Empty;
    private FrozenDictionary<string, string> _written = FrozenDictionary<string, string>.Empty;
    private string? _loaded;

    /// <summary>Заводит словари плагина.</summary>
    /// <param name="directory">Папка плагина; пусто — словарей нет, текст берётся у студии.</param>
    public PluginStrings(string? directory) =>
        _directory = directory is { Length: > 0 } path ? path : null;

    /// <summary>Словари самой студии — то, чем пользуются встроенные модули.</summary>
    public static PluginStrings Studio { get; } = new(null);

    /// <summary>Своих словарей нет: текст приходит из словарей студии.</summary>
    public bool IsStudio => _directory is null;

    /// <summary>
    /// Словари плагина из этой папки.
    /// </summary>
    /// <param name="directory">Папка плагина; пусто — словари студии.</param>
    /// <remarks>
    /// Один набор на папку, а не на каждого спрашивающего: словари читает и
    /// список плагинов, и меню, и сами панели, а файл при этом один.
    /// </remarks>
    public static PluginStrings For(string? directory) =>
        directory is { Length: > 0 } path ? Known.GetOrAdd(path, static value => new PluginStrings(value)) : Studio;

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
            strings.Drop();
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
                return _translated.TryGetValue(key, out var translated) ? translated
                    : _written.TryGetValue(key, out var written) ? written
                    : $"!{key}!";
            }
        }
    }

    /// <inheritdoc/>
    public BindingBase Text(string key) =>
        new Binding(nameof(LocalizedString.Value))
        {
            Source = Localizer.Instance.Track(this, key),
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
    /// Перечитывает словари, если язык студии сменился с прошлого раза.
    /// </summary>
    /// <remarks>
    /// Ленивая проверка вместо подписки на смену языка: словарей столько,
    /// сколько установлено плагинов, и подписка каждого держала бы в студии
    /// список, за которым надо следить при удалении плагина.
    /// </remarks>
    private void Drop()
    {
        lock (_lock)
            _loaded = null;
    }

    private void Reload()
    {
        var language = Localizer.Instance.Language;

        lock (_lock)
        {
            if (_loaded == language)
                return;

            _written = Read(DefaultFile);
            _translated = Read($"strings.{language}.json");
            _loaded = language;
        }
    }

    private FrozenDictionary<string, string> Read(string file) =>
        StringFile.Read(Path.Combine(_directory!, Folder, file))
            .ToFrozenDictionary(StringComparer.Ordinal);
}
