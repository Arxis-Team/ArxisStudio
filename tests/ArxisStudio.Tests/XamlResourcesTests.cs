using System.Globalization;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Ресурсы элемента у службы XAML (контракт 1.5, запись 427): что он может назвать по ключу — свои, предков и приложения
/// формы, — и найдётся ли названное. Правило досягаемости проверено пробой настоящего приложения Avalonia 12.1.1.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class XamlResourcesTests
{
    private const string Application = """
        <Application xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Application.Resources>
            <ResourceDictionary>
              <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="/Colors.axaml" />
              </ResourceDictionary.MergedDictionaries>
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <SolidColorBrush x:Key="Surface" Color="White" />
                </ResourceDictionary>
                <ResourceDictionary x:Key="Dark">
                  <SolidColorBrush x:Key="Surface" Color="Black" />
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
              <SolidColorBrush x:Key="AppBrush" Color="#FF336699" />
            </ResourceDictionary>
          </Application.Resources>
        </Application>
        """;

    private const string Colors = """
        <ResourceDictionary xmlns="https://github.com/avaloniaui"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Color x:Key="Brand">#FF2563EB</Color>
        </ResourceDictionary>
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Элемент называет свои ресурсы, предков и приложения формы — со словарём, который оно включает, и ключом одной темы,
    /// — ближайший первым; соседа не называет. У каждого — где объявлен, значение и подходит ли оно члену.
    /// </summary>
    [AvaloniaFact]
    public async Task An_element_reaches_its_own_its_ancestors_and_its_applications_resources()
    {
        await using var studio = new XamlStudio();

        var application = studio.Write("App.axaml", Application);
        var colors = studio.Write("Colors.axaml", Colors);

        using var view = await ShowAsync(studio, "MainView.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <UserControl.Resources>
                <SolidColorBrush x:Key="FormBrush" Color="Red" />
              </UserControl.Resources>
              <StackPanel>
                <Border>
                  <Border.Resources>
                    <x:Double x:Key="Hidden">1</x:Double>
                  </Border.Resources>
                </Border>
                <Border>
                  <Border.Resources>
                    <Thickness x:Key="Own">4</Thickness>
                  </Border.Resources>
                </Border>
              </StackPanel>
            </UserControl>
            """);

        await XamlStudio.UntilAsync(() => view.Application is not null, "приложение формы не встало");

        var resources = await view.GetResourcesAsync(XamlElementPath.Parse("/0/1"), "Background", Token);

        Assert.Equal(
            [
                ("Own", XamlResourceScope.Element), ("FormBrush", XamlResourceScope.Ancestor), ("Surface", XamlResourceScope.Application),
                ("AppBrush", XamlResourceScope.Application), ("Brand", XamlResourceScope.Application),
            ],
            resources.Select(static resource => (resource.Key, resource.Scope)));

        var own = resources[0];

        Assert.Equal("Thickness", own.TypeName);
        Assert.False(own.Fits, "толщина подошла кисти");
        Assert.Equal("Own", Key(view.Document.Syntax, own.Element));

        var brush = resources.Single(static resource => resource.Key == "AppBrush");

        Assert.True(brush.Fits);
        Assert.Equal(Color.Parse("#FF336699"), brush.Sample);
        Assert.Equal(application, brush.File);
        Assert.Equal("AppBrush", File.ReadAllText(application.Value).Substring(brush.KeySpan!.Value.Start, brush.KeySpan.Value.Length));

        var brand = resources.Single(static resource => resource.Key == "Brand");

        Assert.True(brand.Fits, "цвет не подошёл кисти");
        Assert.Equal(colors, brand.File);

        // Ключ одной темы: значение — у темы, а статическая ссылка из UserControl его не найдёт.
        var surface = resources.Single(static resource => resource.Key == "Surface");

        Assert.True(surface.IsThemed);
        Assert.False(surface.StaticReachable, "статическая ссылка вне окна нашла ключ одной темы");
        Assert.True(own.StaticReachable && brush.StaticReachable && brand.StaticReachable);
    }

    /// <summary>
    /// Ключ ищется там же, где его найдёт форма: найденный называет, где объявлен; пропавший — сломан; ключ одной темы
    /// статической ссылкой вне окна — сломан, а в окне найден, но за темой не идёт.
    /// </summary>
    [AvaloniaFact]
    public async Task A_resource_key_is_found_where_the_element_reaches_or_is_broken()
    {
        await using var studio = new XamlStudio();

        studio.Write("App.axaml", Application);
        studio.Write("Colors.axaml", Colors);

        using (var view = await ShowAsync(studio, "MainView.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <UserControl.Resources>
                <SolidColorBrush x:Key="FormBrush" Color="Red" />
              </UserControl.Resources>
              <StackPanel>
                <Border Background="{DynamicResource AppBrush}" BorderBrush="{StaticResource Nope}" Tag="{StaticResource Surface}" />
                <Border Background="{StaticResource FormBrush}" BorderBrush="{DynamicResource Surface}" />
              </StackPanel>
            </UserControl>
            """))
        {
            await XamlStudio.UntilAsync(() => view.Application is not null, "приложение формы не встало");

            var first = await view.CheckExpressionsAsync(XamlElementPath.Parse("/0/0"), Token);

            Assert.Equal(
                [XamlExpressionState.Resolved, XamlExpressionState.Broken, XamlExpressionState.Broken],
                first.Select(static check => check.State));
            Assert.Equal(Words(studio, "module.xaml.resource.application") + " · SolidColorBrush", first[0].Message);
            Assert.Equal(Format(studio, "module.xaml.resource.missing", "Nope"), first[1].Message);
            Assert.Equal(Format(studio, "module.xaml.resource.themedStatic", "Surface"), first[2].Message);
            Assert.All(first, static check => Assert.False(check.BreaksBuild, "пропавший ключ сборку не останавливает"));

            var second = await view.CheckExpressionsAsync(XamlElementPath.Parse("/0/1"), Token);

            Assert.Equal([XamlExpressionState.Resolved, XamlExpressionState.Resolved], second.Select(static check => check.State));
            Assert.Equal(Format(studio, "module.xaml.resource.ancestor", "UserControl") + " · SolidColorBrush", second[0].Message);
        }

        // В окне тема известна, пока оно строится: статическая ссылка находит ключ одной темы, но застывает на ней.
        using var window = await ShowAsync(studio, "MainWindow.axaml", """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Border Background="{StaticResource Surface}" />
            </Window>
            """);

        await XamlStudio.UntilAsync(() => window.Application is not null, "приложение формы не встало");

        var frozen = Assert.Single(await window.CheckExpressionsAsync(XamlElementPath.Parse("/0"), Token));

        Assert.Equal(XamlExpressionState.Resolved, frozen.State);
        Assert.Equal(Format(studio, "module.xaml.resource.frozen", Words(studio, "module.xaml.resource.application") + " · SolidColorBrush"), frozen.Message);

        var themed = (await window.GetResourcesAsync(XamlElementPath.Parse("/0"), "Background", Token)).Single(static resource => resource.Key == "Surface");

        Assert.True(themed.StaticReachable, "в окне статическая ссылка не нашла бы ключ одной темы");
    }

    /// <summary>Ключ, который объявляет элемент по пути.</summary>
    private static string? Key(XamlDocument document, XamlElementPath? path) =>
        path?.Resolve(document)?.GetDirective(XamlDirectives.Key);

    private static IStudioStrings Strings(XamlStudio studio) =>
        studio.XamlModule.Studio?.Strings ?? throw new InvalidOperationException("у модуля нет словаря");

    private static string Words(XamlStudio studio, string key) => Strings(studio)[key];

    private static string Format(XamlStudio studio, string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Strings(studio)[key], arguments);

    /// <summary>Кладёт форму, открывает решение и показывает её.</summary>
    private static async Task<IXamlDesignView> ShowAsync(XamlStudio studio, string name, string form)
    {
        var path = studio.Write(name, form);

        await studio.OpenAsync();

        var handle = await studio.Documents.OpenAsync(path, Token);

        return await handle.ShowAsync(null, Token);
    }
}
