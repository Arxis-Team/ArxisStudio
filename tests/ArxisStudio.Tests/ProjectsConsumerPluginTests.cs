using System.Runtime.CompilerServices;
using ArxisStudio.Extensibility;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Службу проектов берёт настоящий плагин — папкой на диске, в своём контексте загрузки.
/// </summary>
/// <remarks>
/// Ради этого служба и сделана так, как сделана: работа с проектами проверяется той дорогой, какой
/// её получит чужой плагин. Сборка плагина компилируется здесь же и кладётся на диск вместе с
/// копиями общих сборок — ровно тем, что оставляет в <c>bin</c> неосторожная ссылка.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectsConsumerPluginTests : IDisposable
{
    private const string SeenService = "arxis.tests.projects.service";
    private const string SeenSnapshot = "arxis.tests.projects.snapshot";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-projects-consumer-{Guid.NewGuid():N}");

    public ProjectsConsumerPluginTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        AppDomain.CurrentDomain.SetData(SeenService, null);
        AppDomain.CurrentDomain.SetData(SeenSnapshot, null);

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Плагин видит ту же службу и тот же снимок — даже с копиями контракта и ядра в своём bin.
    /// </summary>
    [Fact]
    public async Task A_plugin_on_disk_sees_the_same_service_and_snapshot_even_with_copies_in_its_bin()
    {
        using var studio = new ProjectsStudio(rethrow: true);

        Install("probe.tree", TreeSource);

        var loaded = Assert.Single(studio.Host.LoadStartup(new PluginCatalog(_root).Scan()));

        Assert.True(loaded.IsLoaded, loaded.Error);
        Assert.NotNull(loaded.Context);
        Assert.Same(studio.Projects, AppDomain.CurrentDomain.GetData(SeenService));

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), TestContext.Current.CancellationToken);
        await studio.Thread.IdleAsync();

        var snapshot = AppDomain.CurrentDomain.GetData(SeenSnapshot);

        Assert.IsType<SolutionSnapshot>(snapshot);
        Assert.Same(studio.Projects.Current, snapshot);
    }

    /// <summary>
    /// Упавший обработчик плагина приписан плагину, а не модулю, в чьём цикле он упал.
    /// </summary>
    /// <remarks>
    /// Проверяется продуктовая дорога: исключение брошено заново в потоке интерфейса, и
    /// виновного ищет тот же разбор стека, что у студии.
    /// </remarks>
    [Fact]
    public async Task A_handler_that_throws_is_blamed_on_its_plugin_not_on_the_module()
    {
        using var studio = new ProjectsStudio(rethrow: true);

        Install("probe.tree", TreeSource);

        Assert.True(Assert.Single(studio.Host.LoadStartup(new PluginCatalog(_root).Scan())).IsLoaded);

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), TestContext.Current.CancellationToken);
        await studio.Thread.IdleAsync();

        Assert.Equal("probe.tree", Blamed(studio));
    }

    /// <summary>
    /// Плагин, забывший отписаться, всё равно выгружается.
    /// </summary>
    /// <remarks>
    /// Делегат подписки держит сборку плагина. Не сними служба его при выгрузке контекста, прежняя
    /// копия осталась бы в памяти навсегда, а доставка звала бы код мертвеца.
    /// </remarks>
    [Fact]
    public void A_plugin_that_forgot_to_unsubscribe_still_unloads()
    {
        using var studio = new ProjectsStudio();

        var context = SubscribedAndDropped(studio);

        for (var attempt = 0; attempt < 10 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(context.IsAlive, "подписка плагина держит его контекст: служба не сняла её при выгрузке");
    }

    /// <summary>Кого студия назовёт виновным в упавшем обработчике.</summary>
    /// <remarks>
    /// Отдельным методом: исключение держит стек с кадрами сборки плагина, и ему нечего делать в
    /// кадре теста дольше проверки.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? Blamed(ProjectsStudio studio) =>
        PluginHost.Blame(Assert.Single(studio.Thread.Crashes), studio.Host.Loaded)?.Installed.Id;

    /// <summary>Ставит плагин, подписанный и не отписывающийся, и снимает его.</summary>
    /// <remarks>
    /// Не встраиваемый, как у выгрузки в <c>PluginTeardownTests</c>: запись плагина, оставшаяся в
    /// кадре вызывающего, держала бы контекст сама.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference SubscribedAndDropped(ProjectsStudio studio)
    {
        Install("probe.forgetful", ForgetfulSource);

        var loaded = Assert.Single(studio.Host.LoadStartup(new PluginCatalog(_root).Scan()));

        Assert.True(loaded.IsLoaded, loaded.Error);

        var context = new WeakReference(loaded.Context);

        Assert.True(studio.Host.Drop("probe.forgetful"));

        return context;
    }

    /// <summary>Кладёт плагин папкой: сборка, копии общих сборок рядом и манифест.</summary>
    private void Install(string id, string source)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, id)).FullName;
        var bin = Directory.CreateDirectory(Path.Combine(folder, "bin")).FullName;
        var name = $"Probe.Projects{Guid.NewGuid():N}";

        // Компилятор берёт ссылки из загруженного: контракт и ядро обязаны быть в процессе.
        _ = typeof(IStudioProjects);
        _ = typeof(SolutionSnapshot);

        TestAssembly.EmitFile(Path.Combine(bin, name + ".dll"), name, source);

        File.Copy(typeof(IStudioProjects).Assembly.Location, Path.Combine(bin, "ArxisStudio.Projects.Contracts.dll"));
        File.Copy(typeof(SolutionSnapshot).Assembly.Location, Path.Combine(bin, "ArxisStudio.ProjectSystem.dll"));

        File.WriteAllText(Path.Combine(folder, "plugin.json"), $$"""
            {
              "id": "{{id}}",
              "name": "{{id}}",
              "version": "1.0.0",
              "entry": "bin/{{name}}.dll",
              "dependencies": [ { "id": "arxis.projects", "min": "1.0" } ],
              "activation": [ "onStartup" ]
            }
            """);
    }

    private static string TreeSource => $$"""
        using System;
        using ArxisStudio.Projects;
        using ArxisStudio.ProjectSystem;
        using ArxisStudio.Sdk;

        namespace Probe;

        public sealed class Tree : StudioPlugin
        {
            public override void Activate(IStudioContext context)
            {
                var projects = context.Projects();

                AppDomain.CurrentDomain.SetData("{{SeenService}}", projects);

                if (projects != null)
                    projects.Changed += OnChanged;
            }

            private static void OnChanged(object sender, ProjectsChangedEventArgs change)
            {
                if (change.Current.Snapshot is SolutionSnapshot snapshot)
                {
                    AppDomain.CurrentDomain.SetData("{{SeenSnapshot}}", snapshot);
                    throw new InvalidOperationException("дерево решения сломалось");
                }
            }
        }
        """;

    private const string ForgetfulSource = """
        using ArxisStudio.Projects;
        using ArxisStudio.Sdk;

        namespace Probe;

        public sealed class Forgetful : StudioPlugin
        {
            private int _seen;

            public override void Activate(IStudioContext context)
            {
                var projects = context.Projects();

                if (projects != null)
                    projects.Changed += (sender, change) => _seen++;
            }
        }
        """;
}
