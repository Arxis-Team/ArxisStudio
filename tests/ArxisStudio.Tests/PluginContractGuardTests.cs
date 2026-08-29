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

    /// <summary>Пишет на место контракта сборку того же имени, но своей версии.</summary>
    private static void Impostor(string path)
    {
        var builder = new PersistedAssemblyBuilder(
            new AssemblyName("Arxis.Hello.Contracts") { Version = new Version(9, 9, 9, 9) },
            typeof(object).Assembly);

        builder.DefineDynamicModule("Arxis.Hello.Contracts")
            .DefineType("Arxis.Hello.Contracts.IGreeter", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract)
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
