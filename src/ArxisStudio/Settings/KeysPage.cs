using System.ComponentModel;
using System.Globalization;
using ArxisStudio.Icons;
using ArxisStudio.Palette;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using Avalonia.Media;

namespace ArxisStudio.Settings;

/// <summary>Сочетание на странице «Клавиши»: что нажимают, кого зовут и чьё оно.</summary>
/// <param name="Gesture">Сочетание, как его пишут: <c>Ctrl+W</c>.</param>
/// <param name="Command">Название команды; нет названия — её имя.</param>
/// <param name="Source">Откуда сочетание: <c>keymap.json</c>, студия или имя плагина.</param>
public sealed record KeyRow(string Gesture, string Command, string Source);

/// <summary>Отказ на странице «Клавиши»: кому сочетание не досталось и кому оно отдано.</summary>
/// <param name="Gesture">Что просили.</param>
/// <param name="Command">Кто просил — названием команды.</param>
/// <param name="Winner">Кому досталось — готовой строкой «занято: …».</param>
/// <param name="Source">Чья была просьба.</param>
public sealed record KeyRefusal(string Gesture, string Command, string Winner, string Source);

/// <summary>
/// Страница «Клавиши»: все сочетания студии, чьи они и кому не досталось.
/// </summary>
/// <remarks>
/// Сверх палитры страница показывает то, чего палитра не знает: откуда сочетание — из
/// <c>keymap.json</c>, от студии или из манифеста плагина — и кто остался без своего, с именем
/// победителя. Правок страница не копит: сочетания меняют в файле, и страница ведёт к нему. Файла
/// нет — кнопка заводит его с подсказкой, как он устроен.
/// <para>
/// Строки идут в порядке раздачи: сперва человек, потом студия, потом плагины в порядке подъёма. Этот
/// порядок и решает, кому достаётся занятое, — показать его значит объяснить отказы.
/// </para>
/// <para>
/// Собранная по реестру, страница его слушает и перечитывает, пока её не отпустят: кнопка страницы
/// ведёт к правке файла, и человек, вернувшийся из редактора — или со страницы плагинов того же окна,
/// — должен увидеть, что стало, а не список на миг открытия. Отпускает её окно настроек, закрываясь:
/// реестр живёт весь сеанс, и забытая подписка держала бы страницу вместе с ним.
/// </para>
/// </remarks>
public sealed class KeysPage : ISettingsPage, INotifyPropertyChanged, IDisposable
{
    private readonly Func<(IReadOnlyList<KeyRow> Rows, IReadOnlyList<KeyRefusal> Refusals)> _read;
    private readonly Action<string> _open;
    private Action? _release;
    private string? _query;

    /// <summary>Собирает страницу и читает строки первый раз.</summary>
    /// <param name="read">Отданные сочетания в порядке раздачи и отказы — такими, какие они сейчас.</param>
    /// <param name="file">Путь к <c>keymap.json</c>.</param>
    /// <param name="open">Чем открыть файл — средствами системы.</param>
    public KeysPage(Func<(IReadOnlyList<KeyRow> Rows, IReadOnlyList<KeyRefusal> Refusals)> read, string file, Action<string> open)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(open);

        _read = read;
        (Rows, Refusals) = read();
        File = file;
        _open = open;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    /// <remarks>Правок у страницы нет, и сказать о них ей нечего.</remarks>
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public string Id => "studio.keys";

    /// <inheritdoc/>
    public string Title => Localizer.Instance["keys.title"];

    /// <inheritdoc/>
    public Geometry? Icon => AxIcons.Keyboard;

    /// <inheritdoc/>
    public IReadOnlyList<ISettingsPage> Children => [];

    /// <inheritdoc/>
    /// <remarks>Ищут и сочетание, и команду: «куда делось Ctrl+W» и «чем открыть палитру».</remarks>
    public IEnumerable<string> Terms =>
    [
        Title,
        "keymap.json",
        .. Rows.SelectMany(row => new[] { row.Gesture, row.Command }),
        .. Refusals.SelectMany(refusal => new[] { refusal.Gesture, refusal.Command }),
    ];

    /// <inheritdoc/>
    public bool HasChanges => false;

    /// <summary>Отданные сочетания, в порядке раздачи.</summary>
    public IReadOnlyList<KeyRow> Rows { get; private set; }

    /// <summary>Кому сочетание не досталось.</summary>
    public IReadOnlyList<KeyRefusal> Refusals { get; private set; }

    /// <summary>Сочетания, которые оставил поиск, — в том же порядке раздачи.</summary>
    public IReadOnlyList<KeyRow> ShownRows =>
        _query is { } query ? [.. Rows.Where(row => Matches(query, row.Gesture, row.Command))] : Rows;

    /// <summary>Отказы, которые оставил поиск.</summary>
    public IReadOnlyList<KeyRefusal> ShownRefusals =>
        _query is { } query ? [.. Refusals.Where(refusal => Matches(query, refusal.Gesture, refusal.Command))] : Refusals;

    /// <summary>Раздел отказов виден: есть отказы, и поиск их не спрятал.</summary>
    public bool ShowsRefusals => ShownRefusals.Count > 0;

    /// <summary>Путь к <c>keymap.json</c>.</summary>
    public string File { get; }

    /// <summary>Перечитывает сочетания и отказы.</summary>
    public void Refresh()
    {
        (Rows, Refusals) = _read();

        PropertyChanged.Raise(this, nameof(Rows), nameof(Refusals));
        Shown();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Ищется и сочетание, и команда — тем же правилом, по которому поиск находит страницу:
    /// «куда делось Ctrl+W» и «чем открыть палитру». Порядок раздачи отбор не трогает: он и
    /// объясняет, кому досталось занятое.
    /// </remarks>
    public void Narrow(string? query)
    {
        _query = SettingsSearch.Normalize(query);
        Shown();
    }

    private static bool Matches(string query, string gesture, string command) =>
        SettingsSearch.Matches(gesture, query) || SettingsSearch.Matches(command, query);

    private void Shown()
    {
        PropertyChanged.Raise(this, nameof(ShownRows), nameof(ShownRefusals), nameof(ShowsRefusals));
    }

    /// <summary>Перестаёт слушать реестр: окно, показывавшее страницу, закрыто.</summary>
    public void Dispose()
    {
        _release?.Invoke();
        _release = null;
    }

    /// <summary>
    /// Собирает страницу по реестру сочетаний и начинает его слушать.
    /// </summary>
    /// <param name="shortcuts">Реестр сочетаний студии.</param>
    /// <param name="commands">
    /// Названия команд — те же, что показывает палитра. Спрашиваются при каждом чтении: расширение,
    /// поднятое при открытой странице, приносит и сочетания, и названия.
    /// </param>
    /// <param name="plugin">Имя расширения по идентификатору; <c>null</c> — не нашлось.</param>
    /// <param name="file">Путь к <c>keymap.json</c>.</param>
    /// <param name="open">Чем открыть файл.</param>
    public static KeysPage From(
        StudioShortcuts shortcuts,
        Func<IReadOnlyList<PaletteEntry>> commands,
        Func<string, string?> plugin,
        string file,
        Action<string> open)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(plugin);

        string Source(string? owner, bool personal) =>
            personal ? "keymap.json" : owner is null ? Localizer.Instance["keys.studio"] : plugin(owner) ?? owner;

        (IReadOnlyList<KeyRow>, IReadOnlyList<KeyRefusal>) Read()
        {
            var titles = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var command in commands())
                titles.TryAdd(command.CommandId, command.Title);

            string Title(string id) => titles.TryGetValue(id, out var title) ? title : id;

            // Жест пишется так, как пишет его платформа, и пишется он одним местом на всю студию:
            // страница клавиш и палитра не должны расходиться в записи одного и того же.
            static string Written(Avalonia.Input.KeyGesture gesture) => StudioShortcuts.Written(gesture) ?? string.Empty;

            return (
                [.. shortcuts.All.Select(bound => new KeyRow(Written(bound.Gesture), Title(bound.CommandId), Source(bound.Owner, bound.Personal)))],
                [
                    .. shortcuts.Refused.Select(refusal => new KeyRefusal(
                        Written(refusal.Gesture),
                        Title(refusal.CommandId),
                        string.Format(CultureInfo.CurrentCulture, Localizer.Instance["keys.refused.winner"], Title(refusal.Winner)),
                        Source(refusal.Owner, refusal.Personal))),
                ]);
        }

        var page = new KeysPage(Read, file, open);

        void OnChanged(object? sender, EventArgs e) => page.Refresh();

        shortcuts.Changed += OnChanged;
        page._release = () => shortcuts.Changed -= OnChanged;

        return page;
    }

    /// <summary>
    /// Открывает <c>keymap.json</c>, заведя его, если файла ещё нет.
    /// </summary>
    /// <remarks>
    /// Заведённый файл — пустой объект с подсказкой в комментарии: пустой файл не объяснил бы, что в
    /// него писать, а объект с чужими сочетаниями стал бы решением, которого человек не принимал.
    /// Существующий файл не трогается. Не завёлся — открывать нечего, и открытие молча ничего не
    /// покажет, как любое открытие средствами системы.
    /// </remarks>
    public void OpenFile()
    {
        if (!System.IO.File.Exists(File))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File)!);
                System.IO.File.WriteAllText(File, Template());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        _open(File);
    }

    /// <inheritdoc/>
    public Task CommitAsync(ICollection<string> problems) => Task.CompletedTask;

    /// <inheritdoc/>
    public void Revert()
    {
    }

    private static string Template() =>
        $$"""
        {
          // {{Localizer.Instance["keys.subtitle"]}}
          // "studio.palette": "Ctrl+Alt+P"
        }

        """;
}
