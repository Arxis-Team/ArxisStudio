using ArxisStudio.Shell.Localization;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Словари студии файлами: добавить язык — положить файл, а не пересобрать
/// студию.
/// </summary>
/// <remarks>
/// Встроенные словари при этом остаются основанием: студия обязана говорить и
/// тогда, когда рядом с ней не осталось ни одного файла.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioLanguagesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"arxis-lang-{Guid.NewGuid():N}");

    public StudioLanguagesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        Localizer.Instance.UseFolders();
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);

        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Строка из файла сильнее встроенной, а остальные остаются встроенными.
    /// </summary>
    /// <remarks>
    /// Наложение поключевое: файл, закрывающий одну строку, не отменяет
    /// остальные сто двадцать семь — иначе исправить одну подпись значило бы
    /// перевести студию заново.
    /// </remarks>
    [Fact]
    public void A_file_wins_over_the_embedded_dictionary_key_by_key()
    {
        Write("ru.json", """{ "projects.recent": "Раскрыть" }""");

        Localizer.Instance.UseFolders(_folder, _folder);
        Localizer.Instance.SetLanguage("ru");

        Assert.Equal("Раскрыть", Localizer.Instance["projects.recent"]);
        Assert.Equal("Проекты", Localizer.Instance["welcome.nav.projects"]);
    }

    /// <summary>
    /// Язык, которого студия не возит, появляется файлом.
    /// </summary>
    /// <remarks>
    /// Это и есть весь смысл: язык — данные, и требовать ради него сборки
    /// студии не за что.
    /// </remarks>
    [Fact]
    public void A_language_the_studio_does_not_ship_arrives_as_a_file()
    {
        Write("de.json", """{ "language.name": "Deutsch", "projects.recent": "Öffnen" }""");

        Localizer.Instance.UseFolders(_folder, _folder);

        Assert.Contains(Localizer.Instance.Languages, language => language is { Code: "de", Name: "Deutsch" });
        Assert.True(Localizer.Instance.SetLanguage("de"), "язык из файла не выбрался");
        Assert.Equal("Öffnen", Localizer.Instance["projects.recent"]);
    }

    /// <summary>
    /// Непереведённое падает на английский, а не превращается в <c>!ключ!</c>.
    /// </summary>
    /// <remarks>
    /// Студия растёт, ключей прибавляется, и перевод неизбежно отстаёт.
    /// Отстающий перевод должен показывать английскую строку — иначе он
    /// протухал бы на первом же нашем релизе целиком.
    /// </remarks>
    [Fact]
    public void What_a_partial_translation_misses_falls_into_english()
    {
        Write("de.json", """{ "projects.recent": "Öffnen" }""");

        Localizer.Instance.UseFolders(_folder, _folder);
        Localizer.Instance.SetLanguage("de");

        Assert.Equal("Projects", Localizer.Instance["welcome.nav.projects"]);
    }

    /// <summary>Язык, не назвавший себя, показывается своим кодом.</summary>
    [Fact]
    public void A_language_that_does_not_name_itself_is_shown_by_its_code()
    {
        Write("xx.json", """{ "projects.recent": "Недавние" }""");

        Localizer.Instance.UseFolders(_folder, _folder);

        Assert.Contains(Localizer.Instance.Languages, language => language is { Code: "xx", Name: "xx" });
    }

    /// <summary>Встроенные языки в списке есть и называют себя сами.</summary>
    [Fact]
    public void The_shipped_languages_name_themselves()
    {
        Localizer.Instance.UseFolders(_folder, _folder);

        Assert.Contains(Localizer.Instance.Languages, language => language is { Code: "ru", Name: "Русский" });
        Assert.Contains(Localizer.Instance.Languages, language => language is { Code: "en", Name: "English" });
    }

    /// <summary>
    /// Испорченный файл не отменяет встроенный словарь.
    /// </summary>
    /// <remarks>
    /// Словарь правит человек, и запятая не на месте — обычное дело. Студия,
    /// онемевшая из-за неё, была бы наказанием, несоразмерным поводу.
    /// </remarks>
    [Fact]
    public void A_broken_file_does_not_cancel_the_embedded_dictionary()
    {
        Write("ru.json", "{ это не json");

        Localizer.Instance.UseFolders(_folder, _folder);
        Localizer.Instance.SetLanguage("ru");

        Assert.Equal("Недавние", Localizer.Instance["projects.recent"]);
    }

    /// <summary>
    /// Перечитывание подхватывает правку файла.
    /// </summary>
    /// <remarks>
    /// Перевод правят строку за строкой, и перезапуск студии на каждую из них
    /// сделал бы работу переводчика невыносимой.
    /// </remarks>
    [Fact]
    public void Reloading_picks_up_an_edited_file()
    {
        Write("ru.json", """{ "projects.recent": "Было" }""");

        Localizer.Instance.UseFolders(_folder, _folder);
        Localizer.Instance.SetLanguage("ru");

        Assert.Equal("Было", Localizer.Instance["projects.recent"]);

        Write("ru.json", """{ "projects.recent": "Стало" }""");
        Localizer.Instance.Reload();

        Assert.Equal("Стало", Localizer.Instance["projects.recent"]);
    }

    private void Write(string file, string content) =>
        File.WriteAllText(Path.Combine(_folder, file), content);
}
