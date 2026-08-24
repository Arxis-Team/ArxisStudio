using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Разбор вывода <c>dotnet new list</c>. Заголовки таблицы переведены на язык
/// системы, поэтому колонки берутся по позициям из строки-разделителя — тесты
/// проверяют именно это, на русском выводе.
/// </summary>
public class TemplateCatalogTests
{
    private const string RussianOutput =
        """
        Эти шаблоны соответствуют входным данным: .

        Имя шаблона                    Короткое имя           Язык        Теги
        -----------------------------  ---------------------  ----------  ------------------------
        Avalonia .NET App              avalonia.app           [C#],F#     Desktop/Xaml/Avalonia
        Avalonia .NET MVVM App         avalonia.mvvm          [C#],F#     Desktop/Xaml/Avalonia
        Библиотека классов             classlib               [C#],F#,VB  Common/Library
        Веб-приложение ASP.NET Core    webapp,razor           [C#]        Web/MVC/Razor Pages
        """;

    [Fact]
    public void Parses_every_row_of_the_table()
    {
        var templates = TemplateCatalog.Parse(RussianOutput);

        Assert.Equal(4, templates.Count);
        Assert.Equal("Avalonia .NET App", templates[0].Name);
        Assert.Equal("avalonia.app", templates[0].ShortName);
    }

    [Fact]
    public void Takes_the_first_short_name_of_an_alias_list()
    {
        var templates = TemplateCatalog.Parse(RussianOutput);

        Assert.Equal("webapp", templates[3].ShortName);
    }

    [Fact]
    public void Reads_languages_without_the_default_marker()
    {
        var templates = TemplateCatalog.Parse(RussianOutput);

        Assert.Equal(["C#", "F#", "VB"], templates[2].Languages);
    }

    [Fact]
    public void Splits_tags_into_parts()
    {
        var templates = TemplateCatalog.Parse(RussianOutput);

        Assert.Equal(["Desktop", "Xaml", "Avalonia"], templates[0].Tags);
        Assert.Equal("Desktop", templates[0].Category);
    }

    [Fact]
    public void Recognises_avalonia_templates()
    {
        var templates = TemplateCatalog.Parse(RussianOutput);

        Assert.True(templates[0].IsAvalonia);
        Assert.False(templates[2].IsAvalonia);
    }

    [Fact]
    public void Output_without_a_separator_yields_nothing()
    {
        Assert.Empty(TemplateCatalog.Parse("dotnet: command not found"));
    }
}
