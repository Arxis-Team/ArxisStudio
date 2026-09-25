using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Строки плагина: текст, который студия показывает за него.
/// </summary>
/// <remarks>
/// Заголовок панели, пункт меню и подпись настройки видны в студии раньше, чем
/// плагин впервые поднимут, — взять их из его кода неоткуда, и они читаются из
/// словарей рядом с манифестом.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginStringsTests : IDisposable
{
    private readonly List<string> _folders = [];

    public void Dispose()
    {
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);

        foreach (var folder in _folders)
            TempFolder.Erase(folder, strict: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Ключ разворачивается словарём текущего языка.</summary>
    [Fact]
    public void A_key_is_taken_from_the_dictionary_of_the_current_language()
    {
        var plugin = Plugin(
            ("en.json", """{ "panel.main": "Panel" }"""),
            ("ru.json", """{ "panel.main": "Панель" }"""));

        Localizer.Instance.SetLanguage("ru");

        Assert.Equal("Панель", plugin.Strings.Resolve("%panel.main%"));
    }

    /// <summary>
    /// Строки нет на текущем языке — отвечает английский.
    /// </summary>
    /// <remarks>
    /// Иначе локализация была бы всё или ничего: расширение, переведённое
    /// наполовину, показывало бы на месте непереведённого пустоту вместо текста,
    /// который у него есть. Запасной язык тот же, что у студии, и он один:
    /// «язык автора» отдельным файлом больше не объявляется.
    /// </remarks>
    [Fact]
    public void What_the_current_language_misses_comes_from_english()
    {
        var plugin = Plugin(
            ("en.json", """{ "panel.main": "Panel", "panel.side": "Side" }"""),
            ("ru.json", """{ "panel.main": "Панель" }"""));

        Localizer.Instance.SetLanguage("ru");

        Assert.Equal("Панель", plugin.Strings.Resolve("%panel.main%"));
        Assert.Equal("Side", plugin.Strings.Resolve("%panel.side%"));
    }

    /// <summary>
    /// Ключа нет нигде — он виден как <c>!ключ!</c>.
    /// </summary>
    /// <remarks>
    /// Пустая строка на месте заголовка выглядела бы как панель без имени, и
    /// искать причину пришлось бы в коде. Видимый ключ говорит, где смотреть.
    /// </remarks>
    [Fact]
    public void A_key_that_is_nowhere_stays_visible()
    {
        Assert.Equal("!panel.main!", Plugin(("en.json", "{ }")).Strings.Resolve("%panel.main%"));
    }

    /// <summary>
    /// Словарь соседа не виден.
    /// </summary>
    /// <remarks>
    /// Ключ вроде <c>panel.main</c> придумают двое, и общий словарь отдал бы его
    /// тому, кого раньше загрузили: чужой плагин молча переименовывал бы панель.
    /// </remarks>
    [Fact]
    public void One_plugin_does_not_read_the_dictionary_of_another()
    {
        var first = Plugin(("en.json", """{ "panel.main": "Первый" }"""));
        var second = Plugin(("en.json", """{ "panel.main": "Второй" }"""));

        Assert.Equal("Первый", first.Strings.Resolve("%panel.main%"));
        Assert.Equal("Второй", second.Strings.Resolve("%panel.main%"));
    }

    /// <summary>
    /// Словарь студии внешнему плагину тоже не виден.
    /// </summary>
    /// <remarks>
    /// Ключи студии внутренние: разрешив брать их, мы пообещали бы никогда не
    /// переименовывать свои строки, — а переименование текста внутри студии не
    /// должно менять текст в чужой панели.
    /// </remarks>
    [Fact]
    public void The_studio_dictionary_is_not_open_to_plugins()
    {
        Assert.Equal("!projects.open!", Plugin(("en.json", "{ }")).Strings.Resolve("%projects.open%"));
    }

    /// <summary>
    /// Встроенный модуль говорит своими словами.
    /// </summary>
    /// <remarks>
    /// Дорога у модуля и плагина одна: словарь лежит в папке расширения. Тем модуль и переносится
    /// во внешний плагин перекладыванием папки — прежде его строки оставались в студии, и переезд
    /// был неполным.
    /// <para>
    /// Ключ студии модулю при этом не виден, как не виден плагину: ключи студии внутренние, и
    /// переименование её строки не должно менять текст в чужой панели.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_built_in_module_speaks_with_its_own_words()
    {
        var module = new InstalledPlugin(
            ModuleManifest.FolderOf(typeof(Modules.Sample.SampleModule).Assembly),
            new PluginManifest { Id = "arxis.sample", Name = "%panel.sample%" },
            null,
            IsEnabled: true,
            IsBuiltIn: true);

        // Текст написан здесь, а не спрошен у словаря второй раз: сверка двух одинаковых
        // обращений прошла бы и на пустом словаре, где оба ответа — «!panel.sample!».
        Assert.Equal("Sample module", module.DisplayName);

        Assert.Equal("!projects.recent!", module.Strings.Resolve("%projects.recent%"));
    }

    /// <summary>Текст без процентов остаётся текстом.</summary>
    /// <remarks>
    /// Локализация необязательна: плагин, написанный на один язык, пишет
    /// подписи прямо в манифест и работает — иначе словарь был бы условием, без
    /// которого плагина не собрать.
    /// </remarks>
    [Fact]
    public void Plain_text_passes_through_untouched()
    {
        var plugin = Plugin(("en.json", """{ "panel.main": "Из словаря" }"""));

        Assert.Equal("Панель", plugin.Strings.Resolve("Панель"));
    }

    /// <summary>Испорченный словарь не мешает плагину показаться.</summary>
    [Fact]
    public void A_broken_dictionary_does_not_take_the_plugin_down()
    {
        var plugin = Plugin(("en.json", "{ это не json"));

        Assert.Equal("!panel.main!", plugin.Strings.Resolve("%panel.main%"));
    }

    /// <summary>
    /// Словарь плагина с комментариями и висячими запятыми читается, а закомментированной строки в нём
    /// нет.
    /// </summary>
    /// <remarks>
    /// Манифест плагина студия читает с комментариями, и автор вправе ждать того же от словаря рядом
    /// с ним. Прежде одна строка комментария делала словарь пустым, и все подписи плагина молча
    /// становились <c>!ключами!</c>.
    /// </remarks>
    [Fact]
    public void A_dictionary_with_comments_and_trailing_commas_is_read()
    {
        var plugin = Plugin(("en.json", """
            {
              // Подписи панели.
              "panel.main": "Панель",
              /* "panel.old": "Старая", */
            }
            """));

        Assert.Equal("Панель", plugin.Strings.Resolve("%panel.main%"));
        Assert.Equal("!panel.old!", plugin.Strings.Resolve("%panel.old%"));
    }

    /// <summary>
    /// Перезагрузка плагина перечитывает его словари.
    /// </summary>
    /// <remarks>
    /// Автор правит строки так же часто, как код, и перезагрузка, оставившая
    /// прежний текст, была бы перезагрузкой наполовину.
    /// </remarks>
    [Fact]
    public void Reloading_a_plugin_rereads_its_dictionaries()
    {
        var plugin = Plugin(("en.json", """{ "panel.main": "Было" }"""));

        Assert.Equal("Было", plugin.Strings.Resolve("%panel.main%"));

        File.WriteAllText(
            Path.Combine(plugin.Directory, PluginStrings.Folder, PluginStrings.DefaultFile),
            """{ "panel.main": "Стало" }""");

        PluginStrings.Forget(plugin.Directory);

        Assert.Equal("Стало", plugin.Strings.Resolve("%panel.main%"));
    }

    /// <summary>
    /// Словаря по умолчанию нет — журнал говорит об этом раз на попытку.
    /// </summary>
    /// <remarks>
    /// Без перевода отвечает английский, а без английского ключами становятся все строки
    /// расширения разом, и сказать об этом, кроме журнала, некому: меню строится по манифесту, и
    /// отказа, который назвал бы причину, нет. Смена языка перечитывает словари, но сказанного не
    /// повторяет; перезагрузка — новая попытка автора, и словарь, не нашедшийся и после неё,
    /// звучит снова.
    /// </remarks>
    [Fact]
    public void A_missing_english_dictionary_is_told_once_per_attempt()
    {
        var log = new StudioLog();

        using var journal = DictionaryJournal.Attach(log);

        var plugin = Plugin(("ru.json", """{ "panel.main": "Панель" }"""));

        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);

        Assert.Equal("!panel.main!", plugin.Strings.Resolve("%panel.main%"));
        Assert.Equal("!panel.side!", plugin.Strings.Resolve("%panel.side%"));

        Localizer.Instance.SetLanguage("ru");

        Assert.Equal("Панель", plugin.Strings.Resolve("%panel.main%"));

        var told = Assert.Single(Told(log, plugin));
        var expected = Path.Combine(plugin.Directory, PluginStrings.Folder, PluginStrings.DefaultFile);

        Assert.Equal(StudioLogLevel.Warning, told.Level);
        Assert.StartsWith($"У {plugin.Id} нет словаря {expected}", told.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("пересоберите", told.Message, StringComparison.Ordinal);

        PluginStrings.Forget(plugin.Directory);

        Assert.Equal("Панель", plugin.Strings.Resolve("%panel.main%"));
        Assert.Equal(2, Told(log, plugin).Count);
    }

    /// <summary>
    /// Словарь под прежним именем журнал называет, но студия его не читает.
    /// </summary>
    /// <remarks>
    /// Так выглядит расширение, собранное до SDK 7.0. Студия его поднимает — прежний старший номер
    /// <c>Satisfies</c> пропускает, — а словарь под прежним именем не читает, и все подписи
    /// становятся ключами. Без подсказки причину искали бы по датам файлов; чтения прежнего имени
    /// подсказка при этом не возвращает — обещание снято, и снято мажором.
    /// </remarks>
    [Fact]
    public void A_dictionary_under_the_former_name_is_named_but_not_read()
    {
        var log = new StudioLog();

        using var journal = DictionaryJournal.Attach(log);

        var plugin = Plugin(("strings.json", """{ "panel.main": "Панель" }"""));

        Assert.Equal("!panel.main!", plugin.Strings.Resolve("%panel.main%"));

        var told = Assert.Single(Told(log, plugin));

        Assert.Contains("strings.json", told.Message, StringComparison.Ordinal);
        Assert.Contains("пересоберите", told.Message, StringComparison.Ordinal);
    }

    /// <summary>Перевода на язык студии у расширения нет — журнал молчит.</summary>
    /// <remarks>Это обычное дело, а не беда: на месте перевода отвечает английский.</remarks>
    [Fact]
    public void An_extension_without_a_translation_is_not_told()
    {
        var log = new StudioLog();

        using var journal = DictionaryJournal.Attach(log);

        var plugin = Plugin(("en.json", """{ "panel.main": "Panel" }"""));

        Localizer.Instance.SetLanguage("ru");

        Assert.Equal("Panel", plugin.Strings.Resolve("%panel.main%"));
        Assert.Empty(Told(log, plugin));
    }

    /// <summary>
    /// Пункт меню переводится словарями своего плагина.
    /// </summary>
    /// <remarks>
    /// Путь режется на части до перевода: ключ разделителя не содержит, а
    /// переведённая строка вполне может — и «Файл/Открыть», пришедшее из
    /// словаря, развалило бы путь на две ветки.
    /// </remarks>
    [Fact]
    public void A_menu_item_is_translated_by_the_dictionary_of_its_plugin()
    {
        var plugin = Plugin(("en.json", """{ "menu.tools": "Инструменты", "menu.run": "Запустить" }"""));

        plugin.Manifest!.Contributions.Menus.Add(new PluginMenuItem("%menu.tools%/%menu.run%", "probe.run"));

        var branch = Assert.Single(StudioMenu.Build([plugin]));
        var item = Assert.Single(branch.Children);

        Assert.Equal("Инструменты", branch.Title);
        Assert.Equal("Запустить", item.Title);
    }

    /// <summary>
    /// Смена языка перерисовывает уже показанный заголовок панели плагина.
    /// </summary>
    /// <remarks>
    /// Иначе переключатель языка менял бы интерфейс студии, оставляя панели
    /// плагинов на прежнем языке, — окно оказалось бы переведённым наполовину.
    /// </remarks>
    [AvaloniaFact]
    public void Switching_language_updates_a_plugin_title_already_on_screen()
    {
        var plugin = Plugin(
            ("en.json", """{ "panel.main": "Panel" }"""),
            ("ru.json", """{ "panel.main": "Панель" }"""));

        Localizer.Instance.SetLanguage("ru");

        var text = new TextBlock();

        text.Bind(TextBlock.TextProperty, plugin.Strings.Text("panel.main"));

        var window = new Window { Content = text };

        window.Show();

        Assert.Equal("Панель", text.Text);

        Localizer.Instance.SetLanguage("en");

        Assert.Equal("Panel", text.Text);
        window.Close();
    }

    private InstalledPlugin Plugin(params (string File, string Content)[] dictionaries)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"arxis-lang-{Guid.NewGuid():N}");

        _folders.Add(directory);
        Directory.CreateDirectory(Path.Combine(directory, PluginStrings.Folder));

        foreach (var (file, content) in dictionaries)
            File.WriteAllText(Path.Combine(directory, PluginStrings.Folder, file), content);

        return new InstalledPlugin(
            directory,
            new PluginManifest { Id = Path.GetFileName(directory), Name = "Проба" },
            null,
            IsEnabled: true);
    }

    /// <summary>Что журнал сказал о словарях этого расширения.</summary>
    private static List<StudioLogRecord> Told(StudioLog log, InstalledPlugin plugin) =>
        [.. log.Records.Where(record => record.Message.Contains(plugin.Directory, StringComparison.Ordinal))];
}
