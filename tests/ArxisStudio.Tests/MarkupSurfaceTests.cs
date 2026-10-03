using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Sdk;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Синтаксис разметки — общие сборки студии, и их поверхность закреплена.
/// </summary>
/// <remarks>
/// <para>
/// <c>ArxisStudio.Markup</c> и <c>ArxisStudio.Markup.Xaml</c> плагин видит напрямую: документ, его
/// редактор, путь элемента, текст и диагностики приходят через контракт службы XAML. Поэтому они одни
/// на всех, как модель проектов, и их поверхность — обещание, которое сдвиг указателя подмодуля не
/// вправе нарушить молча.
/// </para>
/// <para>
/// Загрузчик <c>ArxisStudio.Markup.Xaml.Loader</c> и адаптер <c>ArxisStudio.ProjectSystem.Markup.Xaml</c>
/// общими не стали: загрузчик строит объекты поколения типов проекта, адаптер держит поколения, а
/// поверхность обоих велика и ещё меняется. Их держит служба XAML.
/// </para>
/// </remarks>
public class MarkupSurfaceTests
{
    /// <summary>Поверхность ядра Markup двигается только вместе с номером SDK.</summary>
    [Fact]
    public void The_public_surface_of_the_markup_core_is_the_recorded_one() =>
        PublicSurface.AssertVersioned(
            Baseline("ArxisStudio.Markup.txt"),
            PublicSurface.Describe(typeof(SourceText).Assembly),
            StudioSdk.Version);

    /// <summary>Поверхность синтаксиса XAML двигается только вместе с номером SDK.</summary>
    [Fact]
    public void The_public_surface_of_the_xaml_syntax_is_the_recorded_one() =>
        PublicSurface.AssertVersioned(
            Baseline("ArxisStudio.Markup.Xaml.txt"),
            PublicSurface.Describe(typeof(XamlDocument).Assembly),
            StudioSdk.Version);

    /// <summary>
    /// Синтаксис ссылается только на рантайм и на себя.
    /// </summary>
    /// <remarks>
    /// Общая сборка тащит свои ссылки в общий контекст каждому плагину. Markup обещает не знать Avalonia
    /// ниже загрузчика и держит это своими тестами, а студия проверяет обещание там, где оно ей стоит: на
    /// собранной сборке.
    /// </remarks>
    [Fact]
    public void The_markup_syntax_references_nothing_but_the_runtime_and_itself()
    {
        Assert.Empty(Foreign(typeof(SourceText).Assembly, []));
        Assert.Empty(Foreign(typeof(XamlDocument).Assembly, ["ArxisStudio.Markup"]));
    }

    /// <summary>
    /// Идентичность связывания синтаксиса записана и не двигается сама.
    /// </summary>
    /// <remarks>
    /// Плагин запоминает версию сборки, против которой собран, а основной контекст отдаёт ему ту, что
    /// лежит в студии: младшую, чем у плагина, он не примет. Сдвиг в подмодуле — повод решить за плагины,
    /// а не заметить после установки.
    /// </remarks>
    [Fact]
    public void The_binding_identity_of_the_markup_syntax_does_not_move_by_itself()
    {
        Assert.Equal(new Version(0, 2, 0, 0), typeof(SourceText).Assembly.GetName().Version);
        Assert.Equal(new Version(0, 2, 0, 0), typeof(XamlDocument).Assembly.GetName().Version);
    }

    /// <summary>Синтаксис общий точными именами, а загрузчик и адаптер — свои у службы XAML.</summary>
    [Fact]
    public void The_loader_and_the_adapter_stay_with_the_xaml_service()
    {
        var pack = SharedAssemblies.PackPattern();

        foreach (var shared in new[] { "ArxisStudio.Markup", "ArxisStudio.Markup.Xaml" })
        {
            Assert.True(SharedAssemblies.IsShared(shared), $"{shared} не общая");
            Assert.True(pack.IsMatch(shared), $"упаковка положит {shared} в пакет плагина");
        }

        foreach (var own in new[] { "ArxisStudio.Markup.Xaml.Loader", "ArxisStudio.ProjectSystem.Markup.Xaml" })
        {
            Assert.False(SharedAssemblies.IsShared(own), $"{own} объявлена общей");
            Assert.False(pack.IsMatch(own), $"упаковка не положит {own} в пакет плагина");
        }
    }

    private static List<string> Foreign(System.Reflection.Assembly assembly, string[] allowed) =>
    [
        .. assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !(name == "System" || name.StartsWith("System.", StringComparison.Ordinal)
                || name is "netstandard" or "mscorlib"
                || allowed.Contains(name))),
    ];

    private static string Baseline(string file) => Repository.Path("tests", "ArxisStudio.Tests", "Surfaces", file);
}
