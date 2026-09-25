using System.Runtime.CompilerServices;
using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Плагин, заведший свои свойства и события Avalonia, называется до спуска — и только он.
/// </summary>
/// <remarks>
/// Avalonia помнит свойство и маршрутизируемое событие статическим реестром, и снять их у неё
/// нечем: <c>UnregisterByModule</c> чистит метаданные, а записи самого типа оставляет, у реестра
/// событий снятия нет вовсе. Такой плагин держится в памяти до перезапуска, и студия говорит это
/// заранее. Спросить она обязана, не приковав к памяти того, о ком спрашивает: открытый
/// <c>GetRegistered</c> прогоняет статические конструкторы и сам завёл бы свойства.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginPinTests : IDisposable
{
    private const string Id = "probe.pinned";

    private readonly string _root = TempFolder.Create("pin");

    public void Dispose()
    {
        TempFolder.Erase(_root);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Вопрос ничего не заводит сам, а заведённое называет владельцем.
    /// </summary>
    /// <remarks>
    /// Одна сборка на диске, две копии плагина из неё — сборка на диске дорога. Первую спрашивают,
    /// пока её код ничего не трогал: ответ пустой, и она выгружается — значит, вопрос не завёл её
    /// свойств. Вторая создаёт свой контрол и трогает своё событие: ответ называет обоих владельцев.
    /// </remarks>
    [AvaloniaFact]
    public void The_question_names_what_the_plugin_registered_and_registers_nothing_itself()
    {
        Emit();

        var first = AskedAndDropped();

        for (var attempt = 0; attempt < 10 && first.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(first.IsAlive, "вопрос «держит ли Avalonia плагин» сам завёл его свойства и приковал его к памяти");

        using var studio = new TestHost();

        var loaded = Assert.Single(studio.Host.LoadStartup(new PluginCatalog(_root).Scan()));

        Assert.True(loaded.IsLoaded, loaded.Error);

        var types = loaded.Assemblies.SelectMany(assembly => assembly.GetTypes()).ToList();

        Activator.CreateInstance(types.Single(type => type.Name == "Badge"));
        RuntimeHelpers.RunClassConstructor(types.Single(type => type.Name == "Signals").TypeHandle);

        var reason = studio.Host.Pinned(Id);

        Assert.True(reason is not null, "плагин со своим свойством и событием Avalonia не назван");
        Assert.Contains("Badge", reason, StringComparison.Ordinal);
        Assert.Contains("Signals", reason, StringComparison.Ordinal);

        // Сборка — главное в причине: у просмотрщика держит не его код, а его библиотека.
        Assert.StartsWith("Probe.Pinned", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Поднимает копию плагина, спрашивает о ней и снимает её.
    /// </summary>
    /// <remarks>
    /// Не встраивается — по той же причине, что <c>PluginHost.Retire</c>: запись о плагине в кадре
    /// вызывающего держала бы его контекст, и проверка выгрузки показывала бы только это.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference AskedAndDropped()
    {
        using var studio = new TestHost();

        var loaded = Assert.Single(studio.Host.LoadStartup(new PluginCatalog(_root).Scan()));

        Assert.True(loaded.IsLoaded, loaded.Error);
        Assert.Null(studio.Host.Pinned(Id));

        var context = new WeakReference(loaded.Context);

        Assert.True(studio.Host.Drop(Id));

        return context;
    }

    /// <summary>
    /// Кладёт в каталог плагин с контролом, у которого своё свойство, и классом со своим событием.
    /// </summary>
    /// <remarks>
    /// Контрол ставит своё свойство в конструкторе: инициализатор статического поля иначе не
    /// позвался бы вовсе, и свойство не завелось бы — проверка прошла бы, ничего не проверив.
    /// </remarks>
    private void Emit()
    {
        // Компилятор берёт ссылки из загруженного: библиотека контролов обязана быть в процессе.
        _ = typeof(AxUserControl);

        TestAssembly.EmitPlugin(_root, Id, $"Probe.Pinned{Guid.NewGuid():N}", """
            using ArxisStudio.Controls;
            using ArxisStudio.Sdk;
            using Avalonia;
            using Avalonia.Interactivity;

            namespace Probe;

            public sealed class PinnedPlugin : StudioPlugin
            {
            }

            public sealed class Badge : AxUserControl
            {
                public static readonly StyledProperty<int> CountProperty =
                    AvaloniaProperty.Register<Badge, int>(nameof(Count));

                public Badge() => Count = 1;

                public int Count
                {
                    get => GetValue(CountProperty);
                    set => SetValue(CountProperty, value);
                }
            }

            public static class Signals
            {
                public static readonly RoutedEvent<RoutedEventArgs> PingEvent =
                    RoutedEvent.Register<RoutedEventArgs>("Ping", RoutingStrategies.Bubble, typeof(Signals));
            }
            """);
    }
}
