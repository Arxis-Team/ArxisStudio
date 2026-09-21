using ArxisStudio.LocalHistory;
using ArxisStudio.Modules.Projects.Engine;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects.History;

/// <summary>Метки действий, которые история пишет сама.</summary>
/// <param name="strings">Словари модуля.</param>
internal sealed class HistoryWords(IStudioStrings strings)
{
    /// <summary>Правка мимо студии, увиденная на ходу.</summary>
    public string External => strings["module.projects.history.external"];

    /// <summary>Правки, сделанные, пока студия не смотрела, — найденные опорным снимком.</summary>
    public string Offline => strings["module.projects.history.offline"];
}

/// <summary>
/// Локальная история службы проектов: хранилище, очередь записи и история каждой сессии.
/// </summary>
/// <remarks>
/// <para>
/// <b>Своя очередь.</b> Запись истории — чтение, хэширование и сжатие файлов — идёт своей полосой,
/// а не полосой движка: загрузка решения не должна ждать, пока история снимет большой файл, а
/// история — пока MSBuild дочитает проект. Хранилище открывается первым делом этой же полосы:
/// чтение журнала за пять дней — не работа для потока интерфейса.
/// </para>
/// <para>
/// <b>Где лежит.</b> В машинной папке пользователя (<c>%LocalAppData%/ArxisStudio/LocalHistory</c>),
/// а не рядом с настройками: история большая и к машине привязана, и уезжать с роумингом профиля
/// ей незачем — IntelliJ держит свою там же. Переменная среды <see cref="EnvironmentVariable"/>
/// переносит её в другую папку, а значение <c>0</c> выключает вовсе: так живут тесты, чтобы не
/// писать в историю человека.
/// </para>
/// <para>
/// <b>Вторая студия.</b> Папку держит одна студия. Вторая, открывшая ту же, историю не пишет и
/// говорит об этом в журнал один раз — спорить за журнал с первой ей не за что.
/// </para>
/// </remarks>
internal sealed class HistoryRecorder : IDisposable
{
    /// <summary>Переменная среды: папка истории или <c>0</c> — не вести её.</summary>
    public const string EnvironmentVariable = "ARXIS_LOCAL_HISTORY";

    /// <summary>Как часто известное состояние пишется на диск, если оно менялось.</summary>
    private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(30);

    /// <summary>Как часто очистка идёт сама, кроме открытия.</summary>
    private static readonly TimeSpan PruneEvery = TimeSpan.FromHours(6);

    private readonly IStudioContext _context;
    private readonly string? _root;
    private readonly FileChangeCoalescingOptions _coalescing;
    private readonly Lane _lane;
    private LocalHistoryStore? _store;
    private ProjectsSettings _settings;
    private DateTimeOffset _flushed;
    private DateTimeOffset _pruned;
    private bool _disposed;

    /// <summary>Заводит историю службы.</summary>
    /// <param name="context">Контекст модуля: настройки, словари, журнал.</param>
    /// <param name="root">Папка истории; null — история не ведётся.</param>
    /// <param name="coalescing">Склейка событий наблюдателей.</param>
    public HistoryRecorder(IStudioContext context, string? root, FileChangeCoalescingOptions coalescing)
    {
        _context = context;
        _root = root;
        _coalescing = coalescing;
        _settings = ProjectsSettings.Read(context.Settings);
        Words = new HistoryWords(context.Strings);
        _lane = new Lane(error => context.Log.Write(
            StudioLogLevel.Warning, ProjectsModule.LogSource, $"Локальная история: {error.Message}"));

        if (IsOn)
            _lane.Enqueue(Open);
    }

    /// <summary>Метки действий.</summary>
    public HistoryWords Words { get; }

    /// <summary>Ведётся ли история: есть где и не выключена человеком.</summary>
    public bool IsOn => _root is not null && Volatile.Read(ref _settings).History;

    /// <summary>Хранилище; null — не открыто, выключено или его держит другая студия.</summary>
    public LocalHistoryStore? Store => Volatile.Read(ref _store);

    /// <summary>Завершится, когда очередь закрыта и последнее дело кончилось.</summary>
    public Task Completion => _lane.Completion;

    /// <summary>Где вести историю: явная папка, иначе переменная среды, иначе машинная папка пользователя.</summary>
    /// <param name="explicitRoot">Папка, названная шовом службы; null — по среде.</param>
    /// <returns>Папка; null — история не ведётся.</returns>
    public static string? Root(string? explicitRoot)
    {
        if (!string.IsNullOrEmpty(explicitRoot))
            return explicitRoot;

        var variable = Environment.GetEnvironmentVariable(EnvironmentVariable);

        if (variable is "0" or "off")
            return null;

        if (!string.IsNullOrWhiteSpace(variable))
            return variable;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            "ArxisStudio",
            "LocalHistory");
    }

    /// <summary>История новой сессии; null — история не ведётся.</summary>
    public HistoryWatcher? Watch() => IsOn && !_disposed ? new HistoryWatcher(this, _coalescing) : null;

    /// <summary>Ставит дело в очередь записи.</summary>
    /// <param name="work">Дело.</param>
    /// <returns><c>false</c> — очередь закрыта.</returns>
    public bool Enqueue(Func<Task> work) => _lane.Enqueue(work);

    /// <summary>
    /// Принимает новые настройки: включает, выключает, меняет срок и пределы.
    /// </summary>
    /// <param name="settings">Настройки.</param>
    /// <returns>Поменялось ли, вести историю или нет.</returns>
    public bool Configure(ProjectsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var was = IsOn;

        Volatile.Write(ref _settings, settings);

        _lane.Enqueue(() =>
        {
            if (IsOn)
            {
                if (Store is { } store)
                {
                    store.Options = Options(settings);
                    Prune(store);
                }
                else
                {
                    return Open();
                }
            }
            else
            {
                Close();
            }

            return Task.CompletedTask;
        });

        return was != IsOn;
    }

    /// <summary>
    /// Пачка записана: известное состояние уходит на диск не чаще раза в полминуты, очистка — раз в
    /// несколько часов. Зовётся из очереди записи.
    /// </summary>
    /// <param name="checkpoint">
    /// Кончился обход — записать состояние сейчас. Пачек после опорного снимка может не быть
    /// долго, и без этого всё, что он узнал, жило бы только в памяти: упади студия — и следующий
    /// запуск снимал бы решение заново.
    /// </param>
    public void Written(bool checkpoint = false)
    {
        if (Store is not { } store)
            return;

        var now = DateTimeOffset.UtcNow;

        if (checkpoint || now - _flushed >= FlushEvery)
        {
            store.Flush();
            _flushed = now;
        }

        if (now - _pruned >= PruneEvery)
            Prune(store);
    }

    /// <summary>Закрывает очередь; хранилище закроется последним её делом.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _lane.Enqueue(() =>
        {
            Close();
            return Task.CompletedTask;
        });
        _lane.Complete();
    }

    private Task Open()
    {
        if (Store is not null || _root is null || _disposed)
            return Task.CompletedTask;

        try
        {
            var store = LocalHistoryStore.Open(_root, Options(Volatile.Read(ref _settings)));

            Volatile.Write(ref _store, store);
            _flushed = DateTimeOffset.UtcNow;
            Prune(store);
        }
        catch (LocalHistoryBusyException)
        {
            _context.Log.Write(StudioLogLevel.Info, ProjectsModule.LogSource,
                $"Локальную историю в {_root} ведёт другая студия: эта её не пишет");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _context.Log.Write(StudioLogLevel.Warning, ProjectsModule.LogSource,
                $"Локальная история не открылась в {_root}: {e.Message}");
        }

        return Task.CompletedTask;
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _store, null) is { } store)
            store.Dispose();
    }

    private void Prune(LocalHistoryStore store)
    {
        store.Prune();
        _pruned = DateTimeOffset.UtcNow;
    }

    private static LocalHistoryOptions Options(ProjectsSettings settings) => new()
    {
        Days = settings.HistoryDays,
        MaxFileBytes = settings.HistoryMaxFileMb * 1024L * 1024,
        MaxTotalBytes = settings.HistoryMaxTotalMb * 1024L * 1024,
    };
}
