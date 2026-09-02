using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Собирает сборку плагина прямо в память.
/// </summary>
/// <remarks>
/// Заводить отдельный проект ради каждого случая — забытого манифеста, команды
/// не там, где положено, — дороже самой проверки: плагину нужен манифест
/// ресурсом и несколько строк кода, и то и другое компилятор выдаёт здесь.
/// Настоящие примеры — <c>Arxis.HelloPlugin</c> и
/// <c>ArxisStudio.Modules.Sample</c> — отвечают за то, что работает вся дорога;
/// эти сборки отвечают за случаи, которых у примеров нет и быть не должно.
/// </remarks>
internal static class TestAssembly
{
    /// <summary>
    /// Компилирует сборку со встроенным манифестом.
    /// </summary>
    /// <param name="name">Имя сборки.</param>
    /// <param name="source">Исходный код.</param>
    /// <param name="manifest">Содержимое <c>module.json</c>; null — без манифеста.</param>
    public static Assembly Emit(string name, string source, string? manifest = null) =>
        Emit(name, [source], manifest);

    /// <summary>
    /// Компилирует сборку из нескольких файлов.
    /// </summary>
    /// <param name="name">Имя сборки.</param>
    /// <param name="sources">Исходные файлы.</param>
    /// <param name="manifest">Содержимое <c>module.json</c>; null — без манифеста.</param>
    /// <remarks>
    /// Файлов бывает больше одного там, где проверяется не случай, а готовая
    /// раскладка: у шаблона плагина точка входа и панель лежат порознь, и
    /// склеить их в один файл значило бы проверять не то, что получит автор.
    /// </remarks>
    public static Assembly Emit(string name, IEnumerable<string> sources, string? manifest = null)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            name,
            sources.Select(text => CSharpSyntaxTree.ParseText(text)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var image = new MemoryStream();

        var resources = manifest is null
            ? Array.Empty<ResourceDescription>()
            : [new ResourceDescription($"{name}.module.json", () => new MemoryStream(Encoding.UTF8.GetBytes(manifest)), isPublic: true)];

        var result = compilation.Emit(image, manifestResources: resources);

        // Сборка, не собравшаяся сама, проверила бы что угодно, кроме контракта.
        Assert.True(
            result.Success,
            string.Join("; ", result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.GetMessage())));

        return Assembly.Load(image.ToArray());
    }
}
