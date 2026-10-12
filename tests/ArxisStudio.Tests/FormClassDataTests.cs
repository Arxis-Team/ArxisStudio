using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Xaml;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Класс формы (<c>x:Class</c>) как её собственные данные: что работает в дизайнере и что студию не роняет
/// (запись 422).
/// </summary>
/// <remarks>
/// <para>
/// Дизайнер строит корень экземпляром класса формы и зовёт его конструктор, поэтому <c>DataContext = this</c> в
/// коде и <c>DataContext="{Binding $self}"</c> на корне дают привязкам свойства самой формы — как в приложении.
/// </para>
/// <para>
/// Экземпляр класса формы в её же <c>DataContext</c> строит форму внутри её постройки без конца: компилятор Avalonia
/// такой проект собирает, а приложение падает переполнением стека. Загрузка такой элемент не строит и называет
/// ошибкой (<c>AXM3054</c>), и форма показывается без него.
/// </para>
/// <para>
/// Типы проекта здесь собраны без компилятора XAML, поэтому у класса формы нет своей разметки: живой текст
/// заполняет экземпляр после конструктора. Путь, где класс грузит скомпилированную разметку, держат тесты Markup
/// (<c>SelfPlacementTests</c>).
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class FormClassDataTests
{
    /// <summary>Классы форм проекта: одна делает себя своими данными в коде, другая — нет.</summary>
    private const string ProjectSource = """
        using Avalonia.Controls;

        namespace App.Views;

        public class SelfView : UserControl
        {
            public SelfView() => DataContext = this;

            public string Greeting => "Привет из кода формы";
        }

        public class PlainView : UserControl
        {
            public string Greeting => "Привет из самой формы";
        }
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static XamlElementPath Hello => XamlElementPath.Parse("/0");

    /// <summary>Форма, которая в конструкторе ставит себя своими данными, показывает свои свойства.</summary>
    [AvaloniaFact]
    public async Task A_form_that_makes_itself_its_data_context_in_code_shows_its_own_properties()
    {
        await using var studio = new XamlStudio();

        Emit(studio);

        using var view = await ShowAsync(studio, """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="App.Views.SelfView">
              <TextBlock x:Name="Hello" Text="{Binding Greeting}" />
            </UserControl>
            """);

        Assert.Equal("Привет из кода формы", Shown(view));
    }

    /// <summary>Форма, чей корень привязывает данные к себе, показывает свои свойства без строчки кода.</summary>
    [AvaloniaFact]
    public async Task A_form_whose_root_binds_its_data_context_to_itself_shows_its_own_properties()
    {
        await using var studio = new XamlStudio();

        Emit(studio);

        using var view = await ShowAsync(studio, """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="App.Views.PlainView"
                         DataContext="{Binding $self}">
              <TextBlock x:Name="Hello" Text="{Binding Greeting}" />
            </UserControl>
            """);

        Assert.Equal("Привет из самой формы", Shown(view));
    }

    /// <summary>
    /// Форма с экземпляром своего класса в своих данных показана без него, а загрузка говорит, где он и почему.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_that_places_its_own_class_in_its_data_context_is_shown_without_it_and_says_why()
    {
        await using var studio = new XamlStudio();

        Emit(studio);

        using var view = await ShowAsync(studio, """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:v="using:App.Views"
                         x:Class="App.Views.PlainView">
              <UserControl.DataContext>
                <v:PlainView />
              </UserControl.DataContext>
              <TextBlock x:Name="Hello" Text="Остальное на месте" />
            </UserControl>
            """);

        Assert.Equal("Остальное на месте", Shown(view));
        Assert.Null(view.Root!.DataContext);

        var reported = Assert.Single(view.Document.Diagnostics, static d => d.Code == XamlLoaderDiagnosticCodes.SelfPlacement);

        Assert.True(reported.IsError, "форма, которая строит себя без конца, названа не ошибкой");
        Assert.Contains("App.Views.PlainView", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>Текст подписи, которую показал элемент <c>Hello</c>.</summary>
    private static string? Shown(IXamlDesignView view) => Assert.IsType<TextBlock>(view.ObjectAt(Hello)).Text;

    /// <summary>Кладёт выход проекта: сборку с классами форм.</summary>
    private static void Emit(XamlStudio studio)
    {
        var name = "FormClassData" + Guid.NewGuid().ToString("N")[..8];
        var output = Path.Combine(studio.ProjectFolder, "bin", name + ".dll");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        TestAssembly.EmitFile(output, name, ProjectSource);

        studio.AssemblyName = name;
        studio.Output = CanonicalPath.Create(output);
    }

    private static async Task<IXamlDesignView> ShowAsync(XamlStudio studio, string form)
    {
        var path = studio.Write("MainView.axaml", form);

        await studio.OpenAsync();

        var handle = await studio.Documents.OpenAsync(path, Token);

        return await handle.ShowAsync(null, Token);
    }
}
