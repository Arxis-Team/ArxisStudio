using System.Reflection;
using System.Text;
using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Встроенный модуль: второй способ доставки за тем же контрактом.
/// </summary>
/// <remarks>
/// Модулей в репозитории больше нет — их удалили вместе с работой над проектом,
/// — но способ остался: панель, приезжающая со студией, поднимается тем же
/// хостом, что и внешний плагин, и отличается только тем, откуда взялся
/// манифест и в каком контексте живут сборки.
/// <para>
/// Проверять это стало нечем, поэтому модуль собирается прямо здесь: сборка
/// с встроенным <c>module.json</c> компилируется в память и подсовывается
/// хосту. Так проверяется контракт, а не конкретный модуль, — а именно
/// контракт и должен пережить удаление своих первых пользователей.
/// </para>
/// </remarks>
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
    /// Собирает в память сборку модуля со встроенным манифестом.
    /// </summary>
    /// <remarks>
    /// Отдельный проект ради этого заводить не стоит: модулю нужны манифест
    /// ресурсом и один класс точки входа, и то и другое компилятор выдаёт
    /// прямо здесь.
    /// </remarks>
    private static Assembly Module()
    {
        const string source = """
            using ArxisStudio.Sdk;

            namespace Probe;

            public sealed class ProbeModule : StudioPlugin
            {
                public override void Activate(IStudioContext context)
                {
                }
            }
            """;

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "Arxis.ProbeModule",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var image = new MemoryStream();

        var result = compilation.Emit(
            image,
            manifestResources:
            [
                new ResourceDescription(
                    "Probe.module.json",
                    () => new MemoryStream(Encoding.UTF8.GetBytes(Manifest)),
                    isPublic: true),
            ]);

        // Сборка, не собравшаяся сама, проверила бы что угодно, кроме контракта.
        Assert.True(
            result.Success,
            string.Join("; ", result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.GetMessage())));

        return Assembly.Load(image.ToArray());
    }
}
