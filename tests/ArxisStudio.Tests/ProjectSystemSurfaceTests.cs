using ArxisStudio.Extensibility;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Модель проектов — общая сборка студии, и её поверхность закреплена.
/// </summary>
/// <remarks>
/// Ядро <c>ArxisStudio.ProjectSystem</c> плагин видит напрямую: снимок решения,
/// проект, путь, диагностика — типы оттуда. Поэтому ядро одно на всех, как SDK
/// и контролы, и поэтому его поверхность — обещание, которое сдвиг указателя
/// подмодуля не вправе нарушить молча. Движки — MSBuild и NuGet — общими не
/// становятся: их держит служба проектов, а не каждый плагин.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectSystemSurfaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-model-surface-{Guid.NewGuid():N}");

    public ProjectSystemSurfaceTests() => Directory.CreateDirectory(_root);

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
    /// Поверхность ядра — та, что записана.
    /// </summary>
    /// <remarks>
    /// У самой библиотеки такой записи нет: она подключается исходниками и не
    /// публикуется, и список её публичных типов нигде не хранится. Для студии
    /// это обещание плагинам, поэтому список ведётся здесь — и сдвиг указателя,
    /// снявший или изменивший член, падает на этой строке, а не у автора плагина.
    /// </remarks>
    [Fact]
    public void The_public_surface_of_the_project_model_is_the_recorded_one()
    {
        PublicSurface.AssertRecorded(
            Path.Combine(Repository(), "tests", "ArxisStudio.Tests", "Surfaces", "ArxisStudio.ProjectSystem.txt"),
            PublicSurface.Describe(typeof(SolutionSnapshot).Assembly));
    }

    /// <summary>
    /// Ядро ссылается только на рантайм.
    /// </summary>
    /// <remarks>
    /// Общая сборка тащит свои ссылки в общий контекст каждому плагину. Ядро
    /// ProjectSystem обещает не ссылаться ни на что и держит это своими тестами,
    /// а студия проверяет обещание там, где оно ей стоит: на собранной сборке.
    /// </remarks>
    [Fact]
    public void The_project_model_references_nothing_but_the_runtime()
    {
        var foreign = typeof(SolutionSnapshot).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !(name == "System" || name.StartsWith("System.", StringComparison.Ordinal)
                || name is "netstandard" or "mscorlib"))
            .ToList();

        Assert.True(
            foreign.Count == 0,
            $"ядро модели ссылается не только на рантайм: {string.Join(", ", foreign)} — общей сборкой оно утащило бы это к каждому плагину");
    }

    /// <summary>
    /// Идентичность связывания ядра записана и не двигается сама.
    /// </summary>
    /// <remarks>
    /// Плагин запоминает версию сборки, против которой собран. Сдвиг её в
    /// подмодуле — повод решить за плагины, а не заметить после установки.
    /// </remarks>
    [Fact]
    public void The_binding_identity_of_the_project_model_does_not_move_by_itself()
    {
        Assert.Equal(new Version(0, 1, 0, 0), typeof(SolutionSnapshot).Assembly.GetName().Version);
    }

    /// <summary>
    /// Назваться моделью проектов контракт плагина не может.
    /// </summary>
    /// <remarks>
    /// Резолвер спрашивает контракты раньше всего остального. Файл плагина,
    /// объявленный контрактом под этим именем, достался бы вместо настоящего
    /// ядра и студии, и всем соседям — без возможности это отменить.
    /// </remarks>
    [Fact]
    public void A_plugin_contract_cannot_take_the_name_of_the_project_model()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "probe.impostor")).FullName;

        File.Copy(typeof(SolutionSnapshot).Assembly.Location, Path.Combine(folder, "ArxisStudio.ProjectSystem.dll"));
        File.WriteAllText(Path.Combine(folder, "plugin.json"), """
            {
              "id": "probe.impostor",
              "name": "probe.impostor",
              "version": "1.0.0",
              "provides": { "contracts": [ "ArxisStudio.ProjectSystem.dll" ] }
            }
            """);

        var impostor = Assert.Single(new PluginCatalog(_root).Scan());

        Assert.Contains("ArxisStudio.ProjectSystem", PluginContracts.EnsureLoaded(impostor, []));
    }

    /// <summary>Корень репозитория: тесты бегут из bin, файлы лежат выше.</summary>
    private static string Repository()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "ArxisStudio.slnx")))
            folder = folder.Parent;

        Assert.True(folder is not null, "не нашёл корень репозитория");

        return folder!.FullName;
    }
}
