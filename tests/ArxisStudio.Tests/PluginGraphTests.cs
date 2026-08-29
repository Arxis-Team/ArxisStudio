using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Граф зависимостей: разрешение на манифестах, до загрузки сборок.
/// </summary>
/// <remarks>
/// Здесь нет ни одной настоящей сборки — только записи каталога: граф обязан
/// отвечать по манифестам, иначе список установленного означал бы загрузку
/// всего установленного.
/// </remarks>
public class PluginGraphTests
{
    /// <summary>
    /// Цикл отказывает всем участникам и называет сам путь.
    /// </summary>
    /// <remarks>
    /// «У вас цикл» не говорит, что резать; «a → b → a» — говорит.
    /// </remarks>
    [Fact]
    public void A_dependency_cycle_refuses_every_participant_and_names_the_cycle()
    {
        var resolution = PluginGraph.Resolve(
            [
                Plugin("arxis.a", depends: [Dep("arxis.b")]),
                Plugin("arxis.b", depends: [Dep("arxis.a")]),
            ],
            present: []);

        Assert.Empty(resolution.Order);
        Assert.Contains("arxis.a", resolution.Refused.Keys);
        Assert.Contains("arxis.b", resolution.Refused.Keys);
        Assert.Contains("→", resolution.Refused["arxis.a"]);
    }

    /// <summary>Отсутствующая обязательная зависимость — отказ с именем.</summary>
    [Fact]
    public void A_missing_mandatory_dependency_refuses_the_plugin_by_name()
    {
        var resolution = PluginGraph.Resolve(
            [Plugin("arxis.b", depends: [Dep("arxis.figma")])],
            present: []);

        Assert.Empty(resolution.Order);
        Assert.Contains("arxis.figma", resolution.Refused["arxis.b"]);
    }

    /// <summary>
    /// Устаревшая зависимость — отказ с обеими версиями.
    /// </summary>
    /// <remarks>
    /// «Нужен 2.0, установлен 1.0» отвечает и на «что не так», и на «что
    /// делать»; одна версия из двух не отвечает ни на что.
    /// </remarks>
    [Fact]
    public void An_outdated_dependency_is_refused_with_both_versions()
    {
        var resolution = PluginGraph.Resolve(
            [
                Plugin("arxis.a", version: "1.0.0"),
                Plugin("arxis.b", depends: [Dep("arxis.a", min: "2.0")]),
            ],
            present: []);

        Assert.Contains("2.0", resolution.Refused["arxis.b"]);
        Assert.Contains("1.0", resolution.Refused["arxis.b"]);
        Assert.Single(resolution.Order);
    }

    /// <summary>Выключенная обязательная зависимость — отказ.</summary>
    [Fact]
    public void A_disabled_mandatory_dependency_refuses_the_plugin()
    {
        var resolution = PluginGraph.Resolve(
            [
                Plugin("arxis.a") with { IsEnabled = false },
                Plugin("arxis.b", depends: [Dep("arxis.a")]),
            ],
            present: []);

        Assert.Contains("выключен", resolution.Refused["arxis.b"]);
    }

    /// <summary>
    /// Отказ едет вверх по цепочке вместе с причинами.
    /// </summary>
    /// <remarks>
    /// B не поднят, потому что не поднят A, потому что нет C: человеку нужна
    /// первопричина, а не последнее звено.
    /// </remarks>
    [Fact]
    public void A_refusal_travels_up_the_chain_with_its_reasons()
    {
        var resolution = PluginGraph.Resolve(
            [
                Plugin("arxis.a", depends: [Dep("arxis.c")]),
                Plugin("arxis.b", depends: [Dep("arxis.a")]),
            ],
            present: []);

        Assert.Contains("arxis.a", resolution.Refused["arxis.b"]);
        Assert.Contains("arxis.c", resolution.Refused["arxis.b"]);
    }

    /// <summary>Подъём идёт в порядке зависимостей.</summary>
    [Fact]
    public void Plugins_rise_in_dependency_order()
    {
        var resolution = PluginGraph.Resolve(
            [
                Plugin("arxis.c", depends: [Dep("arxis.b")]),
                Plugin("arxis.b", depends: [Dep("arxis.a")]),
                Plugin("arxis.a"),
            ],
            present: []);

        Assert.Empty(resolution.Refused);
        Assert.Equal(["arxis.a", "arxis.b", "arxis.c"], resolution.Order.Select(plugin => plugin.Id));
    }

    /// <summary>
    /// Внутри уровня — модули первыми, затем идентификаторы, и никогда имя.
    /// </summary>
    /// <remarks>
    /// Отображаемое имя переводится: сортируй мы по нему, порядок подъёма
    /// менялся бы вместе с языком интерфейса.
    /// </remarks>
    [Fact]
    public void Within_a_level_modules_go_first_then_ids_in_ordinal_order()
    {
        var resolution = PluginGraph.Resolve(
            [
                Plugin("zeta.plugin", name: "Аaa"),
                Plugin("alpha.plugin", name: "Яяя"),
                Plugin("omega.module", name: "Ббб") with { IsBuiltIn = true },
            ],
            present: []);

        Assert.Equal(
            ["omega.module", "alpha.plugin", "zeta.plugin"],
            resolution.Order.Select(plugin => plugin.Id));
    }

    /// <summary>Необязательный сосед, который есть, поднимается раньше.</summary>
    [Fact]
    public void An_optional_neighbour_that_is_present_is_ordered_first()
    {
        var resolution = PluginGraph.Resolve(
            [
                Plugin("arxis.a", depends: [Dep("arxis.git", optional: true)]),
                Plugin("arxis.git"),
            ],
            present: []);

        Assert.Equal(["arxis.git", "arxis.a"], resolution.Order.Select(plugin => plugin.Id));
    }

    /// <summary>Отсутствующий необязательный сосед не мешает.</summary>
    [Fact]
    public void A_missing_optional_neighbour_does_not_hold_the_plugin_back()
    {
        var resolution = PluginGraph.Resolve(
            [Plugin("arxis.a", depends: [Dep("arxis.git", optional: true)])],
            present: []);

        Assert.Empty(resolution.Refused);
        Assert.Single(resolution.Order);
    }

    /// <summary>
    /// Устаревший необязательный сосед — как отсутствующий, но со словом.
    /// </summary>
    /// <remarks>
    /// Отказывать не за что — сосед необязателен. Но молчать нельзя: человек
    /// будет гадать, почему связка не работает.
    /// </remarks>
    [Fact]
    public void An_outdated_optional_neighbour_counts_as_absent_and_is_noted()
    {
        var resolution = PluginGraph.Resolve(
            [
                Plugin("arxis.git", version: "1.0.0"),
                Plugin("arxis.a", depends: [Dep("arxis.git", min: "2.0", optional: true)]),
            ],
            present: []);

        Assert.Empty(resolution.Refused);
        Assert.Contains(resolution.Notes, note => note.Contains("2.0", StringComparison.Ordinal));

        // Порядок сосед не диктует: отсутствующий не может стоять «раньше».
        // Без его ребра уровень один, и в нём arxis.a идёт первым по
        // идентификатору — ребро переворачивало бы эту пару.
        Assert.Equal(["arxis.a", "arxis.git"], resolution.Order.Select(plugin => plugin.Id));
    }

    /// <summary>
    /// Цикл, замкнутый необязательным ребром, не фатален.
    /// </summary>
    /// <remarks>
    /// Optional по определению «не мешает»: замыкающее ребро отбрасывается из
    /// порядка со словом в журнал, а не валит обоих.
    /// </remarks>
    [Fact]
    public void An_optional_edge_that_closes_a_cycle_is_dropped_not_fatal()
    {
        var resolution = PluginGraph.Resolve(
            [
                Plugin("arxis.a", depends: [Dep("arxis.b")]),
                Plugin("arxis.b", depends: [Dep("arxis.a", optional: true)]),
            ],
            present: []);

        Assert.Empty(resolution.Refused);
        Assert.Equal(2, resolution.Order.Count);
        Assert.NotEmpty(resolution.Notes);
    }

    /// <summary>
    /// Неразобранная нижняя граница отказом не считается.
    /// </summary>
    /// <remarks>
    /// Манифест пишет человек; правило то же, что у <c>sdk.min</c>.
    /// </remarks>
    [Fact]
    public void An_unreadable_min_version_is_not_a_refusal()
    {
        var resolution = PluginGraph.Resolve(
            [
                Plugin("arxis.a"),
                Plugin("arxis.b", depends: [Dep("arxis.a", min: "следующая")]),
            ],
            present: []);

        Assert.Empty(resolution.Refused);
    }

    /// <summary>
    /// Языковой пакет — годная цель: он «есть», хотя поднимать его нечего.
    /// </summary>
    [Fact]
    public void A_language_pack_counts_as_present_without_rising()
    {
        var pack = new InstalledPlugin(
            Path.Combine(Path.GetTempPath(), "arxis.lang-de"),
            new PluginManifest { Id = "arxis.lang-de", Name = "Deutsch", Version = "1.0.0" },
            null,
            IsEnabled: true);

        var resolution = PluginGraph.Resolve(
            [pack, Plugin("arxis.b", depends: [Dep("arxis.lang-de")])],
            present: []);

        Assert.Empty(resolution.Refused);

        // В порядке подъёма пакета нет — узлом он не является.
        Assert.Equal(["arxis.b"], resolution.Order.Select(plugin => plugin.Id));
    }

    /// <summary>Встроенный модуль удовлетворяет зависимость как поднятый.</summary>
    [Fact]
    public void A_present_module_satisfies_a_dependency()
    {
        var module = Plugin("arxis.sample", version: "1.0.0") with { IsBuiltIn = true };

        var resolution = PluginGraph.Resolve(
            [Plugin("arxis.b", depends: [Dep("arxis.sample")])],
            present: [module]);

        Assert.Empty(resolution.Refused);
        Assert.Equal(["arxis.b"], resolution.Order.Select(plugin => plugin.Id));
    }

    /// <summary>
    /// Зависимые для менеджера — транзитивные и только обязательные.
    /// </summary>
    /// <remarks>
    /// Выключение соседа необязательную связь не ломает: пугать человека
    /// этими именами значило бы врать.
    /// </remarks>
    [Fact]
    public void Dependents_for_the_manager_are_transitive_and_mandatory_only()
    {
        var among = new[]
        {
            Plugin("arxis.b", depends: [Dep("arxis.a")]),
            Plugin("arxis.c", depends: [Dep("arxis.b")]),
            Plugin("arxis.d", depends: [Dep("arxis.a", optional: true)]),
        };

        var mandatory = PluginGraph.Dependents("arxis.a", among, includeOptional: false);

        Assert.Equal(["arxis.b", "arxis.c"], mandatory.Select(plugin => plugin.Id).Order());

        var all = PluginGraph.Dependents("arxis.a", among, includeOptional: true);

        Assert.Contains("arxis.d", all.Select(plugin => plugin.Id));
    }

    /// <summary>Карточка видит состояние каждой зависимости.</summary>
    [Fact]
    public void Describe_answers_the_health_of_each_dependency()
    {
        var plugin = Plugin("arxis.b", depends:
        [
            Dep("arxis.ok"),
            Dep("arxis.gone"),
            Dep("arxis.off"),
            Dep("arxis.old", min: "2.0"),
        ]);

        var all = new[]
        {
            plugin,
            Plugin("arxis.ok", version: "1.0.0"),
            Plugin("arxis.off") with { IsEnabled = false },
            Plugin("arxis.old", version: "1.0.0"),
        };

        var states = PluginGraph.Describe(plugin, all);

        Assert.Equal(
            [
                PluginDependencyHealth.Present,
                PluginDependencyHealth.Missing,
                PluginDependencyHealth.Disabled,
                PluginDependencyHealth.Stale,
            ],
            states.Select(state => state.Health));

        Assert.True(states[1].IsProblem);
        Assert.False(states[0].IsProblem);
    }

    private static PluginDependency Dep(string id, string? min = null, bool optional = false) =>
        new() { Id = id, Min = min, Optional = optional };


    /// <summary>
    /// Причина отказа не называет своего же плагина.
    /// </summary>
    /// <remarks>
    /// Имя подставляет тот, кто показывает причину: журнал студии пишет
    /// «Имя: причина». Назови её ещё и сама — вышло бы «Плагин: Плагин:
    /// нужен…», а в цепочке имя двоилось бы дважды. Нашлось живой проверкой,
    /// на настоящем журнале.
    /// </remarks>
    [Fact]
    public void A_refusal_does_not_repeat_the_name_of_its_own_plugin()
    {
        var lonely = Plugin("gr.lonely", "Одинокий");

        lonely.Manifest!.Dependencies.Add(new PluginDependency { Id = "gr.missing" });

        var resolution = PluginGraph.Resolve([lonely], []);

        Assert.DoesNotContain("Одинокий", resolution.Refused["gr.lonely"], StringComparison.Ordinal);
        Assert.StartsWith("нужен", resolution.Refused["gr.lonely"], StringComparison.Ordinal);
    }

    private static InstalledPlugin Plugin(
        string id,
        string? name = null,
        string version = "1.0.0",
        PluginDependency[]? depends = null)
    {
        var manifest = new PluginManifest
        {
            Id = id,
            Name = name ?? id,
            Version = version,
            Entry = "bin/Probe.dll",
        };

        foreach (var dependency in depends ?? [])
            manifest.Dependencies.Add(dependency);

        return new InstalledPlugin(Path.Combine(Path.GetTempPath(), id), manifest, null, IsEnabled: true);
    }
}
