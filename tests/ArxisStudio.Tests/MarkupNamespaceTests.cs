using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Icons;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
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
