using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Analyzers;
using ArxisStudio.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Шаблон плагина: с чего начинает автор.
/// </summary>
/// <remarks>
/// Начинает он не с чистого листа и не с копирования примера: <c>dotnet new
/// arxis-plugin</c> даёт готовый плагин — манифест, точку входа, команду, кнопку
/// в полосе и панель. Устаревший шаблон хуже отсутствующего: по нему напишут
/// плагин, который студия не поднимет.
/// <para>
/// Подстановка здесь повторяется вручную, как и у языкового пакета: гонять
/// <c>dotnet new</c> из теста значило бы проверять чужую программу. Плейсхолдеры
/// в шаблоне нарочно не пересекаются, поэтому простой замены достаточно.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginTemplateTests : IDisposable
{
    private readonly List<string> _folders = [];

    public void Dispose()
    {
        foreach (var folder in _folders.Where(Directory.Exists))
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Из шаблона выходит плагин, который студия поднимает и понимает.
    /// </summary>
    /// <remarks>
    /// Проверяется вся дорога разом: манифест читается каталогом, сборка
    /// поднимается хостом, команда заявляется атрибутом, а панель находится по
    /// тому же идентификатору, что объявлен в манифесте.
    /// </remarks>
    [Fact]
    public void A_plugin_made_from_the_template_rises_and_matches_its_manifest()
    {
        var made = Generate("Probe.Figma", "probe.figma", "Проба");
        var commands = new StudioCommands();

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null));

        var assembly = TestAssembly.Emit("Probe.Figma", made.Sources, made.Manifest);
        var loaded = host.LoadBuiltIn(assembly);

        Assert.True(loaded.IsLoaded, loaded.Error);
        Assert.Equal("probe.figma", loaded.Installed.Id);

        var manifest = loaded.Installed.Manifest!;

        // Команду никто не заявлял руками — её связал атрибут, и связал ту, что
        // объявлена манифестом.
        Assert.All(
            manifest.Contributions.Commands,
            command => Assert.Contains(command.Id, commands.Registered));

        Assert.True(commands.Invoke("probe.figma.hello"), "команда шаблона не вызвалась");

        // Панели и свои контролы полосы: у каждого объявленного есть класс.
        var panels = Ids<ToolWindowAttribute>(assembly, attribute => attribute.Id);
        var items = Ids<ToolBarItemAttribute>(assembly, attribute => attribute.Id);

        Assert.All(manifest.Contributions.ToolWindows, panel => Assert.Contains(panel.Id, panels));
        Assert.All(
            manifest.Contributions.ToolBar.Where(item => item.IsCustom),
            item => Assert.Contains(item.Id, items));

        // Кнопки полосы зовут только объявленные команды.
        var declared = manifest.Contributions.Commands.Select(command => command.Id).ToList();

        Assert.NotEmpty(manifest.Contributions.ToolBar);
        Assert.All(
            manifest.Contributions.ToolBar.Where(item => item.IsButton),
            button => Assert.Contains(button.Command, declared));
    }

    /// <summary>
    /// Свежий плагин из шаблона проходит собственные правила студии.
    /// </summary>
    /// <remarks>
    /// Шаблон — это первое, что автор соберёт, и первое замечание он получит от
    /// нас же. Плагин, из коробки спорящий с анализатором, учил бы не тому, чему
    /// учат правила.
    /// </remarks>
    [Fact]
    public async Task A_fresh_plugin_passes_the_rules_of_the_studio()
    {
        var made = Generate("Probe.Figma", "probe.figma", "Проба");

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "Probe.Figma",
            made.Sources.Select((text, at) => CSharpSyntaxTree.ParseText(text, path: $"C:/probe/{at}.cs")),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var files = ImmutableArray.Create<AdditionalText>(
            new Given("C:/probe/plugin.json", made.Manifest),
            new Given("C:/probe/lang/strings.json", made.Strings));

        var analyzed = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(
                new AvaloniaWidgetAnalyzer(),
                new ManifestStringsAnalyzer(),
                new ToolBarAnalyzer()),
            new AnalyzerOptions(files));

        var found = await analyzed.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(found.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}"));
    }

    /// <summary>Плейсхолдеров в готовом плагине не остаётся.</summary>
    /// <remarks>
    /// Забытая подстановка не ломает сборку — она приезжает к человеку словом
    /// <c>PLUGIN-NAME</c> в интерфейсе.
    /// </remarks>
    [Fact]
    public void Nothing_of_the_template_placeholders_survives()
    {
        var made = Generate("Probe.Figma", "probe.figma", "Проба");

        foreach (var text in made.Sources.Append(made.Manifest).Append(made.Strings).Append(made.Project))
        {
            Assert.DoesNotContain("PLUGIN-NAME", text, StringComparison.Ordinal);
            Assert.DoesNotContain("STUDIO-PATH", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Arxis.MyPlugin", text, StringComparison.Ordinal);
            Assert.DoesNotContain("arxis.my-plugin", text, StringComparison.Ordinal);
        }

        // Проект зовут так же, как плагин: имя файла тоже под подстановкой.
        Assert.Contains("Probe.Figma.csproj", made.Files);
    }

    /// <summary>Собирает плагин так, как это сделал бы <c>dotnet new</c>.</summary>
    private Made Generate(string name, string id, string display)
    {
        Ready();

        var source = Template();
        var target = Path.Combine(Path.GetTempPath(), $"arxis-plugin-{Guid.NewGuid():N}");

        _folders.Add(target);

        var files = new List<string>();
        var sources = new List<string>();
        string? manifest = null;
        string? strings = null;
        string? project = null;

        var rules = Rules(source, name, id, display);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            // Папка .template.config — служебная, в готовый плагин она не едет.
            if (file.Contains(".template.config", StringComparison.Ordinal))
                continue;

            var relative = Substitute(Path.GetRelativePath(source, file), rules);
            var text = Substitute(File.ReadAllText(file), rules);
            var written = Path.Combine(target, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(written)!);
            File.WriteAllText(written, text);

            files.Add(Path.GetFileName(relative));

            if (relative.EndsWith(".cs", StringComparison.Ordinal))
                sources.Add(text);
            else if (Path.GetFileName(relative) == "plugin.json")
                manifest = text;
            else if (Path.GetFileName(relative) == "strings.json")
                strings = text;
            else if (relative.EndsWith(".csproj", StringComparison.Ordinal))
                project = text;
        }

        Assert.NotNull(manifest);
        Assert.NotNull(strings);
        Assert.NotNull(project);
        Assert.NotEmpty(sources);

        return new Made(files, sources, manifest!, strings!, project!);
    }

    /// <summary>Поднимает в домен сборки, против которых собирается плагин.</summary>
    /// <remarks>
    /// Ссылки для компиляции берутся из уже поднятых сборок, а поднимаются они
    /// только когда их кто-то тронул. Порядок тестов не наш: без этого шаблон
    /// собрался бы или нет в зависимости от того, что запускалось до него.
    /// </remarks>
    private static void Ready()
    {
        Assert.NotNull(typeof(ArxisStudio.Controls.AxButton).Assembly);
        Assert.NotNull(typeof(Avalonia.Controls.StackPanel).Assembly);
        Assert.NotNull(typeof(StudioPlugin).Assembly);
    }

    private static string Substitute(string text, IReadOnlyList<(string From, string To)> rules) =>
        rules.Aggregate(text, (current, rule) => current.Replace(rule.From, rule.To, StringComparison.Ordinal));

    /// <summary>Читает подстановки из описания шаблона.</summary>
    /// <remarks>
    /// Из описания, а не списком здесь: подставляет по нему <c>dotnet new</c>, и
    /// тест, знающий свои плейсхолдеры отдельно, продолжил бы собирать плагин
    /// после того, как шаблон перестал его собирать.
    /// </remarks>
    private static IReadOnlyList<(string From, string To)> Rules(
        string source,
        string name,
        string id,
        string display)
    {
        using var config = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(source, ".template.config", "template.json")));

        var given = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["display"] = display,
            ["studio"] = "../ArxisStudio",
        };

        // sourceName переименовывает и файлы, и всё, что внутри: проект,
        // пространство имён, сборку и путь к ней в манифесте.
        var rules = new List<(string, string)>
        {
            (config.RootElement.GetProperty("sourceName").GetString()!, name),
        };

        foreach (var symbol in config.RootElement.GetProperty("symbols").EnumerateObject())
        {
            if (!symbol.Value.TryGetProperty("replaces", out var replaces))
                continue;

            // Параметр, о котором тест не знает, ведёт себя как у человека,
            // ничего не указавшего, — берётся умолчание шаблона.
            var value = given.TryGetValue(symbol.Name, out var wanted)
                ? wanted
                : symbol.Value.GetProperty("defaultValue").GetString()!;

            rules.Add((replaces.GetString()!, value));
        }

        return rules;
    }

    private static IReadOnlyCollection<string> Ids<T>(Assembly assembly, Func<T, string> id) where T : Attribute =>
        assembly.GetTypes()
            .Select(type => type.GetCustomAttribute<T>())
            .OfType<T>()
            .Select(id)
            .ToList();

    private static string Template()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "templates", "Arxis.Plugin");

            if (File.Exists(Path.Combine(candidate, ".template.config", "template.json")))
                return candidate;
        }

        throw new InvalidOperationException("Не найден шаблон templates/Arxis.Plugin");
    }

    /// <summary>Готовый плагин, как его увидит автор.</summary>
    private sealed record Made(
        IReadOnlyList<string> Files,
        IReadOnlyList<string> Sources,
        string Manifest,
        string Strings,
        string Project);

    /// <summary>Файл, переданный анализатору входом сборки.</summary>
    private sealed class Given(string path, string content) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(content);
    }
}
