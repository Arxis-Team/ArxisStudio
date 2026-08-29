using Arxis.Hello.Contracts;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Экспорты: плагин публикует реализацию — сосед берёт.
/// </summary>
/// <remarks>
/// Смысл существует только вместе с контрактными сборками: тип, которым
/// обмениваются, обязан быть одним на всех, иначе сосед не смог бы привести
/// взятый объект к своему интерфейсу.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioExportsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-exports-{Guid.NewGuid():N}");

    public StudioExportsTests() => Directory.CreateDirectory(_root);

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

    /// <summary>Опубликованное одним берётся другим.</summary>
    [Fact]
    public void What_one_publishes_another_takes()
    {
        var registry = new StudioExportRegistry();
        var publisher = new PluginExports(registry, Plugin("arxis.a"));
        var consumer = new PluginExports(registry, Plugin("arxis.b"));
        var greeter = new ProbeGreeter();

        Assert.True(publisher.Publish<IGreeter>(greeter));
        Assert.Same(greeter, consumer.Get<IGreeter>());
    }

    /// <summary>
    /// Занятый тип второму поставщику не отдаётся — и об этом сказано.
    /// </summary>
    /// <remarks>
    /// То же правило, что у рисовальщиков свойств: два хозяина на одно место —
    /// это не выбор, а гонка, и выиграл бы тот, кого раньше загрузили.
    /// </remarks>
    [Fact]
    public void A_taken_type_is_not_given_to_a_second_publisher()
    {
        var registry = new StudioExportRegistry();
        var conflicts = new List<string>();

        registry.Conflict += (_, message) => conflicts.Add(message);

        var first = new ProbeGreeter();

        Assert.True(new PluginExports(registry, Plugin("arxis.first")).Publish<IGreeter>(first));
        Assert.False(new PluginExports(registry, Plugin("arxis.second")).Publish<IGreeter>(new ProbeGreeter()));

        Assert.Same(first, registry.Get(typeof(IGreeter)));
        Assert.Contains(conflicts, message => message.Contains("arxis.first", StringComparison.Ordinal));
    }

    /// <summary>
    /// Своя повторная публикация — обновление, а не гонка.
    /// </summary>
    /// <remarks>
    /// Так поступает плагин, публикующий заново после перезагрузки: прежняя
    /// запись — его же, и молча отказать ему значило бы оставить соседям
    /// объект из выгруженной копии.
    /// </remarks>
    [Fact]
    public void Republishing_by_the_same_owner_is_an_update_not_a_race()
    {
        var registry = new StudioExportRegistry();
        var exports = new PluginExports(registry, Plugin("arxis.a"));
        var fresh = new ProbeGreeter();

        Assert.True(exports.Publish<IGreeter>(new ProbeGreeter()));
        Assert.True(exports.Publish<IGreeter>(fresh));
        Assert.Same(fresh, exports.Get<IGreeter>());
    }

    /// <summary>Выгрузка снимает публикации плагина, чужие остаются.</summary>
    [Fact]
    public void Unloading_removes_the_owner_publications_only()
    {
        var registry = new StudioExportRegistry();
        var going = new PluginExports(registry, Plugin("arxis.going"));
        var staying = new PluginExports(registry, Plugin("arxis.staying"));

        Assert.True(going.Publish<IGreeter>(new ProbeGreeter()));
        Assert.True(staying.Publish<IStudioProbe>(new ProbeGreeter()));

        registry.RemoveOwnedBy("arxis.going");

        Assert.Null(registry.Get(typeof(IGreeter)));
        Assert.NotNull(registry.Get(typeof(IStudioProbe)));
    }

    /// <summary>
    /// Реализация не того типа не публикуется.
    /// </summary>
    /// <remarks>
    /// Без проверки в ячейку ложится мусор: сосед получает null на попадании в
    /// словарь — неотличимо от «никто не публиковал», — а настоящему хозяину
    /// потом отказывают, называя виновником самозванца.
    /// </remarks>
    [Fact]
    public void An_implementation_of_the_wrong_type_is_not_published()
    {
        var registry = new StudioExportRegistry();
        var conflicts = new List<string>();

        registry.Conflict += (_, message) => conflicts.Add(message);

        Assert.False(registry.Publish(typeof(IGreeter), new object(), "arxis.a", "A"));
        Assert.Null(registry.Get(typeof(IGreeter)));
        Assert.Contains(conflicts, message => message.Contains("не является", StringComparison.Ordinal));
    }

    /// <summary>
    /// Тип из сборки плагина под экспорт не годится.
    /// </summary>
    /// <remarks>
    /// Его никто, кроме самого публикующего, не назовёт — сосед спрашивает
    /// свой тип и получает null без единого слова, — а запись держит контекст
    /// плагина и не даёт ему выгрузиться. Чаще всего так выходит нечаянно:
    /// <c>Publish(реализация)</c> выводит T из аргумента, и вместо интерфейса
    /// публикуется собственный класс.
    /// </remarks>
    [Fact]
    public void A_type_from_the_plugin_assembly_is_not_published()
    {
        var catalog = new PluginCatalog(_root);

        Assert.Null(catalog.InstallFromArchive(HelloArchive.Path).Error);

        using var studio = new TestHost();

        var loaded = Assert.Single(studio.Host.LoadStartup(catalog.Scan()), plugin => plugin.IsLoaded);

        // Тип из контекста плагина: он и есть тот, что публиковать нельзя.
        var own = loaded.Assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .First(type => type.Name == "HelloService");

        var conflicts = new List<string>();

        studio.Exports.Conflict += (_, message) => conflicts.Add(message);

        Assert.False(studio.Exports.Publish(own, Activator.CreateInstance(own)!, "arxis.hello", "Hello"));
        Assert.Contains(conflicts, message => message.Contains("в сборке плагина", StringComparison.Ordinal));
    }

    /// <summary>
    /// Сосед, чьей версии владелец не удовлетворён, не отдаёт и экспортов.
    /// </summary>
    /// <remarks>
    /// Служба соседей уже отвечает про такого «его нет». Разойдись два ответа —
    /// плагин, написанный под вторую версию, получил бы реализацию первой и
    /// упал бы с MissingMethodException внутри собственного кадра: гвард
    /// записал бы сбой на него и через три раза отключил невиновного.
    /// </remarks>
    [Fact]
    public void An_inactive_neighbour_gives_no_exports()
    {
        var registry = new StudioExportRegistry();

        Assert.True(new PluginExports(registry, Plugin("arxis.old")).Publish<IGreeter>(new ProbeGreeter()));

        var consumer = new PluginExports(registry, Plugin("arxis.picky"), new NobodyIsActive());

        Assert.Null(consumer.Get<IGreeter>());

        // А без службы соседей — как раньше: брать некому мерить.
        Assert.NotNull(new PluginExports(registry, Plugin("arxis.picky")).Get<IGreeter>());
    }

    /// <summary>Своё берётся всегда: сам себе зависимости не объявляют.</summary>
    [Fact]
    public void An_owner_always_sees_its_own_export()
    {
        var registry = new StudioExportRegistry();
        var exports = new PluginExports(registry, Plugin("arxis.self"), new NobodyIsActive());

        Assert.True(exports.Publish<IGreeter>(new ProbeGreeter()));
        Assert.NotNull(exports.Get<IGreeter>());
    }

    /// <summary>Служба соседей, для которой не активен никто.</summary>
    private sealed class NobodyIsActive : IStudioPlugins
    {
        public bool IsActive(string pluginId) => false;

        public string? Version(string pluginId) => null;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    /// <summary>Без реестра службы в контексте честно нет.</summary>
    [Fact]
    public void Without_a_registry_the_service_is_simply_absent()
    {
        var factory = new StudioContextFactory(new StudioLog(), new StudioCommands(), null);

        Assert.Null(factory.Create(Plugin("arxis.a")).GetService<IStudioExports>());
    }

    /// <summary>
    /// Пример целиком: Hello публикует, тест берёт своим типом.
    /// </summary>
    /// <remarks>
    /// Перезагрузка соседа обновляет публикацию: прежний объект снят вместе
    /// с выгрузкой, свежая копия опубликовала заново. Потому взятое и не
    /// держат подолгу.
    /// </remarks>
    [Fact]
    public void The_example_publishes_and_a_reload_republishes()
    {
        var catalog = new PluginCatalog(_root);

        Assert.Null(catalog.InstallFromArchive(HelloArchive.Path).Error);

        // Хост собирается так же, как в студии: с подпиской на уход плагина.
        // Без неё реестры держали бы объекты выгружаемого контекста, и тест
        // мерил бы не продукт, а свою самоделку.
        using var studio = new TestHost();
        var registry = studio.Exports;

        Start(studio.Host, catalog);

        // Прежний объект — только слабой ссылкой: сильная, оставшаяся в
        // кадре теста, держала бы контекст Hello, и проверка выгрузки
        // мерила бы сама себя.
        var before = TakeAndForget(registry);

        var installed = catalog.Scan().Single(plugin => plugin.Id == "arxis.hello");
        var cascade = studio.Host.Reload(["arxis.hello"], [installed]);

        Assert.True(cascade.Released["arxis.hello"], "прежняя копия не выгрузилась");

        var after = registry.Get(typeof(IGreeter));

        Assert.NotNull(after);
        Assert.NotSame(before.Target, after);
    }

    /// <summary>
    /// Проверяет публикацию примера и отпускает объект.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference TakeAndForget(StudioExportRegistry registry)
    {
        var published = registry.Get(typeof(IGreeter));

        Assert.NotNull(published);
        Assert.Contains("Friend", Assert.IsAssignableFrom<IGreeter>(published).Greet("Friend"));

        return new WeakReference(published);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Start(PluginHost host, PluginCatalog catalog) =>
        Assert.Single(host.LoadStartup(catalog.Scan()), loaded => loaded.IsLoaded);

    private static InstalledPlugin Plugin(string id) =>
        new(Path.Combine(Path.GetTempPath(), id),
            new PluginManifest { Id = id, Name = id },
            null,
            IsEnabled: true);


    /// <summary>Второй контрактный тип — чтобы проверять снятие по владельцу.</summary>
    private interface IStudioProbe;

    private sealed class ProbeGreeter : IGreeter, IStudioProbe
    {
        public string Greet(string name) => $"Привет, {name}!";
    }
}
