using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Словарь, который студия не прочла, называется в журнале.
/// </summary>
/// <remarks>
/// Непрочитанный словарь студия берёт пустым, и подписи его хозяина становятся ключами. Пустой
/// словарь вместо отказа — правило, и оно остаётся; молчание о нём — нет. У языкового пакета и у
/// слоя поверх языка студии нет сборки, которая сказала бы о порче заранее, и журнал — единственное
/// место, где человек узнаёт, почему подписи стали ключами.
/// </remarks>
public class DictionaryJournalTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"arxis-journal-{Guid.NewGuid():N}");

    public DictionaryJournalTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// О словаре, который не разобрался, журнал слышит — раз на версию файла.
    /// </summary>
    /// <remarks>
    /// Словарь перечитывают при смене языка и при перезагрузке плагина, и один сломанный файл иначе
    /// заполнил бы журнал. Снова испорченный после правки — уже другая версия, о ней говорят снова;
    /// исправленный — молчит.
    /// </remarks>
    [Fact]
    public void A_dictionary_that_does_not_parse_is_told_once_per_version()
    {
        var log = new StudioLog();

        using var journal = DictionaryJournal.Attach(log);

        var path = Write("strings.json", """{ "panel.main": "Панель" "panel.side": "Сбоку" }""", second: 0);

        Assert.Empty(StringFile.Read(path));
        Assert.Empty(StringFile.Read(path));

        var told = Assert.Single(Told(log, path));

        Assert.Equal(StudioLogLevel.Warning, told.Level);
        Assert.Contains("ключами", told.Message, StringComparison.Ordinal);

        Write("strings.json", """{ panel.main: "Панель" }""", second: 1);
        StringFile.Read(path);

        Assert.Equal(2, Told(log, path).Count);

        Write("strings.json", """{ "panel.main": "Панель" }""", second: 2);

        Assert.Equal("Панель", StringFile.Read(path)["panel.main"]);
        Assert.Equal(2, Told(log, path).Count);
    }

    /// <summary><c>null</c> вместо словаря — тоже непрочитанный словарь.</summary>
    /// <remarks>Разборщик на него не жалуется, а строк у хозяина от этого не прибавляется.</remarks>
    [Fact]
    public void Null_instead_of_a_dictionary_is_told_too()
    {
        var log = new StudioLog();

        using var journal = DictionaryJournal.Attach(log);

        var path = Write("strings.json", "null", second: 0);

        Assert.Empty(StringFile.Read(path));
        Assert.Single(Told(log, path));
    }

    /// <summary>Словаря нет вовсе — это не порча, и журнал молчит.</summary>
    /// <remarks>Перевода на каждый язык у плагина нет, и отсутствующий файл — обычное дело.</remarks>
    [Fact]
    public void A_missing_dictionary_is_not_told()
    {
        var log = new StudioLog();

        using var journal = DictionaryJournal.Attach(log);

        var path = Path.Combine(_folder, "strings.de.json");

        Assert.Empty(StringFile.Read(path));
        Assert.Empty(Told(log, path));
    }

    /// <summary>
    /// Журнал, подключённый позже первого чтения, о сломанном словаре всё равно узнаёт.
    /// </summary>
    /// <remarks>
    /// Словари языка студия читает рано, а журнал подключает при запуске приложения — первое чтение
    /// вполне может его опередить. Не услышанное никем сказанным не считается: иначе о словаре,
    /// сломанном с самого начала, журнал не узнал бы никогда.
    /// </remarks>
    [Fact]
    public void A_journal_attached_later_hears_about_a_file_broken_before()
    {
        var path = Write("strings.json", """{ "panel.main" "Панель" }""", second: 0);

        StringFile.Read(path);

        var log = new StudioLog();

        using var journal = DictionaryJournal.Attach(log);

        StringFile.Read(path);

        Assert.Single(Told(log, path));
    }

    /// <summary>Отпущенный журнал больше ничего не слышит.</summary>
    [Fact]
    public void A_released_journal_hears_nothing()
    {
        var log = new StudioLog();

        DictionaryJournal.Attach(log).Dispose();

        var path = Write("strings.json", "{ это не json", second: 0);

        StringFile.Read(path);

        Assert.Empty(Told(log, path));
    }

    /// <summary>Пишет словарь с заданным временем записи — версия файла не должна зависеть от часов.</summary>
    private string Write(string file, string content, int second)
    {
        var path = Path.Combine(_folder, file);

        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 9, 15, 12, 0, second, DateTimeKind.Utc));

        return path;
    }

    private static List<StudioLogRecord> Told(StudioLog log, string path) =>
        [.. log.Records.Where(record => record.Message.Contains(path, StringComparison.Ordinal))];
}
