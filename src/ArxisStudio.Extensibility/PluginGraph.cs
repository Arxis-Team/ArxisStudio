using System.Globalization;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Граф зависимостей между плагинами: кто без кого не поднимается и в каком
/// порядке подниматься.
/// </summary>
/// <remarks>
/// Весь разбор идёт по манифестам, до загрузки единой сборки, — это то же
/// правило, по которому строятся меню и панели: список установленного не
/// должен означать загрузку всего установленного. Плагин с невыполненной
/// зависимостью не поднимается вовсе и говорит почему — вместо падения на
/// первом обращении к соседу.
/// <para>
/// Порядок подъёма детерминированный и не зависит ни от порядка на входе, ни
/// от языка интерфейса: внутри уровня — встроенные модули первыми, затем по
/// идентификатору. Сортировать по отображаемому имени нельзя — оно
/// переводится, и порядок подъёма менялся бы вместе с языком.
/// </para>
/// </remarks>
public static class PluginGraph
{
    /// <summary>
    /// Годится ли установленная версия под просимую нижнюю границу.
    /// </summary>
    /// <param name="version">Версия установленного соседа.</param>
    /// <param name="min">Нижняя граница из зависимости; пусто — любая.</param>
    /// <remarks>
    /// Правило одно на студию — то же, что у <see cref="Sdk.StudioSdk.Satisfies"/>: сравниваются
    /// старший и младший номера, неразобранная граница считается выполненной — манифест пишет
    /// человек, и отказать рабочей паре плагинов из-за опечатки в номере значило бы наказать
    /// несоразмерно поводу. Прежде у графа был свой разбор, и на «1.x» они расходились.
    /// </remarks>
    public static bool Satisfies(string? version, string? min) => Sdk.StudioSdk.Reaches(version, min);

    /// <summary>
    /// Разрешает граф: кому отказано и в каком порядке подниматься остальным.
    /// </summary>
    /// <param name="candidates">Включённые и валидные плагины каталога.</param>
    /// <param name="present">Уже поднятые — встроенные модули.</param>
    /// <param name="refusedUpfront">
    /// Отказы, о которых граф сам знать не может: не загрузившийся контракт.
    /// Расходятся по обязательным рёбрам наравне с остальными.
    /// </param>
    /// <param name="notesUpfront">Заметки, с которых начинается список: канал один на всех.</param>
    /// <remarks>
    /// Узлы порядка — только плагины с entry-сборкой: языковой пакет нечего
    /// поднимать, но целью зависимости он быть может — он «есть».
    /// </remarks>
    public static PluginResolution Resolve(
        IReadOnlyList<InstalledPlugin> candidates,
        IReadOnlyList<InstalledPlugin> present,
        IReadOnlyDictionary<string, string>? refusedUpfront = null,
        IEnumerable<string>? notesUpfront = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(present);

        var all = Universe(candidates, present);
        var raised = new HashSet<string>(present.Select(plugin => plugin.Id), PluginIds.Comparer);
        var notes = notesUpfront is null ? new List<string>() : new List<string>(notesUpfront);
        var refused = Refuse(all, notes, refusedUpfront);

        // Отказ по циклу заразителен так же, как всякий другой, а находится он позже остальных:
        // обход идёт мимо уже отказанных. Поэтому после него отказы разносятся ещё раз — иначе
        // плагин, которому нужен участник цикла, поднимался бы без него и без слова о причине.
        if (Cycles(all, refused))
            refused = Refuse(all, notes, refused);

        var order = Order(all, raised, refused, notes);

        return new PluginResolution(order, refused, notes);
    }

    /// <summary>
    /// Кто из перечисленных зависит от плагина — прямо или через других.
    /// </summary>
    /// <param name="pluginId">От кого зависят.</param>
    /// <param name="among">Среди кого искать.</param>
    /// <param name="includeOptional">Считать ли необязательные зависимости.</param>
    /// <remarks>
    /// Для каскадной перезагрузки зависимые считаются вместе с необязательными:
    /// их гарантия «сосед стоит подо мной» иначе стала бы ложью. Для
    /// предупреждения при выключении — только обязательные: выключение соседа
    /// необязательную связь не ломает, и пугать человека этими именами значило
    /// бы врать.
    /// </remarks>
    public static IReadOnlyList<InstalledPlugin> Dependents(
        string pluginId,
        IReadOnlyList<InstalledPlugin> among,
        bool includeOptional)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);
        ArgumentNullException.ThrowIfNull(among);

        var found = new Dictionary<string, InstalledPlugin>(PluginIds.Comparer);
        var wave = new HashSet<string>(PluginIds.Comparer) { pluginId };

        // Волнами до неподвижной точки: зависимые от зависимых — тоже зависимые.
        while (wave.Count > 0)
        {
            var next = new HashSet<string>(PluginIds.Comparer);

            foreach (var plugin in among)
            {
                if (found.ContainsKey(plugin.Id))
                    continue;

                var depends = (plugin.Manifest?.Dependencies ?? [])
                    .Where(dependency => includeOptional || !dependency.Optional)
                    .Any(dependency => dependency.Id is { Length: > 0 } id && wave.Contains(id));

                if (!depends)
                    continue;

                found[plugin.Id] = plugin;
                next.Add(plugin.Id);
            }

            wave = next;
        }

        return [.. found.Values];
    }

    /// <summary>
    /// Замыкание по зависимостям: сами корни и всё, что они тянут, — среди известных.
    /// </summary>
    /// <param name="roots">С кого начинать.</param>
    /// <param name="known">Среди кого искать, по идентификатору; незнакомых замыкание обходит.</param>
    /// <returns>Идентификаторы в замыкании.</returns>
    /// <remarks>
    /// Тянутся и необязательные присутствующие зависимости: обещание «сосед стоит подо мной» не
    /// делится на обязательных и нет. Так считает хост дважды — нетерпеливых при старте, вместе с
    /// отложенными зависимостями, и цепочку ждущих при пробуждении, — и считал он это двумя
    /// копиями одного обхода.
    /// </remarks>
    internal static HashSet<string> Closure(IEnumerable<string> roots, IReadOnlyDictionary<string, InstalledPlugin> known)
    {
        var closure = new HashSet<string>(PluginIds.Comparer);

        foreach (var root in roots)
            Pull(root);

        return closure;

        void Pull(string id)
        {
            if (!known.TryGetValue(id, out var plugin) || !closure.Add(id))
                return;

            foreach (var declared in plugin.Manifest?.Dependencies ?? [])
            {
                if (declared.Id is { Length: > 0 } target)
                    Pull(target);
            }
        }
    }

    /// <summary>
    /// Состояние каждой зависимости плагина — то, что показывает карточка.
    /// </summary>
    /// <param name="plugin">Чьи зависимости.</param>
    /// <param name="all">Все, среди кого искать цели: каталог плюс модули.</param>
    public static IReadOnlyList<PluginDependencyState> Describe(
        InstalledPlugin plugin,
        IReadOnlyList<InstalledPlugin> all)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(all);

        var states = new List<PluginDependencyState>();

        foreach (var declared in plugin.Manifest?.Dependencies ?? [])
        {
            var target = all.FirstOrDefault(candidate =>
                PluginIds.Same(candidate.Id, declared.Id));

            var health =
                target is null ? PluginDependencyHealth.Missing
                : !target.IsEnabled ? PluginDependencyHealth.Disabled
                : !Satisfies(target.Manifest?.Version, declared.Min) ? PluginDependencyHealth.Stale
                : PluginDependencyHealth.Present;

            states.Add(new PluginDependencyState(declared, target, health));
        }

        return states;
    }

    /// <summary>Все известные плагины по идентификатору: каталог поверх поднятых.</summary>
    private static Dictionary<string, InstalledPlugin> Universe(
        IReadOnlyList<InstalledPlugin> candidates,
        IReadOnlyList<InstalledPlugin> present)
    {
        var all = new Dictionary<string, InstalledPlugin>(PluginIds.Comparer);

        foreach (var plugin in present)
            all[plugin.Id] = plugin;

        foreach (var plugin in candidates)
            all[plugin.Id] = plugin;

        return all;
    }

    /// <summary>
    /// Находит всех, кому отказано, — до неподвижной точки.
    /// </summary>
    /// <remarks>
    /// Отказ заразителен по обязательным рёбрам: B, которому нужен отказанный
    /// A, тоже не поднимается — и несёт в своей причине всю цепочку, а не
    /// последнее звено. Человеку нужна первопричина.
    /// </remarks>
    private static Dictionary<string, string> Refuse(
        Dictionary<string, InstalledPlugin> all,
        List<string> notes,
        IReadOnlyDictionary<string, string>? seed)
    {
        // Затравка — отказы, о которых граф сам знать не может: контракт,
        // объявленный и не загрузившийся. Дальше они расходятся по
        // обязательным рёбрам наравне с остальными: зависимый, чьи типы не
        // приехали, обязан узнать причину словами, а не упасть на первом
        // приведении.
        var refused = seed is null
            ? new Dictionary<string, string>(PluginIds.Comparer)
            : new Dictionary<string, string>(seed, PluginIds.Comparer);

        var changed = true;

        while (changed)
        {
            changed = false;

            foreach (var plugin in all.Values)
            {
                if (refused.ContainsKey(plugin.Id))
                    continue;

                foreach (var declared in plugin.Manifest?.Dependencies ?? [])
                {
                    if (declared.Id is not { Length: > 0 } id)
                        continue;

                    var reason = Complain(plugin, declared, all, refused, notes);

                    if (reason is null)
                        continue;

                    if (declared.Optional)
                        continue;

                    refused[plugin.Id] = reason;
                    changed = true;
                    break;
                }
            }
        }

        return refused;
    }

    /// <summary>Чем плоха одна зависимость; null — всё в порядке.</summary>
    private static string? Complain(
        InstalledPlugin plugin,
        PluginDependency declared,
        Dictionary<string, InstalledPlugin> all,
        Dictionary<string, string> refused,
        List<string> notes)
    {
        // Своего имени жалоба не называет: его подставляет тот, кто её
        // показывает, — иначе в журнале выходит «Плагин: Плагин: нужен…».
        // Заметке имя нужно, потому что она едет отдельной строкой, сама по
        // себе, и там оно добавляется явно.
        if (!all.TryGetValue(declared.Id, out var target))
            return $"нужен {declared.Id}, а он не установлен";

        if (!target.IsEnabled)
            return $"нужен {target.DisplayName}, а он выключен";

        if (!target.IsValid)
            return $"нужен {target.DisplayName}, а его манифест не разобрался";

        if (!Satisfies(target.Manifest?.Version, declared.Min))
        {
            var complaint = $"нужен {target.DisplayName} {declared.Min}, " +
                            $"установлен {target.Manifest?.Version}";

            // Устаревший необязательный сосед — не отказ, но молчать о нём
            // нельзя: человек будет гадать, почему связка не работает. Сказать надо один раз:
            // отказы разносятся кругами до неподвижной точки, и жалоба звучит на каждом.
            var note = $"{plugin.DisplayName}: {complaint} — сосед считается отсутствующим";

            if (declared.Optional && !notes.Contains(note))
                notes.Add(note);

            return complaint;
        }

        if (refused.TryGetValue(target.Id, out var inherited))
            return $"нужен {target.DisplayName}, а тот не поднят ({inherited})";

        return null;
    }

    /// <summary>
    /// Находит циклы по обязательным рёбрам и отказывает всем участникам.
    /// </summary>
    /// <remarks>
    /// Поиск идёт обходом в глубину ради самого пути: сообщение «цикл
    /// зависимостей: a → b → a» указывает, что резать, а «у вас цикл» — нет.
    /// Цикл, замыкающийся только необязательным ребром, не фатален: optional
    /// по определению «не мешает», такое ребро просто выпадает из порядка.
    /// </remarks>
    /// <returns><c>true</c>, если нашёлся хоть один цикл и отказов прибавилось.</returns>
    private static bool Cycles(
        Dictionary<string, InstalledPlugin> all,
        Dictionary<string, string> refused)
    {
        var visiting = new HashSet<string>(PluginIds.Comparer);
        var done = new HashSet<string>(PluginIds.Comparer);
        var path = new List<string>();
        var before = refused.Count;

        foreach (var plugin in all.Values)
            Visit(plugin.Id);

        return refused.Count > before;

        void Visit(string id)
        {
            if (done.Contains(id) || refused.ContainsKey(id) || !all.TryGetValue(id, out var plugin))
                return;

            if (!visiting.Add(id))
            {
                var start = path.IndexOf(id);
                var cycle = string.Join(" → ", path.Skip(start).Append(id));

                foreach (var participant in path.Skip(start))
                    refused[participant] = $"цикл зависимостей: {cycle}";

                return;
            }

            path.Add(id);

            foreach (var declared in plugin.Manifest?.Dependencies ?? [])
            {
                if (declared is { Optional: false, Id.Length: > 0 })
                    Visit(declared.Id);
            }

            path.RemoveAt(path.Count - 1);
            visiting.Remove(id);
            done.Add(id);
        }
    }

    /// <summary>
    /// Топологический порядок подъёма годных плагинов с entry-сборкой.
    /// </summary>
    private static List<InstalledPlugin> Order(
        Dictionary<string, InstalledPlugin> all,
        HashSet<string> raised,
        Dictionary<string, string> refused,
        List<string> notes)
    {
        // Узлы — только то, что поднимается: плагин с entry, не отказанный и
        // ещё не поднятый. Всё остальное — цели: они «есть» без подъёма.
        var nodes = all.Values
            .Where(plugin => plugin.Manifest?.Entry is { Length: > 0 } &&
                             !refused.ContainsKey(plugin.Id) &&
                             !raised.Contains(plugin.Id))
            .ToDictionary(plugin => plugin.Id, PluginIds.Comparer);

        var waiting = new Dictionary<string, int>(PluginIds.Comparer);
        var dependents = new Dictionary<string, List<string>>(PluginIds.Comparer);

        foreach (var node in nodes.Values)
        {
            waiting.TryAdd(node.Id, 0);

            foreach (var declared in node.Manifest!.Dependencies)
            {
                // Ребро порядка — на соседа-узла; обязательное или
                // необязательное, но присутствующее. Отказанная цель сюда не
                // попадает: у обязательного ребра отказ уже заразил узел, а
                // необязательное на отказанного просто не ждёт.
                if (declared.Id is not { Length: > 0 } id || !nodes.ContainsKey(id))
                    continue;

                if (declared.Optional && !Satisfies(all[id].Manifest?.Version, declared.Min))
                    continue;

                if (!dependents.TryGetValue(id, out var list))
                    dependents[id] = list = [];

                list.Add(node.Id);
                waiting[node.Id]++;
            }
        }

        var order = new List<InstalledPlugin>(nodes.Count);
        var ready = Ready(nodes, waiting.Where(pair => pair.Value == 0).Select(pair => pair.Key));

        while (ready.Count > 0)
        {
            var id = ready[0];

            ready.RemoveAt(0);
            order.Add(nodes[id]);

            foreach (var dependent in dependents.TryGetValue(id, out var list) ? list : [])
            {
                if (--waiting[dependent] == 0)
                    ready = Ready(nodes, ready.Append(dependent));
            }
        }

        // Сюда попадают только участники циклов, замкнутых необязательным
        // ребром: обязательные циклы отказаны раньше. Такие поднимаются в
        // конце, а о разорванном ребре сказано — молча менять обещанный
        // порядок нельзя.
        foreach (var leftover in nodes.Values.Where(node => !order.Contains(node)).OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            notes.Add($"{leftover.DisplayName}: необязательная зависимость замыкает цикл — порядок с ней не гарантируется");
            order.Add(leftover);
        }

        return order;
    }

    /// <summary>
    /// Готовый уровень в предсказуемом порядке: модули первыми, затем по
    /// идентификатору — и никогда по отображаемому имени, которое переводится.
    /// </summary>
    private static List<string> Ready(
        Dictionary<string, InstalledPlugin> nodes,
        IEnumerable<string> ids) =>
        ids.OrderByDescending(id => nodes[id].IsBuiltIn)
            .ThenBy(id => id, StringComparer.Ordinal)
            .ToList();
}

/// <summary>
/// Итог разрешения графа.
/// </summary>
/// <param name="Order">Порядок подъёма годных плагинов с entry-сборкой.</param>
/// <param name="Refused">Кому отказано: идентификатор — цепочка причин.</param>
/// <param name="Notes">О чём стоит сказать в журнал, не отказывая.</param>
public sealed record PluginResolution(
    IReadOnlyList<InstalledPlugin> Order,
    IReadOnlyDictionary<string, string> Refused,
    IReadOnlyList<string> Notes);

/// <summary>Состояние цели зависимости.</summary>
public enum PluginDependencyHealth
{
    /// <summary>Установлена, включена, версия годится.</summary>
    Present,

    /// <summary>Не установлена.</summary>
    Missing,

    /// <summary>Установлена, но выключена.</summary>
    Disabled,

    /// <summary>Установлена, но старее просимой версии.</summary>
    Stale,
}

/// <summary>
/// Одна зависимость плагина глазами менеджера.
/// </summary>
/// <param name="Declared">Что объявлено в манифесте.</param>
/// <param name="Target">Найденная цель или null.</param>
/// <param name="Health">Состояние цели.</param>
public sealed record PluginDependencyState(
    PluginDependency Declared,
    InstalledPlugin? Target,
    PluginDependencyHealth Health)
{
    /// <summary>Подпись для карточки: «Hello — установлен».</summary>
    public string Label
    {
        get
        {
            var name = Target?.DisplayName ?? Declared.Id;
            var state = Health switch
            {
                PluginDependencyHealth.Present => Localizer.Instance["plugins.dep.present"],
                PluginDependencyHealth.Missing => Localizer.Instance["plugins.dep.missing"],
                PluginDependencyHealth.Disabled => Localizer.Instance["plugins.dep.disabled"],
                _ => string.Format(
                    CultureInfo.CurrentCulture,
                    Localizer.Instance["plugins.dep.stale"],
                    Declared.Min,
                    Target?.Manifest?.Version),
            };

            return Declared.Optional
                ? $"{name} — {state} ({Localizer.Instance["plugins.dep.optional"]})"
                : $"{name} — {state}";
        }
    }

    /// <summary>Об этом стоит предупредить: обязательная зависимость не в порядке.</summary>
    public bool IsProblem => Health != PluginDependencyHealth.Present && !Declared.Optional;
}
