using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Что приходит из манифеста, до общего контекста не долетает.
/// </summary>
/// <remarks>
/// Контракт грузится в общий контекст навсегда: выгрузить его нечем, а
/// достаётся он всем плагинам сразу. Поэтому каждая проверка здесь — про одно
/// и то же: сломанный или наглый манифест обязан стать отказом своему
/// владельцу, а не падением студии и не подменой чужой сборки.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginContractGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-guard-{Guid.NewGuid():N}");

    public PluginContractGuardTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Контракт, который не сборка, отказывает владельцу, а не рушит загрузку.
    /// </summary>
    /// <remarks>
    /// Раньше <c>BadImageFormatException</c> уходил из <c>LoadStartup</c>
    /// наружу, а тот зовётся из обработчика <c>Opened</c> без перехвата: один
    /// чужой файл уносил с собой все плагины и модули сразу, и отключить
    /// виновника было уже негде.
    /// </remarks>
    [Fact]
    public void A_contract_that_is_not_an_assembly_refuses_its_owner()
    {
        var plugin = Clone("con.notdll", "bin/NotAnAssembly.dll");

        File.WriteAllText(Path.Combine(plugin, "bin", "NotAnAssembly.dll"), "это не сборка");

        var failed = Assert.Single(Start());

        Assert.False(failed.IsLoaded);
        Assert.Contains("не читается как сборка", failed.Error);
    }

    /// <summary>Контракт вне папки плагина не грузится.</summary>
    /// <remarks>
    /// <c>Path.Combine</c> отбрасывает папку плагина целиком, если объявленный
    /// путь абсолютный. Без проверки любой манифест — включая языковой пакет,
    /// которому исполнять нечего, — открывал бы дорогу произвольному файлу в
    /// общий контекст студии.
    /// </remarks>
    [Fact]
    public void A_contract_outside_the_plugin_folder_refuses_its_owner()
    {
        var outside = Path.Combine(_root, "outside.dll");
        var plugin = Clone("con.escape", outside);

        File.Copy(Path.Combine(plugin, "bin", "Arxis.Hello.Contracts.dll"), outside);

        var failed = Assert.Single(Start());

        Assert.False(failed.IsLoaded);
        Assert.Contains("за пределы папки плагина", failed.Error);
    }

    /// <summary>Путь с «..» уводит наружу так же, как абсолютный.</summary>
    [Fact]
    public void A_contract_climbing_out_with_dots_refuses_its_owner()
    {
        Clone("con.dots", "../../outside.dll");

        var failed = Assert.Single(Start());

        Assert.False(failed.IsLoaded);
        Assert.Contains("за пределы папки плагина", failed.Error);
    }

    /// <summary>Имя общей сборки студии под контракт не отдаётся.</summary>
    /// <remarks>
    /// Резолвер спрашивает контракт раньше всего остального, поэтому файл,
    /// назвавшийся именем общей сборки, достался бы вместо настоящего и
    /// студии, и всем соседям — и отменить это до перезапуска нечем.
    /// </remarks>
    [Fact]
    public void A_contract_named_after_a_studio_assembly_refuses_its_owner()
    {
        var plugin = Clone("con.shadowsdk", "bin/ArxisStudio.Sdk.dll");

        // Берётся настоящая общая сборка, а не переименованный файл: имя
        // проверяется по манифесту сборки, и подделать его переименованием
        // нельзя — это и есть правильное поведение.
        File.Copy(
            typeof(Sdk.StudioSdk).Assembly.Location,
            Path.Combine(plugin, "bin", "ArxisStudio.Sdk.dll"),
            overwrite: true);

        var failed = Assert.Single(Start());

        Assert.False(failed.IsLoaded);
        Assert.Contains("занято общими сборками студии", failed.Error);
    }

    /// <summary>
    /// Имя контракта опознаётся по сборке, а не по имени файла.
    /// </summary>
    /// <remarks>
    /// Резолвер спрашивает контракт по имени сборки. Ключ по имени файла
    /// значил бы, что переименованный файл молча не находится, каждый плагин
    /// грузит свою копию — и тип раскалывается ровно там, где контракты его
    /// сращивают, без единого слова в журнале.
    /// </remarks>
    [Fact]
    public void A_renamed_contract_file_is_still_found_by_its_assembly_name()
    {
        var plugin = Clone("con.renamed", "bin/renamed.dll");

        File.Move(
            Path.Combine(plugin, "bin", "Arxis.Hello.Contracts.dll"),
            Path.Combine(plugin, "bin", "renamed.dll"));

        var loaded = Assert.Single(Start());

        Assert.True(loaded.IsLoaded, loaded.Error);

        // Спрашивается именно по имени файла: реестр под этим ключом обязан
        // быть пуст. Проверять обратное — что нашлось «Arxis.Hello.Contracts» —
        // бесполезно: это имя мог занять соседний тест, реестр на процесс.
        Assert.Null(PluginContracts.Find(new AssemblyName("renamed")));
    }

    /// <summary>
    /// Чужая сборка под уже занятым именем контракта отвергается.
    /// </summary>
    /// <remarks>
    /// Резолвер раздаёт контракт по имени всем контекстам сразу. Пусти мы
    /// вторую сборку с тем же именем, но своим содержимым — её типы достались
    /// бы соседям вместо ожидаемых, и виновника было бы уже не найти. Тот же
    /// контракт у второго плагина при этом законен: идентичность совпадает,
    /// делить нечего.
    /// </remarks>
    [Fact]
    public void A_foreign_assembly_under_a_taken_contract_name_is_refused()
    {
        Clone("con.honest", "bin/Arxis.Hello.Contracts.dll");

        var second = Clone("con.impostor", "bin/Arxis.Hello.Contracts.dll");

        // Самозванец: то же имя сборки, своя версия — то есть своя
        // идентичность. Собрать такую подмену иначе нельзя: имя живёт в
        // метаданных, переименованием файла его не подделать.
        Impostor(Path.Combine(second, "bin", "Arxis.Hello.Contracts.dll"));

        var raised = Start();

        // Кто из двоих успел первым, зависит от обхода каталога, и знать это
        // тесту незачем: важно, что имя досталось ровно одному, а второй
        // получил отказ словами, а не тихую подмену типов.
        Assert.Single(raised, plugin => plugin.IsLoaded);

        var refused = Assert.Single(raised, plugin => !plugin.IsLoaded);

        Assert.Contains("уже занято контрактом", refused.Error);
    }

    /// <summary>
    /// Отказ из-за контракта расходится по зависимым, как всякий другой.
    /// </summary>
    /// <remarks>
    /// Зависимый ссылается на типы соседа и не возит их у себя. Подними мы
    /// его после того, как контракт соседа не загрузился, он упал бы на
    /// первом обращении к этим типам — исключением, которого нет ни в одном
    /// перехвате, то есть падением всей студии. Причина обязана дойти до него
    /// словами и до подъёма.
    /// </remarks>
    [Fact]
    public void A_contract_refusal_spreads_to_dependents()
    {
        var owner = Clone("con.provider", "bin/NotAnAssembly.dll");

        File.WriteAllText(Path.Combine(owner, "bin", "NotAnAssembly.dll"), "это не сборка");

        var target = Path.Combine(_root, "con.user");

        ZipFile.ExtractToDirectory(HelloArchive.Path, target);

        File.WriteAllText(
            Path.Combine(target, "plugin.json"),
            """
            {
              "id": "con.user",
              "name": "con.user",
              "version": "1.0.0",
              "entry": "bin/Arxis.HelloPlugin.dll",
              "dependencies": [ { "id": "con.provider" } ],
              "activation": [ "onStartup" ]
            }
            """);

        var raised = Start();
        var dependent = raised.Single(plugin => plugin.Installed.Id == "con.user");

        Assert.False(dependent.IsLoaded);

        // Цепочка причин: своё имя, имя соседа и его собственная беда.
        Assert.Contains("con.provider", dependent.Error);
        Assert.Contains("не читается как сборка", dependent.Error);
    }

    /// <summary>
    /// Изменившийся контракт замечен и при старте, а не только при перезагрузке.
    /// </summary>
    /// <remarks>
    /// Выгрузить прежнюю копию из общего контекста нечем, поэтому единственное,
    /// что студия может дать человеку, — слова. Заметка обязана доехать по обеим
    /// дорогам: и через перезагрузку, и через обычный запуск, где о ней узнают
    /// из <see cref="PluginHost.Resolution"/>.
    /// </remarks>
    [Fact]
    public void A_changed_contract_is_noted_at_startup_too()
    {
        Clone("con.first", "bin/Arxis.Hello.Contracts.dll");

        var second = Clone("con.second", "bin/Arxis.Hello.Contracts.dll");

        // Та же сборка, но файл другого размера: идентичность совпадает —
        // значит не спор за имя, — а вот содержимое на диске разъехалось.
        var contract = Path.Combine(second, "bin", "Arxis.Hello.Contracts.dll");

        File.AppendAllText(contract, new string(' ', 64));

        using var host = new PluginHost(
            new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        host.LoadStartup(new PluginCatalog(_root).Scan());

        Assert.Contains(
            host.Resolution!.Notes,
            note => note.Contains("перезапуска", StringComparison.Ordinal));
    }

    /// <summary>
    /// Беда во втором контракте не оставляет первый загруженным.
    /// </summary>
    /// <remarks>
    /// Из общего контекста выгрузить нечего. Загрузи мы первый контракт до
    /// того, как споткнулись о второй, — сборка плагина, который так и не
    /// поднялся, осталась бы в студии навсегда: автор чинит манифест,
    /// пересобирает, а в ответ слышит «держу прежнюю копию до перезапуска»
    /// про сборку, ни разу не работавшую.
    /// </remarks>
    [Fact]
    public void A_bad_second_contract_leaves_the_first_unloaded()
    {
        var target = Path.Combine(_root, "con.pair");

        ZipFile.ExtractToDirectory(HelloArchive.Path, target);
        Emit("Pair.Contracts", new Version(1, 0, 0, 0), Path.Combine(target, "bin", "Pair.Contracts.dll"));
        File.WriteAllText(Path.Combine(target, "bin", "Broken.dll"), "это не сборка");

        File.WriteAllText(
            Path.Combine(target, "plugin.json"),
            """
            {
              "id": "con.pair",
              "name": "con.pair",
              "version": "1.0.0",
              "entry": "bin/Arxis.HelloPlugin.dll",
              "provides": { "contracts": [ "bin/Pair.Contracts.dll", "bin/Broken.dll" ] },
              "activation": [ "onStartup" ]
            }
            """);

        var failed = Assert.Single(Start());

        Assert.False(failed.IsLoaded);
        Assert.Contains("не читается как сборка", failed.Error);

        // Первый объявленный не должен был доехать до общего контекста.
        Assert.Null(PluginContracts.Find(new AssemblyName("Pair.Contracts")));
    }

    /// <summary>
    /// Контракт грузится теневой копией, а файл плагина остаётся свободен.
    /// </summary>
    /// <remarks>
    /// Ради этого теневая копия и заведена: общий контекст держит открытым то,
    /// что загрузил, и автор не пересобрал бы плагин, не закрыв студию. До сих
    /// пор проверить это было нечем — тесты подсовывали контракт, на который
    /// ссылается сам тестовый проект, а такой уже лежит в общем контексте и
    /// усыновляется без всякой копии. Здесь сборка выпускается на месте, и
    /// потому идёт настоящая, продуктовая дорога.
    /// </remarks>
    [Fact]
    public void The_contract_is_shadow_copied_and_the_original_stays_free()
    {
        var plugin = Clone("con.shadow", "bin/Probe.Contracts.dll");
        var contract = Path.Combine(plugin, "bin", "Probe.Contracts.dll");

        Emit("Probe.Contracts", new Version(1, 0, 0, 0), contract);

        var loaded = Assert.Single(Start());

        Assert.True(loaded.IsLoaded, loaded.Error);

        var found = PluginContracts.Find(new AssemblyName("Probe.Contracts"));

        Assert.NotNull(found);

        // Загружен не файл плагина, а копия: усыновлять было нечего.
        Assert.False(
            string.Equals(Path.GetFullPath(contract), found.Location, StringComparison.OrdinalIgnoreCase),
            "контракт загружен прямо из папки плагина — теневой копии не было");

        // И обещание сдержано: файл свободен, автор пересобирает плагин
        // прямо сейчас, не закрывая студию.
        File.WriteAllText(contract, "пересобрано");
    }

    /// <summary>
    /// Своя же пересборка контракта с новой версией говорит про перезапуск,
    /// а не про чужого захватчика.
    /// </summary>
    /// <remarks>
    /// Отказ тот же самый — общий контекст обновить нечем, — но причина
    /// совсем другая, и слова обязаны это различать. «Имя занято контрактом
    /// плагина arxis.foo», где arxis.foo и есть спрашивающий, отправляет
    /// автора искать несуществующего соседа.
    /// </remarks>
    [Fact]
    public void Rebuilding_ones_own_contract_asks_for_a_restart()
    {
        var plugin = Clone("con.bump", "bin/Bump.Contracts.dll");
        var contract = Path.Combine(plugin, "bin", "Bump.Contracts.dll");

        Emit("Bump.Contracts", new Version(1, 0, 0, 0), contract);

        using var studio = new TestHost();
        var catalog = new PluginCatalog(_root);

        Assert.Single(studio.Host.LoadStartup(catalog.Scan()), loaded => loaded.IsLoaded);

        // Автор пересобрал контракт и поднял версию сборки.
        Emit("Bump.Contracts", new Version(2, 0, 0, 0), contract);

        var again = studio.Host.Reload(catalog.Scan().Single(candidate => candidate.Id == "con.bump"));

        Assert.NotNull(again.Error);
        Assert.Contains("нужен перезапуск студии", again.Error);
        Assert.DoesNotContain("уже занято контрактом плагина", again.Error);
    }

    /// <summary>
    /// При перезагрузке отказ расходится по зависимым так же, как при старте.
    /// </summary>
    /// <remarks>
    /// Найдено живой проверкой. Обещание «раз я поднят, обязательная
    /// зависимость подо мной» держалось только на дороге старта: каскад
    /// проверял контракт у каждого поднимаемого по отдельности и валил
    /// одного, а остальных поднимал как ни в чём не бывало. Зависимый
    /// возвращался без соседа — не падая, потому что службы отвечают правду,
    /// но и не работая, — а человек видел один отказ вместо цепочки причин.
    /// </remarks>
    [Fact]
    public void On_reload_a_contract_refusal_spreads_to_dependents_too()
    {
        var provider = Clone("con.host", "bin/Reload.Contracts.dll");
        var contract = Path.Combine(provider, "bin", "Reload.Contracts.dll");

        Emit("Reload.Contracts", new Version(1, 0, 0, 0), contract);
        Dependent("con.guest", "con.host");

        using var studio = new TestHost();
        var catalog = new PluginCatalog(_root);

        Assert.All(studio.Host.LoadStartup(catalog.Scan()), loaded => Assert.True(loaded.IsLoaded, loaded.Error));

        // Автор пересобрал контракт с новой версией — общий контекст такое
        // не переживает, и провайдер получит отказ.
        Emit("Reload.Contracts", new Version(2, 0, 0, 0), contract);

        var installed = catalog.Scan().ToList();
        var cascade = studio.Host.Reload(
            ["con.guest", "con.host"],
            [.. installed.Where(plugin => plugin.Id is "con.host" or "con.guest")]);

        var host = cascade.Raised.Single(loaded => loaded.Installed.Id == "con.host");
        var guest = cascade.Raised.Single(loaded => loaded.Installed.Id == "con.guest");

        Assert.False(host.IsLoaded);
        Assert.Contains("нужен перезапуск студии", host.Error);

        // Главное: зависимый не поднялся и знает, из-за кого.
        Assert.False(guest.IsLoaded);
        Assert.Contains("con.host", guest.Error);
        Assert.Contains("а тот не поднят", guest.Error);
    }

    /// <summary>
    /// Ждущий своего события годится в зависимости и при перезагрузке.
    /// </summary>
    /// <remarks>
    /// Отложенный плагин установлен и включён — просто ещё не понадобился, и
    /// целью зависимости он остаётся. Забудь мы про таких, разбирая отказ, и
    /// зависимый от спящего соседа получил бы «нужен такой-то, а он не
    /// установлен» на ровном месте: не установлен он только в списке поднятых.
    /// <para>
    /// Через прямой вызов <c>Reload</c>, а не через студию: в самой студии
    /// нетерпеливый тянет свою отложенную зависимость наверх, и спящей она не
    /// остаётся. Но <c>Reload</c> — публичный API, состав поднимаемых задаёт
    /// вызывающий, и встраивающий вправе попросить именно так.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_sleeping_neighbour_still_counts_as_installed_on_reload()
    {
        var broken = Clone("con.willfail", "bin/Spread.Contracts.dll");

        Emit("Spread.Contracts", new Version(1, 0, 0, 0), Path.Combine(broken, "bin", "Spread.Contracts.dll"));
        Sleeper("con.sleeper");
        Sleeper("con.needy", dependsOn: "con.sleeper");

        using var studio = new TestHost();
        var catalog = new PluginCatalog(_root);

        studio.Host.LoadStartup(catalog.Scan());

        // Оба отложены: ни один не нетерпелив, тянуть их некому.
        Assert.Contains(studio.Host.Deferred, plugin => plugin.Id == "con.sleeper");
        Assert.Contains(studio.Host.Deferred, plugin => plugin.Id == "con.needy");

        Emit("Spread.Contracts", new Version(2, 0, 0, 0), Path.Combine(broken, "bin", "Spread.Contracts.dll"));

        var installed = catalog.Scan().ToList();
        var cascade = studio.Host.Reload(
            ["con.willfail"],
            [.. installed.Where(plugin => plugin.Id is "con.willfail" or "con.needy")]);

        // Отказ достался тому, у кого контракт, и только ему: сосед con.needy
        // зависит от спящего, а спящий на месте.
        Assert.False(cascade.Raised.Single(loaded => loaded.Installed.Id == "con.willfail").IsLoaded);

        var needy = cascade.Raised.Single(loaded => loaded.Installed.Id == "con.needy");

        Assert.True(needy.IsLoaded, needy.Error);
    }

    /// <summary>
    /// Пропавший с диска сосед не мешает перезагрузить зависимого.
    /// </summary>
    /// <remarks>
    /// Перезагрузка чинит то, что сломалось прямо сейчас, и не должна вдруг
    /// отказывать плагину за условие, с которым студия прожила весь сеанс:
    /// сосед поднят и работает, а что его папку убрали руками — беда
    /// следующего запуска, не этого щелчка.
    /// </remarks>
    [Fact]
    public void A_neighbour_gone_from_disk_does_not_block_reloading_its_dependent()
    {
        Sleeper("con.provider2");
        Dependent("con.consumer2", "con.provider2");

        using var studio = new TestHost();
        var catalog = new PluginCatalog(_root);

        studio.Host.LoadStartup(catalog.Scan());

        var installed = catalog.Scan().Single(plugin => plugin.Id == "con.consumer2");

        // Сосед исчезает с диска посреди сеанса — и всё же перезагрузка
        // зависимого проходит: отказов нет, пересматривать нечего.
        Directory.Delete(Path.Combine(_root, "con.provider2"), recursive: true);

        var again = studio.Host.Reload(installed);

        Assert.Null(again.Error);
    }

    /// <summary>Клонирует пример отложенным: поднимется только по команде.</summary>
    private void Sleeper(string id, string? dependsOn = null)
    {
        var target = Path.Combine(_root, id);
        var needs = dependsOn is null ? "" : $$"""
              "dependencies": [ { "id": "{{dependsOn}}" } ],
            """;

        ZipFile.ExtractToDirectory(HelloArchive.Path, target);

        File.WriteAllText(
            Path.Combine(target, "plugin.json"),
            $$"""
            {
              "id": "{{id}}",
              "name": "{{id}}",
              "version": "1.0.0",
              "entry": "bin/Arxis.HelloPlugin.dll",
            {{needs}}
              "activation": [ "onCommand:{{id}}.wake" ]
            }
            """);
    }

    /// <summary>Клонирует пример зависимым от названного соседа, без контрактов.</summary>
    private void Dependent(string id, string dependsOn)
    {
        var target = Path.Combine(_root, id);

        ZipFile.ExtractToDirectory(HelloArchive.Path, target);

        File.WriteAllText(
            Path.Combine(target, "plugin.json"),
            $$"""
            {
              "id": "{{id}}",
              "name": "{{id}}",
              "version": "1.0.0",
              "entry": "bin/Arxis.HelloPlugin.dll",
              "dependencies": [ { "id": "{{dependsOn}}" } ],
              "activation": [ "onStartup" ]
            }
            """);
    }

    /// <summary>Пишет на место контракта сборку того же имени, но своей версии.</summary>
    private static void Impostor(string path) =>
        Emit("Arxis.Hello.Contracts", new Version(9, 9, 9, 9), path);

    /// <summary>Выпускает крошечную сборку с заданным именем и версией.</summary>
    /// <remarks>
    /// Имя сборки живёт в метаданных: ни переименованием файла, ни копированием
    /// чужого его не задать — а тестам нужны и самозванец под занятым именем, и
    /// контракт, на который тестовый проект заведомо не ссылается.
    /// </remarks>
    private static void Emit(string name, Version version, string path)
    {
        var builder = new PersistedAssemblyBuilder(
            new AssemblyName(name) { Version = version },
            typeof(object).Assembly);

        builder.DefineDynamicModule(name)
            .DefineType($"{name}.IProbe", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract)
            .CreateType();

        using var file = File.Create(path);

        builder.Save(file);
    }

    /// <summary>Клонирует пример, объявив ему контракт по указанному пути.</summary>
    private string Clone(string id, string contract)
    {
        var target = Path.Combine(_root, id);

        ZipFile.ExtractToDirectory(HelloArchive.Path, target);

        File.WriteAllText(
            Path.Combine(target, "plugin.json"),
            $$"""
            {
              "id": "{{id}}",
              "name": "{{id}}",
              "version": "1.0.0",
              "entry": "bin/Arxis.HelloPlugin.dll",
              "provides": { "contracts": [ {{System.Text.Json.JsonSerializer.Serialize(contract)}} ] },
              "activation": [ "onStartup" ]
            }
            """);

        return target;
    }

    private IReadOnlyList<LoadedPlugin> Start()
    {
        using var host = new PluginHost(
            new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        return host.LoadStartup(new PluginCatalog(_root).Scan());
    }
}
