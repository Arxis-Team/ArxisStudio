using System.Globalization;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Выражения элемента у службы XAML (контракт 1.3, запись 424): какие они — по одному тексту, — и работают ли — по
/// тому, что документ объявляет.
/// </summary>
/// <remarks>Типы проекта — <see cref="DataProject"/>.</remarks>
[Collection(StudioStateCollection.Name)]
public class XamlExpressionChecksTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Вид и короткая запись — по тексту, без показа: путь с источником, ключ, член, свойство шаблона.</summary>
    [Theory]
    [InlineData("{Binding Customer.Name}", XamlExpressionKind.Binding, "Customer.Name")]
    [InlineData("{CompiledBinding Path=Title}", XamlExpressionKind.Binding, "Title")]
    [InlineData("{Binding #slider.Value}", XamlExpressionKind.Binding, "#slider.Value")]
    [InlineData("{Binding Value, ElementName=slider}", XamlExpressionKind.Binding, "#slider.Value")]
    [InlineData("{Binding Title, RelativeSource={RelativeSource AncestorType=Window}}", XamlExpressionKind.Binding, "$parent[Window].Title")]
    [InlineData("{Binding Tag, RelativeSource={RelativeSource AncestorType=Border, AncestorLevel=2}}", XamlExpressionKind.Binding, "$parent[Border;1].Tag")]
    [InlineData("{Binding Width, RelativeSource={RelativeSource Self}}", XamlExpressionKind.Binding, "$self.Width")]
    [InlineData("{Binding}", XamlExpressionKind.Binding, "{Binding}")]
    [InlineData("{DynamicResource Accent}", XamlExpressionKind.Resource, "Accent")]
    [InlineData("{StaticResource ResourceKey=Fill}", XamlExpressionKind.Resource, "Fill")]
    [InlineData("{x:Static local:Limits.Max}", XamlExpressionKind.Static, "local:Limits.Max")]
    [InlineData("{TemplateBinding Background}", XamlExpressionKind.Template, "Background")]
    [InlineData("{x:Null}", XamlExpressionKind.Other, "{x:Null}")]
    public void An_expression_is_told_and_summed_up_by_its_text(string written, XamlExpressionKind kind, string summary)
    {
        Assert.Equal(kind, XamlExpressions.Classify(written));
        Assert.Equal(summary, XamlExpressions.Summarize(written));
    }

    /// <summary>Текст, экранированный <c>{}</c>, и простое значение — не выражения.</summary>
    [Theory]
    [InlineData("{}{Binding Name}")]
    [InlineData("Привет")]
    [InlineData("")]
    [InlineData(null)]
    public void Text_that_is_not_an_expression_has_no_kind(string? written)
    {
        Assert.Null(XamlExpressions.Classify(written));
        Assert.Null(XamlExpressions.Summarize(written));
    }

    /// <summary>
    /// Привязка к данным читается по их типу, к элементу — по имени в документе, а под элементом, отдавшим содержимому
    /// другие данные, — по ним: найденное называет, откуда и куда пришёл путь, сломанное — почему, а ключ ресурса,
    /// которого нет, — сломан.
    /// </summary>
    [AvaloniaFact]
    public async Task Each_expression_of_an_element_is_checked_against_what_the_document_declares()
    {
        await using var studio = new XamlStudio();

        DataProject.Emit(studio);

        using var view = await ShowAsync(studio, "MainView.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="using:App.ViewModels"
                         x:DataType="vm:MainViewModel">
              <UserControl.DataContext>
                <vm:MainViewModel />
              </UserControl.DataContext>
              <StackPanel>
                <TextBlock x:Name="first" Text="{Binding Title}" Tag="{Binding Nmae}" Foreground="{DynamicResource Accent}" />
                <TextBlock Text="{Binding #first.Text}" Tag="{Binding #nobody.Text}" />
                <StackPanel DataContext="{Binding Customer}">
                  <TextBlock Text="{Binding Name}" />
                </StackPanel>
              </StackPanel>
            </UserControl>
            """);

        // Привязку данных к данным родителя Avalonia вычисляет в логическом дереве — там, где форма стоит в студии.
        var window = new Window { Content = view.Root };

        window.Show();

        await XamlStudio.UntilAsync(() => view.ObjectAt(XamlElementPath.Parse("/0/2")) is StackPanel { DataContext: not null }, "панель не получила данные");

        var first = await Checks(view, "/0/0");

        Assert.Equal(["Text", "Tag", "Foreground"], first.Select(static check => check.Member));

        var title = first[0];

        Assert.Equal((XamlExpressionKind.Binding, XamlExpressionState.Resolved), (title.Kind, title.State));
        Assert.Equal("App.ViewModels.MainViewModel", title.SourceType?.FullName);
        Assert.Equal("System.String", title.ResultType?.FullName);
        Assert.Null(title.Message);

        var typo = first[1];

        Assert.Equal(XamlExpressionState.Broken, typo.State);
        Assert.Equal(Format(studio, "module.xaml.binding.noMember", "MainViewModel", "Nmae"), typo.Message);
        Assert.False(typo.BreaksBuild, "проект привязки не компилирует, а сломанная названа ломающей сборку");

        // Ключа нет ни у формы, ни у приложения — его у формы и нет: ресурс сломан (контракт 1.5, запись 427).
        Assert.Equal((XamlExpressionKind.Resource, XamlExpressionState.Broken), (first[2].Kind, first[2].State));
        Assert.Equal(Format(studio, "module.xaml.resource.missing", "Accent"), first[2].Message);

        var named = await Checks(view, "/0/1");

        Assert.Equal(XamlExpressionState.Resolved, named[0].State);
        Assert.Equal("Avalonia.Controls.TextBlock", named[0].SourceType?.FullName);
        Assert.Equal(XamlExpressionState.Broken, named[1].State);
        Assert.Equal(Format(studio, "module.xaml.binding.noElement", "nobody"), named[1].Message);

        // Данные, отданные содержимому, — то, что прочтёт некомпилируемая привязка, а не тип данных корня.
        var handed = Assert.Single(await Checks(view, "/0/2"));

        Assert.Equal(XamlExpressionState.Resolved, handed.State);
        Assert.Equal("App.ViewModels.Customer", handed.ResultType?.FullName);

        var inner = Assert.Single(await Checks(view, "/0/2/0"));

        Assert.Equal(XamlExpressionState.Resolved, inner.State);
        Assert.Equal("App.ViewModels.Customer", inner.SourceType?.FullName);
        Assert.Equal("App.ViewModels.Customer", (await view.DescribeDataAsync(XamlElementPath.Parse("/0/2/0"), Token))?.PathType?.FullName);

        Assert.Empty(await Checks(view, "/0"));
        Assert.Empty(await Checks(view, "/7"));

        window.Content = null;
        window.Close();
    }

    /// <summary>
    /// Проект компилирует привязки, а типа данных нет: привязка к данным сломана и остановит сборку, привязке к элементу
    /// тип данных не нужен, а то, что известно только работающей форме, не проверено.
    /// </summary>
    [AvaloniaFact]
    public async Task A_compiled_binding_without_a_data_type_is_broken_and_stops_the_build()
    {
        await using var studio = new XamlStudio { CompiledBindings = true };

        using var view = await ShowAsync(studio, "MainView.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel>
                <TextBlock x:Name="first" Text="{Binding Title}" />
                <TextBlock Text="{Binding #first.Text}" Tag="{ReflectionBinding Title}" />
              </StackPanel>
            </UserControl>
            """);

        var data = Assert.Single(await Checks(view, "/0/0"));

        Assert.Equal(XamlExpressionState.Broken, data.State);
        Assert.True(data.BreaksBuild, "сборка, компилирующая привязку без типа данных, на ней не остановится");
        Assert.Equal(Format(studio, "module.xaml.binding.breaksBuild", Strings(studio)["module.xaml.binding.noDataType"]), data.Message);

        var other = await Checks(view, "/0/1");

        Assert.Equal(XamlExpressionState.Resolved, other[0].State);
        Assert.False(other[0].BreaksBuild, "привязке к элементу понадобился тип данных");
        Assert.Equal(XamlExpressionState.Unchecked, other[1].State);
        Assert.Equal(Strings(studio)["module.xaml.binding.unchecked"], other[1].Message);
    }

    /// <summary>
    /// Черновик проверяется тем же правилом, что написанное, а путь подсказывает, что назвать дальше: члены данных,
    /// элемента и шаг вглубь — с отметкой, подходит ли значение члену; где типа данных нет, члены берутся у данных показа.
    /// Режим привязки по умолчанию — у сведений о члене, и у свойства CLR его нет.
    /// </summary>
    [AvaloniaFact]
    public async Task A_draft_is_checked_and_its_path_suggests_what_comes_next()
    {
        await using var studio = new XamlStudio { CompiledBindings = true };

        DataProject.Emit(studio);

        var typed = studio.Write("Typed.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="using:App.ViewModels"
                         x:DataType="vm:MainViewModel">
              <UserControl.DataContext>
                <vm:MainViewModel />
              </UserControl.DataContext>
              <StackPanel>
                <Slider x:Name="zoom" />
                <TextBlock />
              </StackPanel>
            </UserControl>
            """);
        var untyped = studio.Write("Untyped.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:vm="using:App.ViewModels">
              <Design.DataContext>
                <vm:MainViewModel />
              </Design.DataContext>
              <TextBlock />
            </UserControl>
            """);

        await studio.OpenAsync();

        var label = XamlElementPath.Parse("/0/1");

        using (var view = await ShowAsync(studio, typed))
        {
            var data = await view.GetBindableMembersAsync(label, "Text", "{Binding}", Token);

            Assert.Equal("App.ViewModels.MainViewModel", data?.Type.FullName);

            var customer = Assert.Single(data!.Members, static member => member.Name == "Customer");

            Assert.True(customer is { HasMembers: true, Fits: true }, "к тексту подходит всё, а в модель путь шагает дальше");
            Assert.True(Assert.Single(data.Members, static member => member.Name == "Title") is { HasMembers: false, CanWrite: false });

            var deeper = await view.GetBindableMembersAsync(label, "Text", "{Binding Customer}", Token);

            Assert.Equal(["Name"], deeper!.Members.Select(static member => member.Name));

            // К флажку подходит логическое, а число — нет.
            var zoom = await view.GetBindableMembersAsync(label, "IsEnabled", "{Binding #zoom}", Token);

            Assert.True(Assert.Single(zoom!.Members, static member => member.Name == "IsEnabled").Fits);
            Assert.False(Assert.Single(zoom.Members, static member => member.Name == "Value").Fits);

            var typo = await view.CheckExpressionAsync(label, "Text", "{Binding Customer.Nmae}", Token);

            Assert.True(typo is { State: XamlExpressionState.Broken, BreaksBuild: true }, $"сломанный черновик не назван сломанным: {typo}");
            Assert.Equal(XamlExpressionState.Resolved, (await view.CheckExpressionAsync(label, "Text", "{Binding #zoom.Value}", Token))?.State);
            Assert.Null(await view.CheckExpressionAsync(label, "Text", "Привет", Token));

            Assert.Equal("TwoWay", view.GetMember(XamlElementPath.Parse("/0/0"), "Value")?.DefaultBindingMode);
            Assert.Equal("OneWay", view.GetMember(label, "Text")?.DefaultBindingMode);

            // Свойство CLR — не свойство Avalonia: привязку на него не поставить, и режима у него нет.
            var clr = view.GetMember(label, "Resources");

            Assert.NotNull(clr);
            Assert.Null(clr.DefaultBindingMode);
        }

        using (var view = await ShowAsync(studio, untyped))
        {
            var label2 = XamlElementPath.Parse("/0");

            // Проект компилирует привязки, а x:DataType нет: путь подсказывают данные показа, а проверка говорит, что
            // сборке нужен тип.
            Assert.Equal("App.ViewModels.MainViewModel", (await view.GetBindableMembersAsync(label2, "Text", "{Binding}", Token))?.Type.FullName);
            Assert.True((await view.CheckExpressionAsync(label2, "Text", "{Binding Title}", Token)) is { State: XamlExpressionState.Broken, BreaksBuild: true });
        }
    }

    private static async Task<IReadOnlyList<XamlExpressionCheck>> Checks(IXamlDesignView view, string path) =>
        await view.CheckExpressionsAsync(XamlElementPath.Parse(path), Token);

    private static IStudioStrings Strings(XamlStudio studio) =>
        studio.XamlModule.Studio?.Strings ?? throw new InvalidOperationException("у модуля нет словаря");

    private static string Format(XamlStudio studio, string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Strings(studio)[key], arguments);

    /// <summary>Кладёт форму, открывает решение и показывает её.</summary>
    private static async Task<IXamlDesignView> ShowAsync(XamlStudio studio, string name, string form)
    {
        var path = studio.Write(name, form);

        await studio.OpenAsync();

        return await ShowAsync(studio, path);
    }

    /// <summary>Показывает форму решения, которое уже открыто.</summary>
    private static async Task<IXamlDesignView> ShowAsync(XamlStudio studio, CanonicalPath path)
    {
        var handle = await studio.Documents.OpenAsync(path, Token);

        return await handle.ShowAsync(null, Token);
    }
}
