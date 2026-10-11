using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Media;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>
/// Библиотеки образцов цвета файлами: у человека — в профиле (<c>%AppData%/ArxisStudio/colors</c>), у решения — в
/// <c>.arxis/colors</c> рядом с ним, как доска дизайнера; это хранилище окна «Цвет» (<see cref="IAxColorPresetStore"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Файл — библиотека:</b> имя и образцы по порядку, цвет — записью разметки. Пишется он целиком после каждой правки,
/// через временный файл и подмену, а непрочитанный пропускается с предупреждением в журнал: сломанный файл не
/// отнимает остальные библиотеки.
/// </para>
/// <para>
/// <b>Профиль</b> переносит переменная <see cref="EnvironmentVariable"/>, а <c>0</c> держит его библиотеки в памяти —
/// так живёт процесс тестов. Без единой библиотеки в профиле есть одна по умолчанию, и файлом она становится при первой
/// правке. <b>Решение</b> спрашивается живьём: открыли другое — его библиотеки перечитываются при следующем показе.
/// </para>
/// <para>
/// Текущая библиотека и вид помнятся, пока студия открыта: это память окна, а не то, что правит человек.
/// </para>
/// </remarks>
internal sealed class ColorLibraries : IAxColorPresetStore
{
    /// <summary>Переменная среды: папка библиотек профиля или <c>0</c> — держать их в памяти.</summary>
    public const string EnvironmentVariable = "ARXIS_COLORS";

    /// <summary>Номер формата файла: растёт, когда старый читатель перестаёт понимать новый файл.</summary>
    public const int Version = 1;

    /// <summary>
    /// Отступы и кириллица как есть: библиотека решения едет в репозитории, и её читают в просмотре изменений глазами.
    /// </summary>
    private static readonly JsonSerializerOptions Writing = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private static readonly JsonDocumentOptions Reading = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string? _userFolder;
    private readonly Func<string?> _projectPath;
    private readonly string _defaultName;
    private readonly Action<string>? _warn;
    private readonly List<AxColorPresetLibrary> _user = [];
    private readonly List<AxColorPresetLibrary> _project = [];
    private readonly Dictionary<AxColorPresetLibrary, string> _files = [];
    private bool _userRead;
    private string? _projectFolder;
    private AxColorPresetLibrary? _current;
    private AxColorPresetView _view;

    /// <summary>Заводит хранилище.</summary>
    /// <param name="userFolder">Папка библиотек профиля; пусто — держать их в памяти.</param>
    /// <param name="projectPath">Открытое решение или проект — спрашивается живьём; пусто — решения нет.</param>
    /// <param name="defaultName">Имя библиотеки по умолчанию.</param>
    /// <param name="warn">Куда сказать о непрочитанном файле.</param>
    public ColorLibraries(string? userFolder, Func<string?> projectPath, string defaultName, Action<string>? warn = null)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(defaultName);

        _userFolder = userFolder;
        _projectPath = projectPath;
        _defaultName = defaultName;
        _warn = warn;
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public IReadOnlyList<AxColorPresetLibrary> Libraries
    {
        get
        {
            Refresh();

            return [.. _user, .. _project];
        }
    }

    /// <inheritdoc/>
    public AxColorPresetLibrary Current
    {
        get
        {
            Refresh();

            return _current is not null && (_user.Contains(_current) || _project.Contains(_current)) ? _current : _user[0];
        }
    }

    /// <inheritdoc/>
    public AxColorPresetView View
    {
        get => _view;
        set
        {
            if (_view == value)
                return;

            _view = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Хранилище модуля: профиль — по переменной среды или в папке данных человека, решение — контекста.</summary>
    /// <param name="context">Контекст модуля.</param>
    public static ColorLibraries For(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new ColorLibraries(
            UserFolder(),
            () => context.ProjectPath,
            context.Strings["inspector.colors.default"],
            message => context.Log.Write(StudioLogLevel.Warning, InspectorModule.LogSource, message));
    }

    /// <summary>Папка библиотек профиля: по переменной среды или в папке данных человека; пусто — держать в памяти.</summary>
    public static string? UserFolder()
    {
        var folder = Environment.GetEnvironmentVariable(EnvironmentVariable)
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
                "ArxisStudio",
                "colors");

        return string.IsNullOrWhiteSpace(folder) || folder == "0" ? null : folder;
    }

    /// <summary>Папка библиотек решения — <c>.arxis/colors</c> рядом с ним; пусто — решения нет.</summary>
    /// <param name="projectPath">Открытое решение, проект или папка.</param>
    public static string? ProjectFolder(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return null;

        var folder = Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath);

        return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, ".arxis", "colors");
    }

    /// <inheritdoc/>
    public bool CanCreate(AxColorPresetLocation location) =>
        location == AxColorPresetLocation.User || ProjectFolder(_projectPath()) is not null;

    /// <inheritdoc/>
    public void Select(AxColorPresetLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        if (ReferenceEquals(Current, library))
            return;

        _current = library;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public AxColorPresetLibrary Create(string name, AxColorPresetLocation location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Refresh();

        var folder = location == AxColorPresetLocation.User ? _userFolder : _projectFolder;

        if (location == AxColorPresetLocation.Project && folder is null)
            throw new InvalidOperationException("Библиотеку решения не завести: решение не открыто.");

        var library = new AxColorPresetLibrary(name.Trim(), location);

        (location == AxColorPresetLocation.User ? _user : _project).Add(library);

        if (folder is not null)
            _files[library] = FreeFile(folder, library.Name);

        Save(library);
        _current = library;
        Changed?.Invoke(this, EventArgs.Empty);

        return library;
    }

    /// <inheritdoc/>
    public void Save(AxColorPresetLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        if (!_files.TryGetValue(library, out var file))
        {
            var folder = library.Location == AxColorPresetLocation.User ? _userFolder : _projectFolder;

            if (folder is null)
                return;

            file = FreeFile(folder, library.Name);
            _files[library] = file;
        }

        var presets = new JsonArray();

        foreach (var preset in library.Presets)
            presets.Add(new JsonObject { ["name"] = preset.Name, ["color"] = AxColorField.Format(preset.Color) });

        var json = new JsonObject { ["version"] = Version, ["name"] = library.Name, ["presets"] = presets };

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        var temporary = file + ".tmp";

        File.WriteAllText(temporary, json.ToJsonString(Writing));
        File.Move(temporary, file, overwrite: true);
    }

    /// <summary>Читает профиль один раз, а решение — когда оно сменилось.</summary>
    private void Refresh()
    {
        if (!_userRead)
        {
            _userRead = true;
            _user.AddRange(Read(_userFolder, AxColorPresetLocation.User));

            // Без единой библиотеки в профиле — одна по умолчанию: файлом она станет при первой правке.
            if (_user.Count == 0)
                _user.Add(new AxColorPresetLibrary(_defaultName, AxColorPresetLocation.User));
        }

        var project = ProjectFolder(_projectPath());

        if (string.Equals(project, _projectFolder, StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var library in _project)
            _files.Remove(library);

        _project.Clear();
        _projectFolder = project;
        _project.AddRange(Read(project, AxColorPresetLocation.Project));
    }

    private List<AxColorPresetLibrary> Read(string? folder, AxColorPresetLocation location)
    {
        var libraries = new List<AxColorPresetLibrary>();

        if (folder is null || !Directory.Exists(folder))
            return libraries;

        foreach (var file in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            if (ReadFile(file, location) is { } library)
            {
                libraries.Add(library);
                _files[library] = file;
            }
        }

        return libraries;
    }

    private AxColorPresetLibrary? ReadFile(string file, AxColorPresetLocation location)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file), Reading);
            var root = document.RootElement;
            var name = root.TryGetProperty("name", out var named) && named.GetString() is { Length: > 0 } text
                ? text
                : Path.GetFileNameWithoutExtension(file);
            var presets = new List<AxColorPreset>();

            if (root.TryGetProperty("presets", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("color", out var code) && Color.TryParse(code.GetString(), out var color))
                    {
                        var label = item.TryGetProperty("name", out var title) ? title.GetString() : null;

                        presets.Add(new AxColorPreset(label ?? AxColorField.Format(color), color));
                    }
                }
            }

            return new AxColorPresetLibrary(name, location, presets);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            _warn?.Invoke($"Библиотека образцов «{file}» не прочитана: {e.Message}");
            return null;
        }
    }

    /// <summary>Свободное имя файла для библиотеки: имя без запрещённых знаков, а занятое — с номером.</summary>
    private string FreeFile(string folder, string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string([.. name.Select(letter => invalid.Contains(letter) ? '_' : letter)]).Trim().TrimEnd('.');

        if (safe.Length == 0)
            safe = "colors";

        var taken = _files.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var file = Path.Combine(folder, safe + ".json");

        for (var number = 2; taken.Contains(file) || File.Exists(file); number++)
            file = Path.Combine(folder, $"{safe} ({number}).json");

        return file;
    }
}
