using System.Collections.Immutable;
using System.Reflection;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Sdk.Analyzers;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правило «интерфейс расширения — на контролах студии» в разметке.
/// </summary>
/// <remarks>
/// Компилятор XAML разбирает разметку уже после Roslyn, и виджет, записанный
/// не кодом, а элементом, не увидел бы никто. Правило то же, что у
/// <c>ARX0001</c>, поэтому проверяется здесь не решение (оно общее), а
/// разрешение имён: чем объявлен префикс и что за ним стоит.
/// </remarks>
public class MarkupWidgetAnalyzerTests
{
    private const string Studio = "https://github.com/Arxis-Team/ArxisStudio";
    private const string Avalonia = "https://github.com/avaloniaui";

    /// <summary>Виджет Avalonia в разметке замечен.</summary>
    [Fact]
    public async Task A_widget_in_markup_is_noticed()
    {
        var found = Assert.Single(await AnalyzeAsync(
            $"""
             <AxUserControl xmlns="{Studio}" xmlns:a="{Avalonia}">
               <a:CalendarDatePicker/>
             </AxUserControl>
             """));

        Assert.Equal(MarkupWidgetAnalyzer.DiagnosticId, found.Id);
        Assert.Contains("CalendarDatePicker", found.GetMessage(), StringComparison.Ordinal);

        // И место названо: замечание без строки в файле разметки не найти.
        Assert.Equal(1, found.Location.GetLineSpan().StartLinePosition.Line);
    }

    /// <summary>
    /// Псевдоним <c>using:</c> разбирается так же, как адрес.
    /// </summary>
    /// <remarks>
    /// Оба способа объявить пространство имён равноправны, и правило,
    /// знающее только один, обходилось бы сменой записи в шапке файла.
    /// </remarks>
    [Fact]
    public async Task A_using_prefix_is_read_the_same_way()
    {
        var found = Assert.Single(await AnalyzeAsync(
            $"""
             <AxUserControl xmlns="{Studio}" xmlns:w="using:Avalonia.Controls">
               <w:CalendarDatePicker/>
             </AxUserControl>
             """));

        Assert.Contains("CalendarDatePicker", found.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Замечание называет замену, а где её нет — говорит и об этом.
    /// </summary>
    /// <remarks>
    /// Из запрета так выходит подсказка, а заодно — единственный список
    /// пробелов набора <c>Ax*</c>, который не приходится вести руками: сборка
    /// сама перечислит виджеты, которым замены пока нет.
    /// </remarks>
    [Fact]
    public async Task The_notice_names_the_replacement()
    {
        var replaced = Assert.Single(await AnalyzeAsync(
            $"""<a:Button xmlns:a="{Avalonia}"/>"""));

        Assert.Contains("AxButton", replaced.GetMessage(), StringComparison.Ordinal);

        var missing = Assert.Single(await AnalyzeAsync(
            $"""<a:CalendarDatePicker xmlns:a="{Avalonia}"/>"""));

        Assert.Contains("замены", missing.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Запись <c>clr-namespace</c> со сборкой разбирается тоже.
    /// </summary>
    /// <remarks>
    /// Третий способ объявить пространство имён, наследство WPF: имя сборки
    /// пишется после точки с запятой, и оставь мы его в имени типа — правило
    /// не нашло бы ничего и молчало бы на любом виджете.
    /// </remarks>
    [Fact]
    public async Task A_clr_namespace_with_an_assembly_is_read_too()
    {
        var found = Assert.Single(await AnalyzeAsync(
            """
            <AxUserControl xmlns:w="clr-namespace:Avalonia.Controls;assembly=Avalonia.Controls">
              <w:CalendarDatePicker/>
            </AxUserControl>
            """));

        Assert.Contains("CalendarDatePicker", found.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Контрол студии в разметке замечаний не собирает.</summary>
    [Fact]
    public async Task A_studio_control_is_left_alone()
    {
        Assert.Empty(await AnalyzeAsync(
            $"""
             <AxUserControl xmlns="{Studio}">
               <AxButton/>
               <AxIcon/>
             </AxUserControl>
             """));
    }

    /// <summary>
    /// Раскладка, рамка, текст и фигуры разрешены и в разметке.
    /// </summary>
    /// <remarks>
    /// Своего оформления они не несут, и черта в разметке проходит там же, где
    /// в коде, — по <c>TemplatedControl</c>.
    /// </remarks>
    [Fact]
    public async Task Layout_and_primitives_are_allowed()
    {
        Assert.Empty(await AnalyzeAsync(
            $"""
             <AxUserControl xmlns="{Studio}" xmlns:a="{Avalonia}">
               <a:DockPanel>
                 <a:Border>
                   <a:StackPanel>
                     <a:TextBlock Text="Заголовок"/>
                     <a:Rectangle Width="10" Height="1"/>
                   </a:StackPanel>
                 </a:Border>
               </a:DockPanel>
             </AxUserControl>
             """));
    }

    /// <summary>
    /// Элемент-свойство — не создание контрола.
    /// </summary>
    /// <remarks>
    /// <c>&lt;a:Grid.ColumnDefinitions&gt;</c> задаёт значение свойства, и
    /// замечание на нём означало бы, что правило считает виджетом запись
    /// вообще любого свойства.
    /// </remarks>
    [Fact]
    public async Task A_property_element_is_not_a_widget()
    {
        Assert.Empty(await AnalyzeAsync(
            $"""
             <AxUserControl xmlns="{Studio}" xmlns:a="{Avalonia}">
               <a:Grid>
                 <a:Grid.ColumnDefinitions>
                   <a:ColumnDefinition Width="Auto"/>
                 </a:Grid.ColumnDefinitions>
               </a:Grid>
             </AxUserControl>
             """));
    }

    /// <summary>
    /// Имя, за которым не стоит типа, замечания не собирает.
    /// </summary>
    /// <remarks>
    /// Про чужой префикс и опечатку скажет компилятор разметки; правилу здесь
    /// сказать нечего, а придирка на пустом месте стоила бы доверия ко всем
    /// остальным.
    /// </remarks>
    [Fact]
    public async Task An_unknown_name_is_left_to_the_compiler()
    {
        Assert.Empty(await AnalyzeAsync(
            $"""
             <AxUserControl xmlns="{Studio}" xmlns:z="using:Nowhere.At.All">
               <z:Whatever/>
               <CalendarDatePicker/>
             </AxUserControl>
             """));
    }

    /// <summary>
    /// Недописанная разметка правило не роняет.
    /// </summary>
    /// <remarks>
    /// Файл, который правят прямо сейчас, разбирается не всегда — и сказать об
    /// этом должен компилятор разметки, а не исключение из анализатора: оно
    /// вышло бы ошибкой сборки без единого слова о причине.
    /// </remarks>
    [Fact]
    public async Task Unfinished_markup_does_not_break_the_rule()
    {
        Assert.Empty(await AnalyzeAsync($"""<AxUserControl xmlns="{Studio}"><a:CalendarDatePicker"""));
    }

    /// <summary>Файл не из разметки анализатор не читает.</summary>
    [Fact]
    public async Task Only_markup_is_read()
    {
        Assert.Empty(await AnalyzeAsync(
            $"""<a:CalendarDatePicker xmlns:a="{Avalonia}"/>""",
            path: "C:/probe/plugin.json"));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string markup, string path = "C:/probe/View.axaml")
    {
        // Сборки грузятся лениво, а ссылки собираются по загруженным: без
        // касания типов ни контролов студии, ни виджетов Avalonia в списке
        // может не оказаться вовсе — и правилу нечего будет разрешать.
        Assembly[] anchors =
        [
            typeof(AxButton).Assembly,
            typeof(AxIcon).Assembly,
            typeof(CalendarDatePicker).Assembly,
            typeof(Rectangle).Assembly,
        ];

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Concat(anchors)
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText("public sealed class Probe { }")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzed = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new MarkupWidgetAnalyzer()),
            new AnalyzerOptions([new Given(path, markup)]));

        return await analyzed.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Файл, переданный анализатору входом сборки.</summary>
    private sealed class Given(string path, string content) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(content);
    }
}
