using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ArxisStudio.Sdk;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Поднимает включённые плагины и держит их, пока студия работает.
/// </summary>
/// <remarks>
/// Каждый внешний плагин живёт в своём выгружаемом контексте загрузки: иначе
/// выключить плагин можно было бы только перезапуском студии. Контекст
/// разрешает сборки плагина рядом с его entry-сборкой, а сборки самой студии —
/// SDK, контролы, Avalonia — берёт из основного контекста: два экземпляра одного
/// типа не были бы одним типом, и панель плагина не встала бы в интерфейс.
/// <para>
/// Сбой плагина не должен уронить студию, поэтому загрузка и активация каждого
/// плагина обёрнуты: упавший плагин попадает в список с ошибкой, остальные
/// продолжают работать.
/// </para>
/// </remarks>
public sealed class PluginHost : IDisposable
{
    private static bool _swept;

    private readonly List<LoadedPlugin> _loaded = [];

    private PluginResolution? _resolution;
    private readonly List<InstalledPlugin> _deferred = [];
    private readonly IStudioContextFactory _contexts;

    /// <summary>Создаёт хост.</summary>
    /// <param name="contexts">Чем выдавать плагину его контекст.</param>
    public PluginHost(IStudioContextFactory contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        _contexts = contexts;
    }

    /// <summary>Поднятые плагины.</summary>
    public IReadOnlyList<LoadedPlugin> Loaded => _loaded;

    /// <summary>Плагины, ждущие своего события.</summary>
    public IReadOnlyList<InstalledPlugin> Deferred => _deferred;

    /// <summary>
    /// Итог разрешения зависимостей при старте; null до первого старта.
    /// </summary>
    /// <remarks>
    /// Оболочка проливает отсюда заметки в журнал: об устаревшем
    /// необязательном соседе граф не отказывает, но молчать о нём нельзя.
    /// </remarks>
    public PluginResolution? Resolution => _resolution;

    /// <summary>
    /// Находит плагин, чей код есть в стеке исключения.
    /// </summary>
    /// <param name="error">Исключение, пришедшее без спроса.</param>
    /// <returns>Плагин или null, если в стеке только код студии.</returns>
    /// <remarks>
    /// Так приписываются падения, пришедшие мимо шва: необработанное
    /// исключение потока интерфейса и задача, чьё исключение никто не забрал.
    /// Там некому назвать плагин — его приходится узнавать по стеку.
    /// <para>
    /// Ищется первый кадр чужого кода: студия зовёт плагин, плагин зовёт
    /// студию, и снизу стека может оказаться и то и другое. Виноват тот, чей
    /// код бросил, — он ближе к вершине.
    /// </para>
    /// <para>
    /// Сборку сверяем и по списку плагина, и по его контексту загрузки: список
    /// знает entry-сборку и то, что нашлось рядом, а приватную зависимость
    /// плагина, подгруженную по требованию, знает только контекст.
    /// </para>
    /// </remarks>
    public LoadedPlugin? Blame(Exception? error) => Blame(error, _loaded);

    /// <summary>
    /// Находит среди перечисленных плагинов того, чей код есть в стеке.
    /// </summary>
    /// <param name="error">Исключение, пришедшее без спроса.</param>
    /// <param name="loaded">Где искать.</param>
    /// <returns>Плагин или null, если в стеке только код студии.</returns>
    public static LoadedPlugin? Blame(Exception? error, IReadOnlyCollection<LoadedPlugin> loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        for (var current = error; current is not null; current = current.InnerException)
        {
            foreach (var frame in new StackTrace(current, fNeedFileInfo: false).GetFrames())
            {
                if (frame.GetMethod()?.DeclaringType?.Assembly is not { } assembly)
                    continue;

                if (Owner(assembly, loaded) is { } plugin)
                    return plugin;
            }
        }

        return null;
    }

    private static LoadedPlugin? Owner(Assembly assembly, IEnumerable<LoadedPlugin> loaded) =>
        loaded.FirstOrDefault(plugin =>
            plugin.Assemblies.Contains(assembly) ||
            (plugin.Context is not null && AssemblyLoadContext.GetLoadContext(assembly) == plugin.Context));

    /// <summary>
    /// Принимает каталог и поднимает то, что просит подняться сразу.
    /// </summary>
    /// <param name="plugins">Что нашёл каталог.</param>
    /// <returns>Результаты по поднятым плагинам.</returns>
    /// <remarks>
    /// Остальные остаются в списке ждущих: их меню и панели студия покажет по
    /// манифесту, а сборка поднимется, когда придёт объявленное событие.
    /// </remarks>
    public IReadOnlyList<LoadedPlugin> LoadStartup(IEnumerable<InstalledPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        var enabled = plugins.Where(candidate => candidate is { IsEnabled: true, IsValid: true }).ToList();

        // Граф разрешается на манифестах, до загрузки единой сборки. Уже
        // поднятые — это встроенные модули: они годятся в цели зависимостей.
        _resolution = PluginGraph.Resolve(
            enabled,
            _loaded.Where(loaded => loaded.IsLoaded).Select(loaded => loaded.Installed).ToList());

        var raised = new List<LoadedPlugin>();

        // Отказанный не поднимается и не ждёт событий: будить его нечем и
        // незачем — причина не в событии, а в соседях. Запись с цепочкой
        // причин едет обычной дорогой сбоя подъёма.
        foreach (var refusedId in _resolution.Refused.Keys)
        {
            if (enabled.FirstOrDefault(plugin => plugin.Id == refusedId) is { } refused)
                raised.Add(Fail(refused, _resolution.Refused[refusedId]));
        }

        // Поднимается замыкание нетерпеливых по рёбрам подъёма: нетерпеливый
        // тянет за собой и отложенную зависимость — в момент его активации
        // службы соседа обязаны существовать, а не ждать своего события.
        var eager = Eager(_resolution.Order);

        foreach (var plugin in _resolution.Order)
        {
            // Плагин без entry-сборки — это данные, а не код: языковой
            // пакет в порядок не попадает вовсе, но перестраховка дешевле
            // догадки о том, что Order всегда прав.
            if (plugin.Manifest?.Entry is not { Length: > 0 })
                continue;

            if (eager.Contains(plugin.Id))
                raised.Add(Add(plugin));
            else
                _deferred.Add(plugin);
        }

        return raised;
    }

    /// <summary>
    /// Замыкание нетерпеливых: кого поднимать сразу вместе с их
    /// зависимостями.
    /// </summary>
    private static HashSet<string> Eager(IReadOnlyList<InstalledPlugin> order)
    {
        var eager = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byId = order.ToDictionary(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in order.Where(candidate => PluginActivation.IsEager(candidate.Manifest)))
            Pull(plugin);

        return eager;

        void Pull(InstalledPlugin plugin)
        {
            if (!eager.Add(plugin.Id))
                return;

            foreach (var declared in plugin.Manifest?.Dependencies ?? [])
            {
                // Тянутся и необязательные присутствующие: обещание «сосед
                // стоит подо мной» не делится на обязательных и нет.
                if (declared.Id is { Length: > 0 } id && byId.TryGetValue(id, out var target))
                    Pull(target);
            }
        }
    }

    /// <summary>Кладёт отказ в список поднятых — той же дорогой, что сбой подъёма.</summary>
    private LoadedPlugin Fail(InstalledPlugin installed, string reason)
    {
        var failed = LoadedPlugin.Failed(installed, reason);

        _loaded.Add(failed);
        return failed;
    }

    /// <summary>
    /// Поднимает ждущий плагин.
    /// </summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    /// <returns>
    /// Поднятый плагин или null, если такого среди ждущих нет — он либо уже
    /// поднят, либо выключен.
    /// </returns>
    public LoadedPlugin? Activate(string pluginId)
    {
        var waiting = _deferred.FirstOrDefault(plugin => plugin.Id == pluginId);

        if (waiting is null)
            return null;

        _deferred.Remove(waiting);
        return Add(waiting);
    }

    /// <summary>
    /// Поднимает плагин заново: выгружает старую копию и загружает свежую.
    /// </summary>
    /// <param name="installed">Плагин, перечитанный каталогом с диска.</param>
    /// <returns>Новая копия или причина, почему перезагрузить не вышло.</returns>
    /// <remarks>
    /// Это то, ради чего у внешнего плагина свой выгружаемый контекст: автор
    /// собрал новую сборку, положил её в папку плагина — и увидел её, не
    /// перезапуская студию. Без перезагрузки контекст только и делал бы, что
    /// ждал закрытия окна.
    /// <para>
    /// Встроенный модуль перезагрузить нельзя, и притворяться, что можно, —
    /// худшее из решений: его сборки живут в основном контексте вместе со
    /// сборками самой студии, выгрузить их отдельно нечем, а «перезагрузка»,
    /// которая на деле подняла бы второй экземпляр поверх первого, оставила бы
    /// две копии панелей и два обработчика на каждую команду.
    /// </para>
    /// <para>
    /// Запись каталога передаётся заново, а не берётся у прежней копии:
    /// перезагружают чаще всего потому, что плагин изменился, и вместе со
    /// сборкой мог измениться список его панелей и команд. Читать манифест
    /// хосту нечем — это дело каталога, и он же знает, где плагин лежит.
    /// </para>
    /// <para>
    /// Выгрузка в .NET кооперативная: <c>Unload</c> её только начинает, а
    /// закончится она, когда на типы плагина не останется ни одной ссылки.
    /// Поэтому хост не верит себе на слово и проверяет по слабой ссылке, умер
    /// ли контекст. Не умер — плагин всё равно поднят заново, но в памяти
    /// теперь две копии, и вторая продолжает получать события, на которые
    /// подписалась первая. Об этом надо сказать, а не молчать: молчание тут
    /// хуже, чем честный совет перезапустить студию.
    /// </para>
    /// </remarks>
    public PluginReload Reload(InstalledPlugin installed)
    {
        ArgumentNullException.ThrowIfNull(installed);

        if (Refuse(installed.Id) is { } refusal)
            return new PluginReload(null, refusal, false);

        var released = Released(Retire(installed.Id));

        // Словари читаются с диска и живут дольше подъёма: перезагрузка,
        // оставившая прежний текст, была бы перезагрузкой наполовину — автор
        // правит строки так же часто, как код.
        PluginStrings.Forget(installed.Directory);

        return new PluginReload(Add(installed), null, released);
    }

    /// <summary>
    /// Причина, по которой перезагружать нечего или нельзя; null — можно.
    /// </summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    /// <remarks>
    /// Метод отдаёт строку, а не запись о плагине, и не встраивается: ссылка на
    /// прежнюю копию, оставшаяся в кадре — хоть в переменной, хоть в регистре,
    /// заведённом компилятором, — держала бы её контекст живым, и проверка
    /// выгрузки честно сообщала бы о помехе, которую сама же и создала.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private string? Refuse(string pluginId)
    {
        var loaded = _loaded.FirstOrDefault(plugin => plugin.Installed.Id == pluginId);

        if (loaded is null)
            return $"Плагин {pluginId} не поднят";

        return loaded.Context is null
            ? $"{loaded.Installed.DisplayName} — встроенный модуль: он приезжает вместе со студией, и отдельно от неё его не перезагрузить"
            : null;
    }

    /// <summary>
    /// Снимает прежнюю копию с учёта и начинает выгрузку её контекста.
    /// </summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    /// <returns>Слабая ссылка на контекст — по ней и видно, выгрузился ли он.</returns>
    /// <remarks>
    /// Отдельный метод, и не встраиваемый: ссылка на прежнюю запись, оставшаяся
    /// в переменной вызывающего — хоть в явной, хоть в той, что заведёт себе
    /// компилятор, — держала бы контекст живым, и проверка выгрузки показывала
    /// бы только это.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference Retire(string pluginId)
    {
        var loaded = _loaded.First(plugin => plugin.Installed.Id == pluginId);

        _loaded.Remove(loaded);
        loaded.Unload();

        return new WeakReference(loaded.Context);
    }

    /// <summary>
    /// Дождался ли контекст своей смерти.
    /// </summary>
    /// <remarks>
    /// Сборщик мусора зовётся руками: выгрузка кончается только его проходом, а
    /// ждать его своим чередом значит ответить «не выгрузился» о том, что
    /// выгрузится через секунду. Проходов несколько — у объектов плагина могут
    /// быть финализаторы, и до них очередь доходит не сразу.
    /// </remarks>
    private static bool Released(WeakReference context)
    {
        for (var attempt = 0; attempt < 10 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return !context.IsAlive;
    }

    private LoadedPlugin Add(InstalledPlugin installed)
    {
        var loaded = Load(installed);

        _loaded.Add(loaded);
        return loaded;
    }

    /// <summary>Опускает все поднятые плагины.</summary>
    public void Dispose()
    {
        foreach (var plugin in _loaded)
            plugin.Unload();

        _loaded.Clear();
        _deferred.Clear();
        _resolution = null;
    }

    private LoadedPlugin Load(InstalledPlugin installed)
    {
        if (installed.Manifest?.Entry is not { Length: > 0 } entry)
            return LoadedPlugin.Failed(installed, "В манифесте не указана entry-сборка");

        if (!StudioSdk.Satisfies(installed.Manifest?.Sdk?.Min))
        {
            return LoadedPlugin.Failed(
                installed,
                $"Плагину нужен SDK {installed.Manifest!.Sdk!.Min}, у этой студии {StudioSdk.Version}: обновите студию или соберите плагин под неё");
        }

        var assemblyPath = Path.Combine(installed.Directory, entry);

        if (!File.Exists(assemblyPath))
            return LoadedPlugin.Failed(installed, $"Сборка плагина не найдена: {entry}");

        assemblyPath = Shadow(installed, assemblyPath);

        var context = new PluginLoadContext(installed.Id, assemblyPath);

        try
        {
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

            return Raise(installed, context, [assembly], _contexts.Create(installed));
        }
        catch (Exception e) when (e is ReflectionTypeLoadException or BadImageFormatException
            or FileLoadException or MissingMethodException or TargetInvocationException or InvalidOperationException)
        {
            context.Unload();
            return LoadedPlugin.Failed(installed, Describe(e));
        }
    }

    /// <summary>
    /// Поднимает встроенный модуль: те же точки входа и тот же контракт, но в
    /// основном контексте загрузки и без выгрузки.
    /// </summary>
    /// <param name="assembly">Сборка модуля со встроенным <c>module.json</c>.</param>
    /// <returns>Результат — как у обычного плагина.</returns>
    /// <remarks>
    /// Модуль отличается от плагина только способом доставки: путь подъёма
    /// один, поэтому код между режимами переносим. Сбой модуля точно так же
    /// остаётся записью, а не падением студии.
    /// </remarks>
    public LoadedPlugin LoadBuiltIn(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var (manifest, error) = ModuleManifest.Load(assembly);

        var installed = new InstalledPlugin(
            AppContext.BaseDirectory,
            manifest,
            error,
            IsEnabled: true,
            IsBuiltIn: true);

        var loaded = manifest is null
            ? LoadedPlugin.Failed(installed, error ?? "Манифест модуля не разобрался")
            : Raise(installed, context: null, [assembly], _contexts.Create(installed));

        _loaded.Add(loaded);
        return loaded;
    }

    /// <summary>
    /// Готовит теневую копию сборок плагина и возвращает путь к entry в ней.
    /// </summary>
    /// <remarks>
    /// Загруженная сборка держит свой файл открытым, пока жив её контекст. Без
    /// копии это значит, что автор плагина не может пересобрать его, пока
    /// студия открыта: сборка не запишется, а перезагружать будет нечего.
    /// Именно этот случай перезагрузка и должна закрывать, поэтому плагин
    /// грузится не из своей папки, а из копии рядом.
    /// <para>
    /// Копируется только <c>bin/</c>. Ресурсы плагина — значки, словари —
    /// остаются на месте: путь к его папке студия выдаёт в контексте, и он
    /// должен указывать туда, где плагин установлен, а не туда, где лежит
    /// копия его сборок.
    /// </para>
    /// <para>
    /// Не вышло скопировать — не беда: грузим из папки плагина, как раньше.
    /// Перезагрузка после этого потребует закрыть студию, но сам плагин
    /// поднимется.
    /// </para>
    /// </remarks>
    private static string Shadow(InstalledPlugin installed, string assemblyPath)
    {
        var source = Path.GetDirectoryName(assemblyPath);

        if (source is null)
            return assemblyPath;

        try
        {
            var shadow = Path.Combine(ShadowRoot, $"{installed.Id}-{Guid.NewGuid():N}");

            Directory.CreateDirectory(shadow);

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(shadow, Path.GetRelativePath(source, file));

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }

            return Path.Combine(shadow, Path.GetFileName(assemblyPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return assemblyPath;
        }
    }

    /// <summary>
    /// Где живут теневые копии.
    /// </summary>
    /// <remarks>
    /// Папка чистится при первом обращении: копии выгруженных плагинов
    /// остаются на диске — файл, только что отпущенный контекстом, ещё занят, —
    /// и убрать их получается лишь в следующий запуск.
    /// </remarks>
    private static string ShadowRoot
    {
        get
        {
            var root = Path.Combine(Path.GetTempPath(), "arxis-plugin-shadow");

            if (_swept)
                return root;

            _swept = true;

            foreach (var stale in Directory.Exists(root) ? Directory.EnumerateDirectories(root) : [])
            {
                try
                {
                    Directory.Delete(stale, recursive: true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }

            return root;
        }
    }

    /// <summary>
    /// Заявляет команды, помеченные атрибутом.
    /// </summary>
    /// <remarks>
    /// Плагину остаётся написать метод и повесить на него
    /// <see cref="CommandAttribute"/>: заявка — работа однообразная, и требовать
    /// её от каждого автора значит собирать по ней одни и те же опечатки.
    /// <para>
    /// Обычный метод берётся у объектов самого плагина — точки входа и служб:
    /// они уже созданы, им уже отдан контекст, и команда видит то же состояние,
    /// что и остальной плагин. Создать ради команды второй экземпляр значило бы
    /// вызвать её на объекте, которому студия ничего не давала.
    /// </para>
    /// <para>
    /// В любом другом классе сборки атрибут действует только на статическом
    /// методе: у такого класса нет ни контекста, ни причины существовать в
    /// одном экземпляре.
    /// </para>
    /// </remarks>
    private static void Bind(
        IEnumerable<Assembly> assemblies,
        IEnumerable<object> owners,
        IStudioContext studio)
    {
        foreach (var owner in owners)
            Register(owner.GetType(), owner, studio);

        foreach (var type in assemblies.SelectMany(assembly => assembly.GetTypes()))
        {
            if (type is { IsAbstract: false, IsPublic: true })
                Register(type, owner: null, studio);
        }
    }

    /// <summary>Заявляет команды одного класса.</summary>
    /// <param name="type">Класс, в котором ищем.</param>
    /// <param name="owner">Объект плагина; null — берём только статические методы.</param>
    /// <param name="studio">Контекст, через который заявляются команды.</param>
    private static void Register(Type type, object? owner, IStudioContext studio)
    {
        const BindingFlags Where = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var method in type.GetMethods(Where))
        {
            if (method.GetCustomAttribute<CommandAttribute>() is not { } declared)
                continue;

            // Команда — это «сделай», а не «сделай вот с этим»: параметрам
            // взяться неоткуда, и молча передать null было бы хуже отказа.
            if (method.GetParameters().Length > 0)
                continue;

            if (method.IsStatic != (owner is null))
                continue;

            studio.Commands.Register(declared.Id, () => method.Invoke(owner, null));
        }
    }

    private static LoadedPlugin Raise(
        InstalledPlugin installed,
        PluginLoadContext? context,
        IReadOnlyList<Assembly> assemblies,
        IStudioContext studio)
    {
        try
        {
            var entries = assemblies.SelectMany(assembly => assembly.GetTypes())
                .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(StudioPlugin).IsAssignableFrom(type))
                .Select(Activator.CreateInstance)
                .OfType<StudioPlugin>()
                .ToList();

            var services = assemblies.SelectMany(assembly => assembly.GetTypes())
                .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(StudioService).IsAssignableFrom(type))
                .Select(Activator.CreateInstance)
                .OfType<StudioService>()
                .ToList();

            foreach (var plugin in entries)
                plugin.Activate(studio);

            foreach (var service in services)
                service.Start(studio);

            Bind(assemblies, entries.Cast<object>().Concat(services), studio);

            return new LoadedPlugin(installed, context, assemblies, studio, entries, services, null);
        }
        catch (Exception e) when (e is ReflectionTypeLoadException or BadImageFormatException
            or FileLoadException or MissingMethodException or TargetInvocationException or InvalidOperationException)
        {
            context?.Unload();
            return LoadedPlugin.Failed(installed, Describe(e));
        }
    }

    private static string Describe(Exception error) =>
        error is TargetInvocationException { InnerException: { } inner } ? inner.Message : error.Message;
}

/// <summary>
/// Чем кончилась перезагрузка плагина.
/// </summary>
/// <param name="Plugin">Новая копия; null, если перезагрузить не вышло.</param>
/// <param name="Error">Почему не вышло; null, если всё получилось.</param>
/// <param name="Released">
/// Выгрузился ли контекст прежней копии. Нет — значит, на её типы кто-то ещё
/// ссылается: подписка на событие студии, оставленный таймер, работающий поток.
/// Плагин при этом поднят, но старая копия осталась в памяти и продолжает
/// получать то, на что подписалась.
/// </param>
public sealed record PluginReload(LoadedPlugin? Plugin, string? Error, bool Released);

/// <summary>Кто выдаёт плагину его контекст.</summary>
public interface IStudioContextFactory
{
    /// <summary>Создаёт контекст для плагина.</summary>
    /// <param name="plugin">Плагин, которому он предназначен.</param>
    IStudioContext Create(InstalledPlugin plugin);
}

/// <summary>Поднятый плагин или причина, почему он не поднялся.</summary>
/// <param name="Installed">Плагин каталога.</param>
/// <param name="Context">
/// Выгружаемый контекст загрузки; null у встроенного модуля — его сборки живут
/// в основном контексте и не выгружаются.
/// </param>
/// <param name="Assemblies">Сборки плагина.</param>
/// <param name="Studio">Контекст, выданный плагину при подъёме.</param>
/// <param name="Entries">Точки входа плагина.</param>
/// <param name="Services">Службы плагина.</param>
/// <param name="Error">Почему плагин не поднялся; null, если поднялся.</param>
public sealed record LoadedPlugin(
    InstalledPlugin Installed,
    AssemblyLoadContext? Context,
    IReadOnlyList<Assembly> Assemblies,
    IStudioContext? Studio,
    IReadOnlyList<StudioPlugin> Entries,
    IReadOnlyList<StudioService> Services,
    string? Error)
{
    /// <summary>Плагин работает.</summary>
    public bool IsLoaded => Error is null;

    /// <summary>Собирает запись о плагине, который поднять не удалось.</summary>
    /// <param name="installed">Плагин каталога.</param>
    /// <param name="error">Почему не удалось.</param>
    public static LoadedPlugin Failed(InstalledPlugin installed, string error) =>
        new(installed, null, [], null, [], [], error);

    /// <summary>Останавливает плагин и выгружает его сборки.</summary>
    public void Unload()
    {
        foreach (var service in Services)
            Safely(service.Stop);

        foreach (var plugin in Entries)
            Safely(plugin.Deactivate);

        (Context as PluginLoadContext)?.Unload();

        // Плагин, упавший на прощание, уже никому не мешает: студия его
        // отпускает, и держаться за исключение незачем.
        static void Safely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
            {
            }
        }
    }
}

/// <summary>
/// Контекст загрузки одного плагина.
/// </summary>
/// <remarks>
/// Сборки студии сюда не тянутся: общий тип должен быть один на всех, иначе
/// <c>StudioPlugin</c> плагина и <c>StudioPlugin</c> студии окажутся разными
/// типами. Поэтому разрешаются только те сборки, что лежат рядом с плагином, а
/// всё остальное отдаётся основному контексту.
/// </remarks>
internal sealed class PluginLoadContext(string name, string entryPath)
    : AssemblyLoadContext($"arxis-plugin:{name}", isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(entryPath);

    protected override Assembly? Load(AssemblyName assemblyName) =>
        _resolver.ResolveAssemblyToPath(assemblyName) is { } path && !IsShared(assemblyName)
            ? LoadFromAssemblyPath(path)
            : null;

    private static bool IsShared(AssemblyName assemblyName) =>
        assemblyName.Name is { } name &&
        (name.StartsWith("Avalonia", StringComparison.Ordinal) ||
         name.StartsWith("ArxisStudio.Sdk", StringComparison.Ordinal) ||
         name.StartsWith("ArxisStudio.Controls", StringComparison.Ordinal));
}
