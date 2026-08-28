using ArxisStudio.Extensibility;
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
[Collection(LocalizationCollection.Name)]
public class PluginStringsTests : IDisposable
{
    private readonly List<string> _folders = [];

    public void Dispose()
    {
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);

        foreach (var folder in _folders.Where(Directory.Exists))
            Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Ключ разворачивается словарём текущего языка.</summary>
    [Fact]
    public void A_key_is_taken_from_the_dictionary_of_the_current_language()
    {
        var plugin = Plugin(
            ("strings.json", """{ "panel.main": "Панель" }"""),
            ("strings.en.json", """{ "panel.main": "Panel" }"""));

        Localizer.Instance.SetLanguage("en");

        Assert.Equal("Panel", plugin.Strings.Resolve("%panel.main%"));
    }

    /// <summary>
    /// Перевода нет — берётся словарь по умолчанию.
    /// </summary>
    /// <remarks>
    /// Иначе локализация была бы всё или ничего: плагин, переведённый на один
    /// язык, показывал бы всем остальным пустые места вместо текста, который у
    /// него есть.
    /// </remarks>
    [Fact]
    public void Without_a_translation_the_default_dictionary_answers()
    {
        var plugin = Plugin(
            ("strings.json", """{ "panel.main": "Панель", "panel.side": "Сбоку" }"""),
            ("strings.en.json", """{ "panel.main": "Panel" }"""));

        Localizer.Instance.SetLanguage("en");

        Assert.Equal("Panel", plugin.Strings.Resolve("%panel.main%"));
        Assert.Equal("Сбоку", plugin.Strings.Resolve("%panel.side%"));
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
        Assert.Equal("!panel.main!", Plugin(("strings.json", "{ }")).Strings.Resolve("%panel.main%"));
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
        var first = Plugin(("strings.json", """{ "panel.main": "Первый" }"""));
        var second = Plugin(("strings.json", """{ "panel.main": "Второй" }"""));

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
        Assert.Equal("!projects.open!", Plugin(("strings.json", "{ }")).Strings.Resolve("%projects.open%"));
    }

    /// <summary>
    /// Встроенный модуль говорит словарями студии.
    /// </summary>
    /// <remarks>
    /// Своей папки у модуля нет, и словаря тоже: его строки писала студия, и
    /// лежат они там же, где весь остальной её текст.
    /// </remarks>
    [Fact]
    public void A_built_in_module_speaks_with_the_words_of_the_studio()
    {
        var module = new InstalledPlugin(
            AppContext.BaseDirectory,
            new PluginManifest { Id = "arxis.sample", Name = "%panel.sample%" },
            null,
            IsEnabled: true,
            IsBuiltIn: true);

        Assert.Equal(Localizer.Instance["panel.sample"], module.DisplayName);
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
        var plugin = Plugin(("strings.json", """{ "panel.main": "Из словаря" }"""));

        Assert.Equal("Панель", plugin.Strings.Resolve("Панель"));
    }

    /// <summary>Испорченный словарь не мешает плагину показаться.</summary>
    [Fact]
    public void A_broken_dictionary_does_not_take_the_plugin_down()
    {
        var plugin = Plugin(("strings.json", "{ это не json"));

        Assert.Equal("!panel.main!", plugin.Strings.Resolve("%panel.main%"));
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
        var plugin = Plugin(("strings.json", """{ "panel.main": "Было" }"""));

        Assert.Equal("Было", plugin.Strings.Resolve("%panel.main%"));

        File.WriteAllText(
            Path.Combine(plugin.Directory, PluginStrings.Folder, PluginStrings.DefaultFile),
            """{ "panel.main": "Стало" }""");

        PluginStrings.Forget(plugin.Directory);

        Assert.Equal("Стало", plugin.Strings.Resolve("%panel.main%"));
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
        var plugin = Plugin(("strings.json", """{ "menu.tools": "Инструменты", "menu.run": "Запустить" }"""));

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
            ("strings.json", """{ "panel.main": "Панель" }"""),
            ("strings.en.json", """{ "panel.main": "Panel" }"""));

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
}
