using System.Reflection;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Модуль делится контрактом и версией наравне с плагином.
/// </summary>
/// <remarks>
/// Встроенный модуль приезжает со студией, но интерфейс, который он отдаёт
/// соседям, берут плагины — и берут той же дорогой, что у плагина: тип из
/// общего контекста, версию из службы соседей. Пока ни один модуль ничего не
/// отдавал, этими дорогами у модулей просто никто не ходил.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ModuleContractsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-module-contracts-{Guid.NewGuid():N}");

    public ModuleContractsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Контракт живёт в общем контексте до конца процесса и держит свой файл.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Объявленный модулем и отсутствующий контракт — отказ модулю.
    /// </summary>
    /// <remarks>
    /// Подъём модуля манифест контрактов не читал вовсе: модуль вставал, а
    /// соседи, рассчитывавшие на его типы, падали там, куда автор не
    /// заглядывал. У плагина это обещание проверяется с первого дня.
    /// </remarks>
    [Fact]
    public void A_module_that_declares_a_missing_contract_does_not_rise()
    {
        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        var loaded = host.LoadBuiltIn(TestAssembly.Emit("Arxis.ContractModule", ModuleSource, """
            {
              "id": "arxis.contractmodule",
              "name": "Контракт модуля",
              "version": "1.0.0",
              "provides": { "contracts": [ "No.Such.Module.Contracts.dll" ] }
            }
            """));

        Assert.False(loaded.IsLoaded, "модуль поднялся без объявленного контракта");
        Assert.Contains("No.Such.Module.Contracts", loaded.Error);
    }

    /// <summary>
    /// Контракт модуля — сам файл студии, а не теневая копия.
    /// </summary>
    /// <remarks>
    /// Копия нужна плагину: его пересобирают, пока студия открыта. Модуль не
    /// перезагружается, а к своему контракту привязан по имени из списка сборок
    /// приложения — и копия, загруженная раньше самого модуля, стала бы второй
    /// сборкой того же имени, то есть тем самым расколом типа, от которого
    /// контракты заведены.
    /// </remarks>
    [Fact]
    public void A_module_contract_is_the_file_itself_not_a_shadow_copy()
    {
        var name = $"Probe.ModuleContracts{Guid.NewGuid():N}";
        var folder = Directory.CreateDirectory(Path.Combine(_root, "probe.module")).FullName;
        var file = Path.Combine(folder, name + ".dll");

        TestAssembly.EmitFile(file, name, "namespace Probe; public interface IProbeContract { }");

        File.WriteAllText(Path.Combine(folder, "plugin.json"), $$"""
            {
              "id": "probe.module",
              "name": "probe.module",
              "version": "1.0.0",
              "provides": { "contracts": [ "{{name}}.dll" ] }
            }
            """);

        var module = Assert.Single(new PluginCatalog(_root).Scan()) with { IsBuiltIn = true };

        Assert.Null(PluginContracts.EnsureLoaded(module, []));

        var contract = PluginContracts.Find(new AssemblyName(name));

        Assert.NotNull(contract);
        Assert.Equal(file, contract!.Location, ignoreCase: true);
    }

    /// <summary>
    /// Нижняя граница версии на модуль выдерживается так же, как на плагин.
    /// </summary>
    /// <remarks>
    /// Версию служба соседей брала только из каталога установленных, а модулей
    /// в каталоге нет. Сосед, объявивший «min» на модуль, видел его
    /// отсутствующим и получал null вместо его экспорта — при том что граф
    /// зависимостей ту же зависимость принимал.
    /// </remarks>
    [Fact]
    public void A_minimum_version_on_a_module_is_honoured_as_on_a_plugin()
    {
        var roster = new StudioPluginRoster();
        var exports = new StudioExportRegistry();

        using var host = new PluginHost(new StudioContextFactory(
            new StudioLog(), new StudioCommands(), null, plugins: roster, exports: exports));

        roster.Attach(host, () => []);

        var loaded = host.LoadBuiltIn(TestAssembly.Emit("Arxis.VersionedModule", PublishingSource, """
            {
              "id": "arxis.versioned",
              "name": "Версия модуля",
              "version": "2.1.0"
            }
            """));

        Assert.True(loaded.IsLoaded, loaded.Error);

        var satisfied = Consumer("probe.satisfied", "1.5");
        var stale = Consumer("probe.stale", "3.0");

        Assert.Equal("2.1.0", new PluginNeighbours(roster, satisfied).Version("arxis.versioned"));
        Assert.True(new PluginNeighbours(roster, satisfied).IsActive("arxis.versioned"), "модуль новее границы, а сосед его не видит");
        Assert.False(new PluginNeighbours(roster, stale).IsActive("arxis.versioned"), "модуль старее границы, а сосед его видит");

        // Главное следствие — экспорт: его-то и не получал тот, кто объявил границу.
        Assert.NotNull(new PluginExports(exports, satisfied, new PluginNeighbours(roster, satisfied)).Get<IStudioStatus>());
    }

    /// <summary>Сосед, объявивший нижнюю границу версии на модуль.</summary>
    private InstalledPlugin Consumer(string id, string min)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, id)).FullName;

        File.WriteAllText(Path.Combine(folder, "plugin.json"), $$"""
            {
              "id": "{{id}}",
              "name": "{{id}}",
              "version": "1.0.0",
              "dependencies": [ { "id": "arxis.versioned", "min": "{{min}}" } ]
            }
            """);

        return new PluginCatalog(_root).Scan().Single(plugin => plugin.Id == id);
    }

    private const string ModuleSource = """
        using ArxisStudio.Sdk;

        namespace Probe;

        public sealed class ContractModule : StudioPlugin
        {
        }
        """;

    private const string PublishingSource = """
        using ArxisStudio.Sdk;

        namespace Probe;

        public sealed class VersionedModule : StudioPlugin
        {
            public override void Activate(IStudioContext context) =>
                context.GetService<IStudioExports>()?.Publish<IStudioStatus>(new Status());

            private sealed class Status : IStudioStatus
            {
                public void Show(string message)
                {
                }
            }
        }
        """;
}
