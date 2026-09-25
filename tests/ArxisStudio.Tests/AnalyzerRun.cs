using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Прогон анализатора в тесте: сборка-проба, входы сборки и то, что анализатор о них сказал.
/// </summary>
/// <remarks>
/// Четырнадцать наборов собирали пробу своими руками — ссылки, дерево, параметры, прогон, — и
/// различались в этом только опорными типами, входными файлами и ролями словарей. Одна дорога
/// держит одно правило ссылок: всё, что загружено в процесс тестов, плюс сборки названных типов, —
/// и новый набор анализатора пишется о правилах, а не о Roslyn.
/// </remarks>
internal static class AnalyzerRun
{
    /// <summary>Проба без кода — для правил, которым нужна только сборка, а смотрят они на входы.</summary>
    public const string EmptyProbe = "public sealed class Probe { }";

    /// <summary>
    /// Ссылки пробы: сборки, загруженные в процесс тестов, и сборки названных типов.
    /// </summary>
    /// <param name="anchors">
    /// Типы, чьи сборки нужны пробе: сборки грузятся лениво, и без касания типа ни контролов студии,
    /// ни виджетов Avalonia в списке может не оказаться вовсе.
    /// </param>
    /// <param name="keep">Какие сборки оставить; null — все.</param>
    public static IReadOnlyList<MetadataReference> References(
        IEnumerable<Type>? anchors = null,
        Func<Assembly, bool>? keep = null) =>
    [
        .. AppDomain.CurrentDomain.GetAssemblies()
            .Concat((anchors ?? []).Select(anchor => anchor.Assembly))
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Where(assembly => keep?.Invoke(assembly) ?? true)
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location)),
    ];

    /// <summary>Дерево пробы.</summary>
    /// <param name="source">Код.</param>
    /// <param name="path">Имя файла: без него замечание в коде не найти — путь в нём пуст.</param>
    public static SyntaxTree Tree(string source, string path = "") =>
        CSharpSyntaxTree.ParseText(source, path: path, cancellationToken: TestContext.Current.CancellationToken);

    /// <summary>Сборка-проба из деревьев.</summary>
    /// <param name="trees">Код пробы.</param>
    /// <param name="references">Ссылки; null — <see cref="References"/> без опорных типов.</param>
    /// <param name="name">Имя сборки.</param>
    public static CSharpCompilation Probe(
        IEnumerable<SyntaxTree> trees,
        IEnumerable<MetadataReference>? references = null,
        string name = "Probe") =>
        CSharpCompilation.Create(
            name,
            trees,
            references ?? References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>Сборка-проба из одного куска кода.</summary>
    /// <param name="source">Код пробы.</param>
    /// <param name="references">Ссылки; null — <see cref="References"/> без опорных типов.</param>
    public static CSharpCompilation Probe(string source, IEnumerable<MetadataReference>? references = null) =>
        Probe([Tree(source)], references);

    /// <summary>
    /// Требует, чтобы проба собиралась без ошибок, и отдаёт её дальше.
    /// </summary>
    /// <remarks>Ошибки самого кода означали бы, что тест проверяет не то, что думает.</remarks>
    public static CSharpCompilation Compiling(this CSharpCompilation probe)
    {
        var broken = probe.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(broken.Count == 0, string.Join("; ", broken.Select(diagnostic => diagnostic.GetMessage())));

        return probe;
    }

    /// <summary>Прогоняет анализатор над пробой.</summary>
    /// <param name="probe">Сборка-проба.</param>
    /// <param name="analyzer">Анализатор.</param>
    /// <param name="files">Входы сборки — манифест, словари, разметка; null — их нет.</param>
    /// <param name="options">Метаданные входов — роли словарей; null — их нет.</param>
    public static Task<ImmutableArray<Diagnostic>> RunAsync(
        this CSharpCompilation probe,
        DiagnosticAnalyzer analyzer,
        IEnumerable<AdditionalText>? files = null,
        AnalyzerConfigOptionsProvider? options = null) =>
        probe.RunAsync([analyzer], files, options);

    /// <summary>Прогоняет несколько анализаторов разом — так их видит сборка плагина.</summary>
    /// <param name="probe">Сборка-проба.</param>
    /// <param name="analyzers">Анализаторы.</param>
    /// <param name="files">Входы сборки; null — их нет.</param>
    /// <param name="options">Метаданные входов; null — их нет.</param>
    public static Task<ImmutableArray<Diagnostic>> RunAsync(
        this CSharpCompilation probe,
        IEnumerable<DiagnosticAnalyzer> analyzers,
        IEnumerable<AdditionalText>? files = null,
        AnalyzerConfigOptionsProvider? options = null)
    {
        var inputs = (files ?? []).ToImmutableArray();

        return probe
            .WithAnalyzers(
                [.. analyzers],
                options is null ? new AnalyzerOptions(inputs) : new AnalyzerOptions(inputs, options))
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>Файл, переданный анализатору входом сборки.</summary>
/// <param name="path">Путь, под которым его видит анализатор.</param>
/// <param name="content">Содержимое.</param>
internal sealed class AdditionalFile(string path, string content) : AdditionalText
{
    /// <inheritdoc/>
    public override string Path => path;

    /// <inheritdoc/>
    public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(content);
}
