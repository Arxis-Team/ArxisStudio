using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Xaml;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Данные элемента у службы XAML (контракт 1.2, запись 423): откуда они, какого типа, компилируются ли привязки — и
/// какими классами проекта их можно задать.
/// </summary>
/// <remarks>
/// Типы проекта — <see cref="DataProject"/>: каталог поколения знает собранное, а файлы кода — и то, что ещё не собрано.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class XamlDataTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static XamlElementPath Panel => XamlElementPath.Parse("/0");

    private static XamlElementPath Label => XamlElementPath.Parse("/0/0");

    /// <summary>
    /// Корень создаёт модель сам, панель берёт её часть выражением, а подпись наследует от панели: служба называет
    /// источник, элемент, который его написал, и тип данных сейчас.
    /// </summary>
    [AvaloniaFact]
    public async Task The_data_of_an_element_says_where_it_comes_from_and_what_it_is()
    {
        await using var studio = new XamlStudio();

        DataProject.Emit(studio);

        using var view = await ShowAsync(studio, "MainView.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="using:App.ViewModels">
              <UserControl.DataContext>
                <vm:MainViewModel />
              </UserControl.DataContext>
              <StackPanel DataContext="{Binding Customer}">
                <TextBlock Text="{Binding Name}" />
              </StackPanel>
            </UserControl>
            """);

        // Привязку данных к данным родителя Avalonia вычисляет в логическом дереве — там, где форма стоит в студии.
        var window = new Window { Content = view.Root };

        window.Show();

        await XamlStudio.UntilAsync(() => view.ObjectAt(Panel) is StackPanel { DataContext: not null }, "панель не получила данные");

        var root = await Describe(view, XamlElementPath.Root);

        Assert.Equal(XamlDataSource.Instance, root.Source);
        Assert.Equal(XamlElementPath.Root, root.SourceElement);
        Assert.Equal("<vm:MainViewModel />", root.SourceText);
        Assert.Equal("App.ViewModels.MainViewModel", root.SourceType?.FullName);

        var panel = await Describe(view, Panel);

        Assert.Equal(XamlDataSource.Expression, panel.Source);
        Assert.Equal("{Binding Customer}", panel.SourceText);
        Assert.Equal("App.ViewModels.Customer", panel.SourceType?.FullName);

        var label = await Describe(view, Label);

        Assert.Equal(XamlDataSource.Inherited, label.Source);
        Assert.Equal(Panel, label.SourceElement);
        Assert.Equal("App.ViewModels.Customer", label.PathType?.FullName);

        window.Content = null;
        window.Close();
    }

    /// <summary>
    /// Данные, поставленные кодом формы, данные для дизайна и сама форма своими данными различаются, а форма без
    /// данных их не получает ниоткуда.
    /// </summary>
    [AvaloniaFact]
    public async Task Data_from_code_design_data_the_form_itself_and_no_data_are_told_apart()
    {
        await using var studio = new XamlStudio();

        DataProject.Emit(studio);

        var code = studio.Write("Code.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="App.Views.SelfView">
              <TextBlock Text="{Binding Greeting}" />
            </UserControl>
            """);
        var design = studio.Write("Design.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:vm="using:App.ViewModels">
              <Design.DataContext>
                <vm:MainViewModel />
              </Design.DataContext>
              <TextBlock Text="{Binding Title}" />
            </UserControl>
            """);
        var self = studio.Write("Self.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         DataContext="{Binding $self}">
              <TextBlock />
            </UserControl>
            """);
        var none = studio.Write("None.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui">
              <TextBlock />
            </UserControl>
            """);

        await studio.OpenAsync();

        using (var view = await ShowAsync(studio, code))
        {
            var fromCode = await Describe(view, Panel);

            Assert.Equal(XamlDataSource.Code, fromCode.Source);
            Assert.Null(fromCode.SourceElement);
            Assert.Equal("App.Views.SelfView", fromCode.SourceType?.FullName);
        }

        using (var view = await ShowAsync(studio, design))
        {
            var forDesign = await Describe(view, Panel);

            Assert.Equal(XamlDataSource.Design, forDesign.Source);
            Assert.Equal(XamlElementPath.Root, forDesign.SourceElement);
            Assert.Equal("<vm:MainViewModel />", forDesign.SourceText);
            Assert.Equal("App.ViewModels.MainViewModel", forDesign.SourceType?.FullName);
        }

        using (var view = await ShowAsync(studio, self))
            Assert.Equal(XamlDataSource.Self, (await Describe(view, XamlElementPath.Root)).Source);

        using (var view = await ShowAsync(studio, none))
        {
            var nothing = await Describe(view, Panel);

            Assert.Equal(XamlDataSource.None, nothing.Source);
            Assert.Null(nothing.SourceType);
        }
    }

    /// <summary>
    /// Корень, поставленный хозяином в контрол с данными, наследует их, — но это данные хозяина, а не формы: служба
    /// говорит, что данных у формы нет, и типа хозяина не называет.
    /// </summary>
    [AvaloniaFact]
    public async Task What_the_root_inherits_from_its_host_is_not_the_forms_data()
    {
        await using var studio = new XamlStudio();

        using var view = await ShowAsync(studio, "MainView.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui">
              <TextBlock />
            </UserControl>
            """);

        var window = new Window { Content = new ContentControl { DataContext = "данные хозяина", Content = view.Root } };

        window.Show();

        await XamlStudio.UntilAsync(() => view.ObjectAt(Panel) is TextBlock { DataContext: string }, "форма не унаследовала данные хозяина");

        foreach (var element in new[] { XamlElementPath.Root, Panel })
        {
            var scope = await Describe(view, element);

            Assert.Equal(XamlDataSource.None, scope.Source);
            Assert.Null(scope.SourceType);
        }

        window.Content = null;
        window.Close();
    }

    /// <summary>
    /// Тип данных в области — написанный и разрешённый, — а компиляция привязок — ближайший <c>x:CompileBindings</c> или
    /// свойство проекта; без типа данных компилируемым привязкам его не хватит.
    /// </summary>
    [AvaloniaFact]
    public async Task The_data_type_in_scope_and_whether_bindings_compile_are_told()
    {
        await using var studio = new XamlStudio { CompiledBindings = true };

        DataProject.Emit(studio);

        var typed = studio.Write("Typed.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="using:App.ViewModels"
                         x:DataType="vm:MainViewModel">
              <StackPanel x:CompileBindings="False">
                <TextBlock />
              </StackPanel>
            </UserControl>
            """);
        var untyped = studio.Write("Untyped.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui">
              <TextBlock />
            </UserControl>
            """);

        await studio.OpenAsync();

        using (var view = await ShowAsync(studio, typed))
        {
            var root = await Describe(view, XamlElementPath.Root);

            Assert.Equal("vm:MainViewModel", root.WrittenDataType);
            Assert.Equal(XamlElementPath.Root, root.DataTypeElement);
            Assert.Equal("App.ViewModels.MainViewModel", root.DataType?.FullName);
            Assert.True(root.CompilesBindings, "проект компилирует привязки, а корень — нет");
            Assert.True(root.CompilesByProjectDefault, "свойство проекта не прочтено");
            Assert.False(root.NeedsDataType, "разрешённого типа данных не хватило");

            var panel = await Describe(view, Panel);

            Assert.False(panel.CompilesBindings, "x:CompileBindings=\"False\" не услышан");
            Assert.Equal(XamlElementPath.Root, panel.DataTypeElement);
            Assert.Equal("App.ViewModels.MainViewModel", panel.DataType?.FullName);
            Assert.False((await Describe(view, Label)).CompilesBindings, "x:CompileBindings предка не дошёл до потомка");
        }

        using (var view = await ShowAsync(studio, untyped))
        {
            var root = await Describe(view, XamlElementPath.Root);

            Assert.Null(root.WrittenDataType);
            Assert.True(root.NeedsDataType, "привязкам, которые компилируются, без типа данных — хватило");
        }
    }

    /// <summary>
    /// Классы для данных — собранные классы проекта с тем, что о них знает каталог, и объявленные файлами кода, но ещё
    /// не собранные; у собранного — файл, который его объявляет.
    /// </summary>
    [AvaloniaFact]
    public async Task Data_types_are_the_built_classes_and_those_the_code_declares()
    {
        await using var studio = new XamlStudio();

        DataProject.Emit(studio);

        var file = studio.Write("ViewModels/MainViewModel.cs", DataProject.Source);

        studio.Write("ViewModels/DraftViewModel.cs", DataProject.Draft);

        using var view = await ShowAsync(studio, "MainView.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui" />
            """);

        var types = (await view.GetDataTypesAsync(Token)).ToDictionary(static type => type.FullName);

        var model = types["App.ViewModels.MainViewModel"];

        Assert.True(model is { IsBuilt: true, CanCreate: true, IsData: true, Notifies: true, IsModel: true }, $"модель описана не так: {model}");
        Assert.Equal("using:App.ViewModels", model.XmlNamespace);
        Assert.Equal(file, Assert.Single(model.Files));

        Assert.True(types["App.ViewModels.Customer"] is { IsModel: true, Notifies: false }, "модель без уведомлений описана не так");
        Assert.True(types["App.ViewModels.Shout"] is { IsConverter: true, IsModel: false }, "конвертер назван моделью");
        Assert.True(types["App.Views.SelfView"] is { IsControl: true, IsModel: false }, "контрол назван моделью");

        var draft = types["App.ViewModels.DraftViewModel"];

        Assert.True(draft is { IsBuilt: false, CanCreate: true }, $"несобранный класс описан не так: {draft}");
    }

    /// <summary>Брошенный файл кода называет свои классы — собранные сведениями каталога, несобранные тем, что сказал текст.</summary>
    [AvaloniaFact]
    public async Task A_code_file_names_its_classes_built_or_not()
    {
        await using var studio = new XamlStudio();

        DataProject.Emit(studio);

        var models = studio.Write("ViewModels/MainViewModel.cs", DataProject.Source);
        var draft = studio.Write("ViewModels/DraftViewModel.cs", DataProject.Draft);

        using var view = await ShowAsync(studio, "MainView.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui" />
            """);

        var declared = await studio.Design.ClassesInAsync(models, Token);

        Assert.Equal(
            ["App.ViewModels.MainViewModel", "App.ViewModels.Customer", "App.ViewModels.Shout", "App.Views.SelfView"],
            declared.Select(static type => type.FullName));
        Assert.All(declared, static type => Assert.True(type.IsBuilt, $"{type.Name} собран, а назван несобранным"));

        var unbuilt = Assert.Single(await studio.Design.ClassesInAsync(draft, Token));

        Assert.Equal("App.ViewModels.DraftViewModel", unbuilt.FullName);
        Assert.False(unbuilt.IsBuilt, "несобранный класс назван собранным");
        Assert.Empty(await studio.Design.ClassesInAsync(studio.PathOf("MainView.axaml"), Token));
    }

    private static async Task<XamlDataScope> Describe(IXamlDesignView view, XamlElementPath element) =>
        await view.DescribeDataAsync(element, Token) ?? throw new InvalidOperationException($"служба не ответила о {element}");

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
