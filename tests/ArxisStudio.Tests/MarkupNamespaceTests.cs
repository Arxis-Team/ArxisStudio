using System.Reflection;
using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Icons;
using ArxisStudio.Sdk;
using ArxisStudio.Shell;
using ArxisStudio.Themes.Arxis;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Metadata;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Словарь разметки студии: один адрес <c>xmlns</c> на все её библиотеки.
/// </summary>
/// <remarks>
/// Разметка здесь разбирается на ходу, а не компилируется: так проверяется
/// ровно то, что объявили сборки — <c>XmlnsDefinition</c>, — а не то, что
/// компилятор XAML однажды подставил в сгенерированный файл.
/// </remarks>
public class MarkupNamespaceTests
{
    private const string Url = "https://github.com/Arxis-Team/ArxisStudio";
    private const string Avalonia = "https://github.com/avaloniaui";

    /// <summary>Библиотеки, объявившие общий адрес.</summary>
    private static Assembly[] Libraries() =>
    [
        typeof(AxButton).Assembly,
        typeof(AxIcon).Assembly,
        typeof(ArxisTheme).Assembly,
        typeof(ToolWindow).Assembly,
        typeof(StudioShell).Assembly,
        typeof(DockView).Assembly,
    ];

    /// <summary>Всё, что открывается под общим адресом.</summary>
    private static IEnumerable<Type> Dictionary() =>
        Libraries().SelectMany(assembly => assembly
            .GetCustomAttributes<XmlnsDefinitionAttribute>()
            .Where(mapping => mapping.XmlNamespace == Url)
            .SelectMany(mapping => assembly.GetExportedTypes()
                .Where(type => type.Namespace == mapping.ClrNamespace)));

    /// <summary>
    /// Одно имя под общим адресом — у одной библиотеки.
    /// </summary>
    /// <remarks>
    /// За адресом стоит несколько сборок, и совпади в них имя типа —
    /// разметка молча досталась бы той, что раньше в списке ссылок:
    /// диагностики о неоднозначности у компилятора разметки нет. Библиотеки
    /// живут своими репозиториями и об именах друг друга не знают, поэтому
    /// проверка стоит здесь, где видно всех сразу.
    /// </remarks>
    [Fact]
    public void One_name_under_the_address_belongs_to_one_library()
    {
        var twice = Dictionary()
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .Where(names => names.Select(type => type.Assembly).Distinct().Count() > 1)
            .Select(names => names.Key)
            .ToList();

        Assert.Empty(twice);
    }

    /// <summary>
    /// Пространство имён с типами для разметки объявлено, а не забыто.
    /// </summary>
    /// <remarks>
    /// Забыть <c>XmlnsDefinition</c> для нового пространства имён нечем: тип
    /// просто не найдётся в разметке, и автор увидит «Unable to resolve type»
    /// вместо подсказки. Список исключений здесь — запись о том, что вынесено
    /// из словаря нарочно.
    /// </remarks>
    [Fact]
    public void A_namespace_with_markup_types_is_declared_and_not_forgotten()
    {
        // Чистые данные и настройки: из разметки их не пишут.
        string[] aside = ["ArxisStudio.Sdk.Plugins", "ArxisStudio.Shell.Settings"];

        var declared = Libraries()
            .SelectMany(assembly => assembly.GetCustomAttributes<XmlnsDefinitionAttribute>())
            .Where(mapping => mapping.XmlNamespace == Url)
            .Select(mapping => mapping.ClrNamespace)
            .ToHashSet(StringComparer.Ordinal);

        var forgotten = Libraries()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Select(type => type.Namespace)
            .OfType<string>()
            // Своё пространство имён добавляет в сборку с разметкой и сам
            // компилятор XAML — оно тут ни при чём.
            .Where(space => space.StartsWith("ArxisStudio.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Where(space => !declared.Contains(space) && !aside.Contains(space))
            .ToList();

        Assert.Empty(forgotten);
    }

    /// <summary>Контрол студии находится по общему адресу.</summary>
    [AvaloniaFact]
    public void A_studio_control_answers_at_the_studio_address()
    {
        var button = AvaloniaRuntimeXamlLoader.Parse<AxButton>($"<AxButton xmlns='{Url}'/>");

        Assert.NotNull(button);
    }

    /// <summary>
    /// Соседняя библиотека отвечает по тому же адресу.
    /// </summary>
    /// <remarks>
    /// Иконки — отдельная сборка и отдельный репозиторий, и в этом весь смысл
    /// общего адреса: автор разметки не должен знать, из какой библиотеки
    /// приехал контрол.
    /// </remarks>
    [AvaloniaFact]
    public void A_neighbour_library_answers_at_the_same_address()
    {
        var icon = AvaloniaRuntimeXamlLoader.Parse<AxIcon>($"<AxIcon xmlns='{Url}'/>");

        Assert.NotNull(icon);
    }

    /// <summary>Корень разметки расширения — тоже контрол студии.</summary>
    [AvaloniaFact]
    public void The_root_of_an_extension_view_is_a_studio_control()
    {
        var view = AvaloniaRuntimeXamlLoader.Parse<AxUserControl>($"<AxUserControl xmlns='{Url}'/>");

        Assert.NotNull(view);
    }

    /// <summary>
    /// Сборки самой студии стоят в том же словаре.
    /// </summary>
    /// <remarks>
    /// Докинг и оболочка — не библиотеки для плагинов, но разметка студии
    /// пишется теми же руками и по тем же правилам: адрес один, а кто что
    /// увидит, решают ссылки проекта.
    /// </remarks>
    [AvaloniaFact]
    public void The_studio_assemblies_join_the_same_dictionary()
    {
        var dock = AvaloniaRuntimeXamlLoader.Parse<DockView>($"<DockView xmlns='{Url}'/>");

        Assert.NotNull(dock);
    }

    /// <summary>
    /// Расширение разметки для строк находится по тому же адресу.
    /// </summary>
    /// <remarks>
    /// Подписи в разметке студии ставит <c>{Loc ключ}</c>, и живёт оно в
    /// пространстве имён локализации. Не объяви мы его — псевдоним пришлось бы
    /// держать ровно ради одной этой записи, самой частой в файле.
    /// </remarks>
    [AvaloniaFact]
    public void The_string_extension_answers_at_the_same_address()
    {
        var text = AvaloniaRuntimeXamlLoader.Parse<TextBlock>(
            $"<a:TextBlock xmlns='{Url}' xmlns:a='{Avalonia}' Text='{{Loc app.title}}'/>");

        Assert.True(text.IsSet(TextBlock.TextProperty), "привязка к строке не поставлена");
    }

    /// <summary>
    /// Виджета Avalonia по адресу студии нет.
    /// </summary>
    /// <remarks>
    /// Это и есть цена, которую платят осознанно: чужой словарь под свой адрес
    /// не подмешан, и родной виджет в такой разметке пишется с префиксом. Иначе
    /// пропало бы единственное, что показывает, чего в наборе <c>Ax*</c> ещё
    /// нет.
    /// </remarks>
    [AvaloniaFact]
    public void An_avalonia_widget_is_not_in_the_studio_dictionary()
    {
        Assert.ThrowsAny<Exception>(
            () => AvaloniaRuntimeXamlLoader.Parse<Button>($"<Button xmlns='{Url}'/>"));
    }

    /// <summary>
    /// Наш адрес и адрес Avalonia уживаются в одном документе.
    /// </summary>
    /// <remarks>
    /// Так и будет выглядеть разметка панели: по умолчанию словарь студии,
    /// раскладка и примитивы — с префиксом.
    /// </remarks>
    [AvaloniaFact]
    public void Both_dictionaries_live_in_one_document()
    {
        var view = AvaloniaRuntimeXamlLoader.Parse<AxUserControl>(
            $"""
             <AxUserControl xmlns='{Url}' xmlns:a='{Avalonia}'>
               <a:StackPanel>
                 <AxButton/>
               </a:StackPanel>
             </AxUserControl>
             """);

        var stack = Assert.IsType<StackPanel>(view.Content);

        Assert.IsType<AxButton>(Assert.Single(stack.Children));
    }
}
