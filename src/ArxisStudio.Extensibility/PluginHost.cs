using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Platform;

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

    // Снимок для читающих: список правит поток интерфейса, а спрашивают его и со стороны —
    // служба соседей из фоновой работы плагина, разбор исключения забытой задачи из потока
    // финализатора. Перебор живого списка под правкой бросал бы «коллекция изменена» в чужом
    // кадре; снимок меняется целиком и одной записью.
    private volatile IReadOnlyList<LoadedPlugin> _standing = [];

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

    /// <summary>
    /// Поднятые плагины — снимок на момент вопроса.
    /// </summary>
    /// <remarks>
    /// Читать можно из любого потока: снимок неизменяем и подменяется целиком. Запись у плагина
    /// одна — поднятая или с ошибкой, — и новая попытка прежнюю вытесняет.
    /// </remarks>
    public IReadOnlyList<LoadedPlugin> Loaded => _standing;

    /// <summary>Плагины, ждущие своего события.</summary>
    public IReadOnlyList<InstalledPlugin> Deferred => _deferred;

    /// <summary>
    /// Состав поднятых изменился: подъём, выгрузка, перезагрузка.
    /// </summary>
    /// <remarks>
    /// Во время старта и каскадов событие приходит на каждый шаг: подписчик,
    /// читающий <see cref="Loaded"/> из обработчика, видит промежуточные
    /// состояния — это цена того, что о каждом изменении сказано сразу.
    /// </remarks>
    public event EventHandler? Changed;

    /// <summary>
    /// Плагин уходит: его контекст сейчас выгрузят.
    /// </summary>
    /// <remarks>
    /// Отсюда узнают реестры, чьи записи заведены на владельца: команды,
    /// экспорты, вклады. Оставленная запись не просто мусор — она держит
    /// сильную ссылку на объект из выгружаемого контекста, и тот не умрёт
    /// никогда: получал бы вызовы, держал типы, не давал контексту
    /// собраться. Раньше эту уборку переписывал каждый, кто выгружает, и
    /// списки успели разъехаться.
    /// <para>
    /// Зовётся до выгрузки — подписчику может понадобиться сам плагин, — и
    /// на всякой дороге: перезагрузка, снятие упавшего, закрытие студии.
    /// </para>
    /// </remarks>
    public event EventHandler<string>? Unloading;

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
    public LoadedPlugin? Blame(Exception? error) => Blame(error, _standing);

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

        // Контракты грузятся раньше графа и у всех сразу — и у отложенных, и
        // у плагинов без entry: типы должны существовать к моменту, когда их
        // коснётся любой сосед, а не к моменту подъёма владельца. Раньше
        // графа — потому что отказ из-за контракта обязан разойтись по
        // зависимым так же, как всякий другой: зависимый, чьи типы не
        // приехали, иначе поднялся бы и упал на первом же приведении.
        var contractNotes = new List<string>();
        var contractless = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in enabled)
        {
            if (PluginContracts.EnsureLoaded(plugin, contractNotes) is { } refusal)
                contractless[plugin.Id] = refusal;
        }

        // Граф разрешается на манифестах, до загрузки единой сборки. Уже
        // поднятые — это встроенные модули: они годятся в цели зависимостей.
        _resolution = PluginGraph.Resolve(
            enabled,
            _loaded.Where(loaded => loaded.IsLoaded).Select(loaded => loaded.Installed).ToList(),
            contractless,
            contractNotes);

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

            if (!eager.Contains(plugin.Id))
            {
                _deferred.Add(plugin);
                continue;
            }

            // Подъём одного не стоит остальным ничего. Список собирается
            // целиком и только потом уходит зовущему: бросок на середине
            // терял бы и следующих, и уже поднятых — их никто бы не принял,
            // хотя в памяти они уже есть.
            try
            {
                raised.Add(Add(plugin));
            }
            catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
            {
                raised.Add(Fail(plugin, Describe(e)));
            }
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

        Keep(failed);
        return failed;
    }

    /// <summary>
    /// Ставит запись на учёт, вытесняя прежнюю запись об ошибке того же плагина.
    /// </summary>
    /// <remarks>
    /// Запись у плагина одна. Пока прежняя оставалась лежать рядом, хост находил первой её: плагин,
    /// упавший на старте и поднятый заново после починки, на «Перезагрузить» получал отказ словами
    /// о встроенном модуле, а отключение за сбои снимало мёртвую запись и оставляло живую копию
    /// работать. Вытесняется только запись об ошибке: поднятого снимает <see cref="Retire"/>, и
    /// никто другой.
    /// </remarks>
    private void Keep(LoadedPlugin record)
    {
        _loaded.RemoveAll(known => !known.IsLoaded && Same(known.Installed.Id, record.Installed.Id));
        _loaded.Add(record);

        _standing = [.. _loaded];
    }

    /// <summary>Поднят ли плагин с этим идентификатором.</summary>
    private bool Stands(string pluginId) =>
        _loaded.Any(known => known.IsLoaded && Same(known.Installed.Id, pluginId));

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);

    /// <summary>
    /// Поднимает ждущий плагин, а прежде — всё, что обязано стоять под ним.
    /// </summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    /// <returns>
    /// Поднятые в порядке подъёма: зависимости первыми, просимый последним;
    /// пусто — среди ждущих такого нет: он либо уже поднят, либо выключен.
    /// </returns>
    /// <remarks>
    /// Зависимость с собственными событиями активации поднимается здесь же,
    /// раньше срока: в момент активации просимого службы соседа обязаны
    /// существовать, а не ждать своего события.
    /// <para>
    /// Метод реэнтерабелен поневоле: активация — чужой код, и он может позвать
    /// команду соседа, а та — разбудить кого-то ещё. Поэтому цепочка снимается
    /// заранее, а перед каждым подъёмом плагин перепроверяется в списке
    /// ждущих: вложенный вызов мог его уже поднять.
    /// </para>
    /// </remarks>
    public IReadOnlyList<LoadedPlugin> Activate(string pluginId)
    {
        if (_deferred.All(plugin => plugin.Id != pluginId))
            return [];

        var chain = Chain(pluginId);
        var raised = new List<LoadedPlugin>();

        foreach (var plugin in chain)
        {
            var waiting = _deferred.FirstOrDefault(candidate => candidate.Id == plugin.Id);

            if (waiting is null)
                continue;

            _deferred.Remove(waiting);
            raised.Add(Add(waiting));
        }

        return raised;
    }

    /// <summary>
    /// Снимает плагин с ожидания: его выключили или убрали, и будить его больше нечем.
    /// </summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    /// <returns><c>true</c>, если плагин ждал.</returns>
    /// <remarks>
    /// Ждущий — это включённый плагин, чья сборка ещё не понадобилась. Выключить его значит убрать
    /// отсюда: запись, оставшаяся в ожидании, поднимала выключенного первым же его событием —
    /// щелчком по кнопке, жестом, открытием файла его типа.
    /// </remarks>
    public bool Withdraw(string pluginId)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);

        return _deferred.RemoveAll(waiting => Same(waiting.Id, pluginId)) > 0;
    }

    /// <summary>
    /// Цепочка подъёма: просимый и его ждущие зависимости, в порядке графа.
    /// </summary>
    private List<InstalledPlugin> Chain(string pluginId)
    {
        var byId = _deferred.ToDictionary(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Pull(pluginId);

        // Порядок берётся из разрешения старта, а не выдумывается заново:
        // правило одно, и оно уже посчитано.
        var order = _resolution?.Order ?? [];
        var position = order
            .Select((plugin, index) => (plugin.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.OrdinalIgnoreCase);

        return wanted
            .Select(id => byId[id])
            .OrderBy(plugin => position.TryGetValue(plugin.Id, out var index) ? index : int.MaxValue)
            .ThenBy(plugin => plugin.Id, StringComparer.Ordinal)
            .ToList();

        void Pull(string id)
        {
            if (!byId.TryGetValue(id, out var plugin) || !wanted.Add(id))
                return;

            foreach (var declared in plugin.Manifest?.Dependencies ?? [])
            {
                // Тянутся и необязательные: раз сосед установлен и ждёт,
                // обещание «он стоит подо мной» должно быть сдержано.
                if (declared.Id is { Length: > 0 } target)
                    Pull(target);
            }
        }
    }

    /// <summary>
    /// Почему поднятый плагин выгрузится только перезапуском студии; null — ничто из видимого не держит.
    /// </summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    /// <returns>Причина словами: чьи свойства и события Avalonia держат его сборки.</returns>
    /// <remarks>
    /// Спрашивать надо перед спуском, а не при подъёме: свойство заводится, когда код плагина
    /// впервые тронул свой тип, — просмотрщик заводит свойства AvaloniaEdit на первом открытом
    /// файле, — и до того тот же плагин выгружается чисто.
    /// <para>
    /// Не встраивается — по той же причине, что <see cref="Retire"/>: запись о плагине, оставшаяся
    /// в кадре вызывающего, держала бы его контекст.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string? Pinned(string pluginId)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);

        return _loaded.FirstOrDefault(plugin => Same(plugin.Installed.Id, pluginId))?.Context is PluginLoadContext context
            ? context.Pinned()
            : null;
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

        var cascade = Reload([installed.Id], [installed]);

        if (cascade.Skipped.TryGetValue(installed.Id, out var refusal))
            return new PluginReload(null, refusal, false, cascade.Notes);

        var raised = cascade.Raised.Single();

        // Ошибка берётся с самой записи, а не подставляется null: поднятая
        // копия может быть записью со сбоем — например, контракт объявлен и
        // не загрузился. Иначе вызывающий, спрашивающий «Error is null?»,
        // отчитался бы об успешной перезагрузке того, что не поднялось.
        return new PluginReload(
            raised,
            raised.Error,
            cascade.Released[installed.Id],
            cascade.Notes);
    }

    /// <summary>
    /// Опускает перечисленных и поднимает свежие копии — одним каскадом.
    /// </summary>
    /// <param name="lower">Кого опустить: зависимые первыми, зависимость последней.</param>
    /// <param name="raise">Кого поднять: зависимость первой, зависимые следом.</param>
    /// <returns>Судьба каждого: выгрузился ли, поднялся ли, почему пропущен.</returns>
    /// <remarks>
    /// Зависимый держит соседа живым так же, как забытая подписка: перезагрузи
    /// мы одну зависимость, её прежний контекст не умер бы, пока стоит
    /// зависимый. Поэтому опускается вся ветка, а поднимается в обратном
    /// порядке.
    /// <para>
    /// Проход сборщика мусора один на всех, а не по десять на каждого: ждать
    /// надо смерти всех контекстов разом, и меряется она после того, как
    /// выгрузка начата у всех.
    /// </para>
    /// </remarks>
    public PluginCascade Reload(IReadOnlyList<string> lower, IReadOnlyList<InstalledPlugin> raise)
    {
        ArgumentNullException.ThrowIfNull(lower);
        ArgumentNullException.ThrowIfNull(raise);

        var skipped = new Dictionary<string, string>(StringComparer.Ordinal);
        var retired = new Dictionary<string, WeakReference>(StringComparer.Ordinal);

        foreach (var pluginId in lower)
        {
            if (Refuse(pluginId) is { } refusal)
                skipped[pluginId] = refusal;
            else
                retired[pluginId] = Retire(pluginId);
        }

        var released = ReleasedAll(retired);

        var raised = new List<LoadedPlugin>();
        var notes = new List<string>();

        var raising = raise.Where(plugin => !skipped.ContainsKey(plugin.Id)).ToList();

        // Контракты перечитываются у всех до первого подъёма: автор мог
        // пересобрать и их, а выгрузить прежнюю копию из общего контекста
        // нечем — честнее сказать про перезапуск, чем промолчать. До подъёма —
        // потому что отказ обязан разойтись по зависимым прежде, чем кого-то
        // поднимут.
        var contractless = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var restart = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var installed in raising)
        {
            var stale = new List<string>();

            if (PluginContracts.EnsureLoaded(installed, notes, stale) is { } refusal)
                contractless[installed.Id] = refusal;

            // Новый контракт в общий контекст не встанет до перезапуска: плагин поднят со старым
            // или не поднят вовсе. Это не отказ плагину, а повод перезапустить студию.
            if (stale.Count > 0)
                restart[installed.Id] = string.Join("; ", stale);
        }

        var refused = Spread(contractless, raising);

        foreach (var installed in raising)
        {
            if (refused.TryGetValue(installed.Id, out var reason))
            {
                raised.Add(Fail(installed, reason));
                continue;
            }

            // Поднимать просят и того, кого не опускали: включённый в настройках плагин мог успеть
            // подняться своим событием. Второй копии не будет.
            if (Stands(installed.Id))
            {
                notes.Add($"{installed.DisplayName} уже поднят — второй раз не поднимается");
                continue;
            }

            // Словари читаются с диска и живут дольше подъёма: перезагрузка,
            // оставившая прежний текст, была бы перезагрузкой наполовину —
            // автор правит строки так же часто, как код.
            PluginStrings.Forget(installed.Directory);

            raised.Add(Add(installed));
        }

        // Слабые ссылки отдаются как есть: сильной на контекст здесь нет, и вопрос «выгрузился ли он
        // теперь» потом не удержит его сам.
        var lingering = retired
            .Where(pair => !released[pair.Key])
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return new PluginCascade(released, raised, skipped, notes, lingering, restart);
    }

    /// <summary>
    /// Ждёт смерти всех выгружаемых контекстов разом.
    /// </summary>
    /// <remarks>
    /// Ссылки приходят словарём слабых: сильной ссылки на прежние записи здесь
    /// уже нет — их снял <see cref="Retire"/>, и держать их в кадре нельзя.
    /// </remarks>
    private static Dictionary<string, bool> ReleasedAll(Dictionary<string, WeakReference> retired)
    {
        for (var attempt = 0; attempt < 10 && retired.Values.Any(context => context.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return retired.ToDictionary(pair => pair.Key, pair => !pair.Value.IsAlive, StringComparer.Ordinal);
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
        var loaded = _loaded.FirstOrDefault(plugin => Same(plugin.Installed.Id, pluginId));

        if (loaded is null)
            return $"Плагин {pluginId} не поднят";

        // Спрашивается сам признак, а не контекст загрузки: контекста нет и у записи об ошибке, и
        // упавший внешний плагин получал бы отказ словами о модуле — вместо новой попытки.
        return loaded.Installed.IsBuiltIn
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
        var loaded = _loaded.First(plugin => Same(plugin.Installed.Id, pluginId));

        _loaded.Remove(loaded);
        _standing = [.. _loaded];

        Unloading?.Invoke(this, pluginId);
        loaded.Unload();
        Changed?.Invoke(this, EventArgs.Empty);

        return new WeakReference(loaded.Context);
    }

    /// <summary>
    /// Разносит отказы по обязательным рёбрам среди поднимаемых.
    /// </summary>
    /// <param name="refusals">Кому уже отказано и почему.</param>
    /// <param name="raising">Кого собираются поднять.</param>
    /// <returns>Отказы вместе с унаследованными; те же слова, что при старте.</returns>
    /// <remarks>
    /// Дорога перезагрузки обязана держать то же обещание, что и дорога
    /// старта: раз плагин поднят, его обязательная зависимость под ним. Без
    /// этого зависимый возвращался бы после перезагрузки без соседа — не
    /// падая, потому что службы отвечают правду, но и не работая, а человек
    /// видел бы в журнале один отказ вместо цепочки причин.
    /// <para>
    /// Граф зовётся только когда есть с чего начать: без отказов
    /// перезагрузка не должна платить за разрешение зависимостей. Целями
    /// служат и поднятые, и ждущие своего события — ждущий установлен и
    /// включён, просто ещё не понадобился.
    /// </para>
    /// </remarks>
    private IReadOnlyDictionary<string, string> Spread(
        Dictionary<string, string> refusals, IReadOnlyList<InstalledPlugin> raising)
    {
        if (refusals.Count == 0)
            return refusals;

        var present = _loaded
            .Where(loaded => loaded.IsLoaded)
            .Select(loaded => loaded.Installed)
            .Concat(_deferred)
            .ToList();

        return PluginGraph.Resolve(raising, present, refusals).Refused;
    }

    /// <summary>
    /// Снимает поднятый плагин с учёта и выгружает его.
    /// </summary>
    /// <param name="pluginId">Кого снять.</param>
    /// <returns><c>false</c> — такого поднятого нет.</returns>
    /// <remarks>
    /// Дорога для отключения упавшего: раньше оболочка звала
    /// <c>LoadedPlugin.Unload</c> напрямую, мимо хоста, и потому мимо уборки
    /// реестров — команды снятого плагина оставались заявленными и звали код
    /// выгруженного контекста, а запись висела в списке поднятых как живая.
    /// </remarks>
    public bool Drop(string pluginId)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);

        if (_loaded.All(plugin => !Same(plugin.Installed.Id, pluginId)))
            return false;

        // О перемене состава говорит сам Retire — второй раз отсюда было бы два события на одно.
        Retire(pluginId);
        return true;
    }

    private LoadedPlugin Add(InstalledPlugin installed)
    {
        // Поднятый больше не ждёт. Включённый в настройках плагин поднимается сразу, не дожидаясь
        // своего события, и запись, оставшаяся в ожидании, подняла бы первым же событием вторую
        // копию: два обработчика на каждую команду и две панели на одно имя в раскладке.
        _deferred.RemoveAll(waiting => Same(waiting.Id, installed.Id));

        var loaded = Load(installed);

        Keep(loaded);
        Changed?.Invoke(this, EventArgs.Empty);
        return loaded;
    }

    /// <summary>Опускает все поднятые плагины.</summary>
    public void Dispose()
    {
        foreach (var plugin in _loaded)
        {
            Unloading?.Invoke(this, plugin.Installed.Id);
            plugin.Unload();
        }

        _loaded.Clear();
        _standing = [];
        _deferred.Clear();
        _resolution = null;
        Changed?.Invoke(this, EventArgs.Empty);
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

        if (PluginPaths.Inside(installed.Directory, entry) is not { } assemblyPath)
            return LoadedPlugin.Failed(installed, $"Сборка плагина уводит за пределы его каталога: {entry}");

        if (!File.Exists(assemblyPath))
            return LoadedPlugin.Failed(installed, $"Сборка плагина не найдена: {entry}");

        PluginLoadContext? context = null;

        // Шов загрузки: фильтр тот же и по той же причине, что у Raise, — там она и записана.
        // Контекст заводится уже внутри него. Его конструктор читает .deps.json плагина, и файл,
        // обрезанный пересборкой, бросает прямо оттуда; пока конструктор стоял снаружи, исключение
        // уходило из хоста на дорогах пробуждения и перезагрузки, где своего catch нет: плагин
        // пропадал и из ждущих, и из поднятых, а каскад обрывался, никого не подняв.
        try
        {
            assemblyPath = Shadow(installed, assemblyPath);
            context = new PluginLoadContext(installed.Id, assemblyPath);

            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

            return Raise(installed, context, [assembly], _contexts.Create(installed));
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            context?.Release();
            return LoadedPlugin.Failed(installed, Describe(e));
        }
    }

    /// <summary>
    /// Поднимает встроенный модуль: те же точки входа и тот же контракт, но в
    /// основном контексте загрузки и без выгрузки.
    /// </summary>
    /// <param name="assembly">Сборка модуля; его манифест лежит в папке над её <c>bin</c>.</param>
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
            ModuleManifest.FolderOf(assembly),
            manifest,
            error,
            IsEnabled: true,
            IsBuiltIn: true);

        // Контракты занимаются до подъёма — тем же шагом, что у плагина: модуль
        // отдаёт соседям типы точно так же, и объявленный, но не найденный
        // контракт значил бы модуль, на чьи типы соседи рассчитывают зря.
        // Ищется контракт внутри папки модуля и объявлен так же, как у плагина, —
        // bin/… от её корня. Заметки о переменах на диске модулю ни к чему: его
        // контракт приехал со студией, а не лежит в папке, которую пересобирают
        // при открытой студии.
        //
        // Ошибка берётся без подстраховки: ModuleManifest.Load обещает, что пустой
        // манифест приходит со словом о том, почему он пуст.
        var loaded = manifest is null
            ? LoadedPlugin.Failed(installed, error!)
            : PluginContracts.EnsureLoaded(installed, []) is { } refusal
                ? LoadedPlugin.Failed(installed, refusal)
                : Raise(installed, context: null, [assembly], _contexts.Create(installed));

        Keep(loaded);
        Changed?.Invoke(this, EventArgs.Empty);
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
            // Плагин, положенный в папку руками, через проверку установки не проходил, и его
            // идентификатор в имя папки идёт только годным: иначе копия уехала бы из ShadowRoot.
            var label = PluginPaths.IsFolderName(installed.Id) ? installed.Id : "plugin";
            var shadow = Path.Combine(ShadowRoot, $"{label}-{Guid.NewGuid():N}");

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
    /// одном экземпляре. Класс при этом может быть и статическим — это самый
    /// естественный дом для таких методов. Для среды исполнения статический
    /// класс — <c>abstract sealed</c>, и отбор по одному <c>IsAbstract</c>
    /// молча оставлял его команды незаявленными.
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
            // Абстрактный класс отсеивается, статический — нет: наследника у статического не
            // бывает, и его методы зовутся как есть. Открытый обобщённый тип звать не у кого.
            if (type is { IsPublic: true, ContainsGenericParameters: false } && (!type.IsAbstract || type.IsSealed))
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

    private LoadedPlugin Raise(
        InstalledPlugin installed,
        PluginLoadContext? context,
        IReadOnlyList<Assembly> assemblies,
        IStudioContext studio)
    {
        // Кого уже позвали. Ведётся снаружи try: упади подъём на середине, с этими надо
        // попрощаться, а кроме этого списка о них не знает никто.
        var activated = new List<StudioPlugin>();
        var started = new List<StudioService>();

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

            // Разметка расширения не знает ни плагина, ни его контекста: она
            // видит только свою сборку. Связь «сборка → словарь» кладётся
            // здесь, до первой построенной панели, и одинаково для плагина и
            // для встроенного модуля — путь подъёма у них один.
            foreach (var assembly in assemblies)
                StudioStringsRegistry.Remember(assembly, studio.Strings);

            // В список — до вызова: упавший на середине Activate мог успеть подписаться, и
            // прощание нужно ему не меньше, чем тем, кто поднялся целиком.
            foreach (var plugin in entries)
            {
                activated.Add(plugin);
                plugin.Activate(studio);
            }

            foreach (var service in services)
            {
                started.Add(service);
                service.Start(studio);
            }

            Bind(assemblies, entries.Cast<object>().Concat(services), studio);

            return new LoadedPlugin(installed, context, assemblies, studio, entries, services, null);
        }
        // Фильтр широкий нарочно, и это не небрежность. Подъём — это чужой
        // код: загрузка чужой сборки, чужой Activate, чужой Start. Зовётся он
        // здесь напрямую, а PluginGuard, через который идут остальные вызовы
        // плагина, на загрузке ни при чём: считать падения ещё не поднятого
        // плагина некому и незачем. Значит этот catch и есть шов загрузки.
        //
        // Список типов был перечислением известных бед: сборки нет, типа нет,
        // версия SDK чужая. Всё это правда, но беды нельзя перечислить —
        // NullReferenceException из чужого Activate уносил студию целиком
        // просто потому, что его забыли назвать.
        //
        // Не ловятся две. Нехватку памяти нельзя пережить осмысленно,
        // переполнение стека нельзя поймать вовсе.
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            // Поднятая половина останавливается тем же порядком, что у ушедшего: службы, потом
            // точки входа. Без этого подписка или таймер из успевшего Activate держали контекст
            // загрузки, а «несостоявшийся» плагин продолжал получать события студии — у
            // встроенного модуля до конца сеанса.
            foreach (var service in Enumerable.Reverse(started))
                Quietly(service.Stop);

            foreach (var plugin in Enumerable.Reverse(activated))
                Quietly(plugin.Deactivate);

            // Плагин мог успеть опубликоваться в Activate и упасть уже на
            // запуске службы. Его записи снимаются так же, как у ушедшего:
            // иначе сосед получил бы объект из контекста, который студия
            // только что объявила мёртвым.
            Unloading?.Invoke(this, installed.Id);
            context?.Release();
            return LoadedPlugin.Failed(installed, Describe(e));
        }
    }

    /// <summary>
    /// Зовёт прощальный код плагина, не выпуская его исключение.
    /// </summary>
    /// <remarks>
    /// Плагин, упавший на прощание, уже никому не мешает: студия его отпускает, и держаться за
    /// исключение незачем. Остановить из-за него прощание остальных было бы хуже.
    /// </remarks>
    internal static void Quietly(Action farewell)
    {
        try
        {
            farewell();
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
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
/// <param name="Notes">
/// О чём сказать, не отказывая: изменившийся контракт, который выгрузить
/// нечем. Ради этой строки перезагрузка контракт и перечитывает — потерять
/// её значит оставить автора править типы, которых процесс уже не увидит.
/// </param>
public sealed record PluginReload(
    LoadedPlugin? Plugin, string? Error, bool Released, IReadOnlyList<string> Notes);

/// <summary>
/// Итог каскадной перезагрузки.
/// </summary>
/// <param name="Released">По каждому опущенному: выгрузился ли его контекст.</param>
/// <param name="Raised">Поднятые в порядке подъёма, включая записи с ошибкой.</param>
/// <param name="Skipped">Кого не тронули и почему: не поднят, встроенный.</param>
/// <param name="Notes">О чём сказать, не отказывая: изменившийся контракт.</param>
/// <param name="Lingering">
/// Невыгрузившиеся контексты — слабыми ссылками: застрявшего на миг можно спросить ещё раз, не
/// удержав его этим вопросом.
/// </param>
/// <param name="Restart">
/// Кому новый контракт встанет только после перезапуска студии, и почему: общий контекст контрактов
/// не выгружается.
/// </param>
public sealed record PluginCascade(
    IReadOnlyDictionary<string, bool> Released,
    IReadOnlyList<LoadedPlugin> Raised,
    IReadOnlyDictionary<string, string> Skipped,
    IReadOnlyList<string> Notes,
    IReadOnlyDictionary<string, WeakReference> Lingering,
    IReadOnlyDictionary<string, string> Restart);

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
            PluginHost.Quietly(service.Stop);

        foreach (var plugin in Entries)
            PluginHost.Quietly(plugin.Deactivate);

        (Context as PluginLoadContext)?.Release();
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

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Контракт один на всех: даже если копия с тем же именем лежит в
        // bin/ плагина — автор забыл исключить, — тип обязан остаться общим.
        // Иначе вернулась бы двойная идентичность, от которой контракты и
        // заведены.
        if (PluginContracts.Find(assemblyName) is { } contract)
            return contract;

        return _resolver.ResolveAssemblyToPath(assemblyName) is { } path &&
               assemblyName.Name is { } name && !IsShared(name)
            ? LoadFromAssemblyPath(path)
            : null;
    }

    /// <summary>
    /// Сборка, которая обязана быть одной на всех и приходит из общего контекста.
    /// </summary>
    /// <remarks>
    /// Спрашивается не только резолвером: под этими именами нельзя объявить и
    /// контракт — иначе файл плагина подменил бы общую сборку и студии, и всем
    /// соседям.
    /// <para>
    /// Семейство узнаётся по имени целиком или по имени с точкой, а не по первым буквам: под
    /// «Avalonia» без точки попадали и чужие библиотеки — <c>AvaloniaEdit</c>, <c>AvaloniaHex</c>, —
    /// которые в студии не лежат. Плагину с такой зависимостью отказывали в его же файле, основной
    /// контекст её не находил, и плагин падал на первом обращении к редактору.
    /// </para>
    /// </remarks>
    internal static bool IsShared(string name) =>
        Family(name, "Avalonia") ||
        Family(name, "ArxisStudio.Sdk") ||
        Family(name, "ArxisStudio.Controls") ||
        Family(name, "ArxisStudio.Icons") ||
        // Модель проектов — точным именем, а не семейством: семейство отдало бы
        // плагинам и её движки — MSBuild, NuGet, адаптер разметки, — а их держит
        // служба проектов, и второй экземпляр движка в процессе был бы бедой.
        name.Equals("ArxisStudio.ProjectSystem", StringComparison.Ordinal);

    /// <summary>Само имя или имя из его семейства: <c>Avalonia</c>, <c>Avalonia.Base</c>, но не <c>AvaloniaEdit</c>.</summary>
    private static bool Family(string name, string root) =>
        name.Equals(root, StringComparison.Ordinal) ||
        name.StartsWith(root + ".", StringComparison.Ordinal);

    /// <summary>
    /// Выгружает контекст, отпустив прежде то, что держит его снаружи.
    /// </summary>
    /// <remarks>
    /// Дорог выгрузки три — прощание поднятого плагина, сбой загрузки сборки
    /// и сбой активации, — и уборка стоит здесь, на общем шве, а не у каждой
    /// из них. Забытая на одной дороге, она означала бы плагин, который
    /// выгружается при перезагрузке и остаётся в памяти, упав на подъёме.
    /// <para>
    /// Выгрузка идёт в <c>finally</c>: уборка перед ней — дело полезное, но не
    /// обязательное, и сорвись она непредвиденным образом, контекст не должен
    /// остаться неотпущенным. Это было бы хуже той беды, ради которой уборку и
    /// завели.
    /// </para>
    /// </remarks>
    public void Release()
    {
        try
        {
            Forget();
            Unregister();
        }
        finally
        {
            Unload();
        }
    }

    /// <summary>
    /// Снимает с реестра свойств Avalonia всё, что свойства запомнили о типах плагина.
    /// </summary>
    /// <remarks>
    /// Свойство Avalonia кэширует свои метаданные для каждого типа, у которого их спросили, —
    /// словарём с сильным ключом-типом, а спрашивает оформление. Тип-контрол плагина, побывавший на
    /// экране, оставался ключом у свойств, которые ему назначила тема, — в замере это
    /// <c>Border.Background</c>, <c>Visual.ClipToBounds</c>, <c>Visual.IsVisible</c> и
    /// <c>TemplatedControl.Template</c>, — и контекст загрузки не собирался никогда. Так терял
    /// перезагрузку на ходу всякий плагин со своим классом-контролом, а значит и всякий с разметкой
    /// <c>x:Class</c>.
    /// <para>
    /// Средство у Avalonia открытое: <c>UnregisterByModule</c> забывает по списку типов их
    /// переопределения метаданных и кэши. Типы спрашиваются у всех сборок контекста: приватная
    /// зависимость плагина тоже может завести свой контрол.
    /// </para>
    /// <para>
    /// Свойств и событий, заведённых самим типом, он не снимает — ни в 12.1.1, ни в основной ветке
    /// Avalonia, — а у реестра событий снятия нет вовсе. Такой плагин остаётся в памяти до
    /// перезапуска, и узнать это до спуска можно только спросив: <see cref="Pinned"/>. По ответу
    /// студия решает, ждать ли застрявшую копию или сразу ставить плагин в ждущие перезапуска.
    /// </para>
    /// </remarks>
    private void Unregister() => AvaloniaPropertyRegistry.Instance.UnregisterByModule(Types());

    /// <summary>
    /// Кто из типов контекста держится реестрами Avalonia; null — никто из видимых.
    /// </summary>
    /// <returns>Причина словами: чья сборка завела свои свойства и события и у каких типов.</returns>
    /// <remarks>
    /// Avalonia помнит свойство и маршрутизируемое событие статическим реестром до конца процесса, а
    /// ключом там стоит тип-владелец. Свой <c>StyledProperty</c> у контрола плагина — обычное дело, и
    /// AvaloniaEdit у просмотрщика заводит их десятки, а с ними события и присоединённое свойство.
    /// Причина называет сборку: у просмотрщика держит не его код, а его библиотека, — и это автору
    /// нужнее списка типов.
    /// <para>
    /// Свойства спрашиваются закрытым словарём реестра <c>_registered</c>, только на чтение: под
    /// типом-владельцем Avalonia кладёт и обычное свойство, и прямое, и присоединённое, и принятое
    /// <c>AddOwner</c>. Открытый <c>GetRegistered</c> прогоняет статические конструкторы всей цепочки
    /// типов и сам завёл бы свойства, которых плагин ещё не трогал: проверка приковала бы к памяти тот
    /// плагин, о котором спрашивает. Словарь пропал при обновлении Avalonia — ответ беднеет, а не врёт,
    /// и падает тест. События спрашиваются открытым <c>GetAllRegistered</c>: он конструкторов не зовёт.
    /// </para>
    /// <para>
    /// Классовый обработчик на чужом событии так не виден вовсе: его подписка лежит внутри события
    /// Avalonia, и хозяина у неё нет. Его выдаёт только проверка выгрузки после спуска.
    /// </para>
    /// </remarks>
    internal string? Pinned()
    {
        var mine = Assemblies.ToHashSet();
        var owners = new List<Type>();

        foreach (var routed in Avalonia.Interactivity.RoutedEventRegistry.Instance.GetAllRegistered())
        {
            if (mine.Contains(routed.OwnerType.Assembly))
                owners.Add(routed.OwnerType);
        }

        try
        {
            if (typeof(AvaloniaPropertyRegistry)
                    .GetField("_registered", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(AvaloniaPropertyRegistry.Instance) is System.Collections.IDictionary registered)
            {
                owners.AddRange(registered.Keys.OfType<Type>().Where(key => mine.Contains(key.Assembly)));
            }
        }
        catch (InvalidOperationException)
        {
            // Словарь переписали посреди обхода: свойство завели из фонового потока. Сказано то, что
            // успели увидеть, — проверка выгрузки после спуска всё равно скажет своё.
        }

        if (owners.Count == 0)
            return null;

        var assemblies = owners.Select(owner => owner.Assembly.GetName().Name).Distinct().Order(StringComparer.Ordinal).ToList();
        var names = owners.Select(owner => owner.Name).Distinct().Order(StringComparer.Ordinal).ToList();
        var shown = names.Count > 3 ? $"{string.Join(", ", names.Take(3))} и ещё {names.Count - 3}" : string.Join(", ", names);

        return $"{string.Join(", ", assemblies)} {(assemblies.Count > 1 ? "заводят" : "заводит")} свои свойства и события " +
               $"Avalonia — {shown}, — а снимать их Avalonia не умеет";
    }

    /// <summary>Все типы сборок контекста, включая те, что загрузились не все.</summary>
    private List<Type> Types()
    {
        var types = new List<Type>();

        foreach (var assembly in Assemblies)
        {
            try
            {
                types.AddRange(assembly.GetTypes());
            }
            catch (ReflectionTypeLoadException e)
            {
                // Типы, которые не загрузились, ни у кого метаданных не спрашивали.
                types.AddRange(e.Types.OfType<Type>());
            }
        }

        return types;
    }

    /// <summary>
    /// Убирает сборки плагина из кэша загрузчика ресурсов Avalonia.
    /// </summary>
    /// <remarks>
    /// Кэш держит сборку сильной ссылкой и по <b>простому</b> имени, а попасть
    /// в него хватает одного вопроса про <c>avares://</c>-адрес с этим именем:
    /// в замере даже <c>Exists</c>, ответивший «такого ресурса нет», оставлял
    /// сборку в кэше — и контекст плагина не собирался никогда.
    /// <para>
    /// Беда при этом сама себя поддерживает: живая прежняя копия находится по
    /// простому имени первой, и следующий подъём того же плагина получал бы
    /// ресурсы предыдущего. Порядок «сперва забыть, потом выгрузить» её и
    /// разрывает.
    /// </para>
    /// <para>
    /// Спрашиваются сборки контекста, а не одна entry: приватная зависимость
    /// плагина, подгруженная по требованию, везёт свои ресурсы и попадает в
    /// тот же кэш под своим именем.
    /// </para>
    /// <para>
    /// Кэша может не быть вовсе — студию собирают и без платформы Avalonia, и
    /// так же живёт половина тестов расширений. Спросить об этом заранее
    /// нечем: <c>AvaloniaLocator</c> из открытой поверхности убран, и
    /// единственный ответ службы — исключение. Ловится оно здесь: платформы
    /// нет, значит и кэш пуст, и выгрузке это не помеха.
    /// </para>
    /// </remarks>
    private void Forget()
    {
        try
        {
            foreach (var assembly in Assemblies)
            {
                if (assembly.GetName().Name is { Length: > 0 } simple)
                    AssetLoader.InvalidateAssemblyCache(simple);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
