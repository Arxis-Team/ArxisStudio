using System.Reflection;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Sample;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Встроенный модуль: второй способ доставки за тем же контрактом.
/// </summary>
/// <remarks>
/// Панель, приезжающая вместе со студией, поднимается тем же хостом, что и
/// внешний плагин, и отличается только тем, откуда взялся манифест и в каком
/// контексте живут сборки.
/// <para>
/// Проверяется это дважды и по разным причинам. Сам контракт — на сборке,
/// собранной прямо здесь, в память: она отвечает за случаи, которых у примера
/// нет, вроде забытого манифеста. Поставляемый модуль
/// <c>ArxisStudio.Modules.Sample</c> — на том, что его манифест и его код
/// говорят одно и то же: разойтись они могут молча, и человек увидит пустое
/// место в зоне вместо панели.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class BuiltInModuleTests
{
    private const string Manifest = """
        {
          "id": "arxis.probe",
          "name": "Проба",
          "version": "1.0.0",
          "contributions": {
            "toolWindows": [ { "id": "probe.panel", "title": "Проба", "zone": "left" } ]
          },
          "activation": [ "onStartup" ]
        }
        """;

    /// <summary>Манифест модуля читается из его сборки.</summary>
    [Fact]
    public void The_manifest_of_a_built_in_module_is_read_from_the_assembly()
    {
        var (manifest, error) = ModuleManifest.Load(Module());

        Assert.Null(error);
        Assert.NotNull(manifest);
        Assert.Equal("arxis.probe", manifest!.Id);
        Assert.Equal("probe.panel", Assert.Single(manifest.Contributions.ToolWindows).Id);
    }

    /// <summary>
    /// Сборка без манифеста объясняет, почему не поднялась.
    /// </summary>
    /// <remarks>
    /// Забыть встроить <c>module.json</c> — самая обычная ошибка при заведении
    /// модуля, и молчание в ответ означало бы панель, которой нет, без единого
    /// слова о причине.
    /// </remarks>
    [Fact]
    public void An_assembly_without_a_manifest_says_why()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(BuiltInModuleTests).Assembly);

        Assert.Null(manifest);
        Assert.NotNull(error);
        Assert.Contains("module.json", error);
    }

    /// <summary>
    /// Модуль поднимается тем же хостом и остаётся в основном контексте.
    /// </summary>
    /// <remarks>
    /// Своего выгружаемого контекста у встроенного модуля нет и быть не должно:
    /// он приезжает со студией, выключать его отдельно нечем, а лишний контекст
    /// раздвоил бы типы, которые он делит с оболочкой.
    /// </remarks>
    [Fact]
    public void A_built_in_module_rises_in_the_main_context()
    {
        using var host = new PluginHost(
            new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        var loaded = host.LoadBuiltIn(Module());

        Assert.True(loaded.IsLoaded, loaded.Error);
        Assert.Null(loaded.Context);
        Assert.NotEmpty(loaded.Entries);
        Assert.Equal("arxis.probe", loaded.Installed.Id);
    }

    /// <summary>
    /// У каждой панели, объявленной в манифесте примера, есть класс в сборке.
    /// </summary>
    /// <remarks>
    /// Манифест и код — две записи об одном, и разойтись они могут молча:
    /// панель переименовали в коде, а в манифесте забыли, — и человек увидит
    /// пустое место в зоне вместо панели. Оболочка ищет класс по
    /// идентификатору из манифеста, здесь тем же способом ищет и тест.
    /// </remarks>
    [Fact]
    public void The_sample_module_carries_every_panel_it_declares()
    {
        var assembly = typeof(SampleModule).Assembly;
        var (manifest, error) = ModuleManifest.Load(assembly);

        Assert.Null(error);
        Assert.NotNull(manifest);

        var declared = manifest!.Contributions.ToolWindows.Select(panel => panel.Id).ToList();

        Assert.NotEmpty(declared);

        var built = assembly.GetTypes()
            .Select(type => type.GetCustomAttribute<ToolWindowAttribute>()?.Id)
            .OfType<string>()
            .ToList();

        Assert.All(declared, id => Assert.Contains(id, built));
    }

    /// <summary>
    /// Пример поднимается как встроенный модуль и заявляет свою команду.
    /// </summary>
    /// <remarks>
    /// Это тот же путь, которым его поднимает студия: манифест из ресурса,
    /// сборка из основного контекста, команда — через контекст. Панель здесь
    /// не строится: её строит оболочка, когда ставит в зону.
    /// </remarks>
    [Fact]
    public void The_sample_module_rises_and_registers_its_command()
    {
        var commands = new StudioCommands();

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null));

        var loaded = host.LoadBuiltIn(typeof(SampleModule).Assembly);

        Assert.True(loaded.IsLoaded, loaded.Error);
        Assert.Null(loaded.Context);
        Assert.Equal("arxis.sample", loaded.Installed.Id);
        Assert.Contains(SampleModule.AboutCommand, commands.Registered);
        Assert.Contains(SampleModule.VerboseCommand, commands.Registered);
        Assert.NotEmpty(loaded.Services);

        // Переключатель зовёт полосу, которой у этой студии нет, — и обязан
        // это пережить: службы контекста необязательны по контракту.
        Assert.True(commands.Invoke(SampleModule.VerboseCommand), "переключатель не вызвался");
    }

    /// <summary>
    /// Каждая кнопка модуля в полосе зовёт команду, которую модуль объявил.
    /// </summary>
    /// <remarks>
    /// Кнопка и команда — две записи об одном; разойдясь, они дали бы кнопку,
    /// за которой никого нет, и щелчок отвечал бы замечанием в журнал.
    /// </remarks>
    [Fact]
    public void Every_toolbar_button_of_the_sample_names_a_declared_command()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(SampleModule).Assembly);

        Assert.Null(error);

        var commands = manifest!.Contributions.Commands.Select(command => command.Id).ToList();
        var buttons = manifest.Contributions.ToolBar.Where(item => item.IsButton).ToList();

        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.Contains(button.Command, commands));
    }

    /// <summary>Сборка модуля со встроенным манифестом.</summary>
    private static Assembly Module() => TestAssembly.Emit(
        "Arxis.ProbeModule",
        """
            using ArxisStudio.Sdk;

            namespace Probe;

            public sealed class ProbeModule : StudioPlugin
            {
                public override void Activate(IStudioContext context)
                {
                }
            }
            """,
        Manifest);
}
