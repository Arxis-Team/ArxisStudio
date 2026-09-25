using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Settings;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Окно настроек: страницы, транзакция и поиск.
/// </summary>
/// <remarks>
/// Настройку объявляет расширение, а не плагин: секция манифеста у модуля и у
/// внешнего плагина одна, и на экране они стоят вперемешку. Экран же долго
/// знал только плагинов — четыре настройки терминала были объявлены,
/// переведены на три языка и не показаны никому.
/// <para>
/// Второе, что здесь закреплено, — «Сохранить» и «Отмена». До этой работы
/// правка уходила на диск с каждым щелчком, и отменять было нечего: файл уже
/// изменён. Теперь строка копит правку у себя, а окно решает её судьбу.
/// </para>
/// <para>
/// Очередь общая с остальными: страница оформления снимает действующий язык, а
/// <c>Localizer</c> один на процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class SettingsTests : IDisposable
{
    private readonly string _home = TempFolder.Create("settings-ui");

    public void Dispose()
    {
        TempFolder.Erase(_home, strict: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Настройка встроенного модуля доходит до страницы.</summary>
    /// <remarks>
    /// Своего словаря у модуля нет, и подпись ему даёт словарь студии — но на
    /// экране это такая же строка, как у плагина: человеку всё равно, приехал
    /// терминал со студией или его поставили после.
    /// </remarks>
    [Fact]
    public void Settings_declared_by_a_built_in_module_reach_a_page()
    {
        var page = Assert.IsType<ExtensionPage>(Pages(Module()).Last().Children.Single());
        var row = Assert.Single(page.Rows);

        Assert.Equal("Терминал", page.Title);
        Assert.Equal("Кегль", row.Label);
        Assert.Equal("terminal.fontSize", row.Key);
    }

    /// <summary>
    /// Сперва принесённое студией, потом принесённое со стороны.
    /// </summary>
    /// <remarks>
    /// То же правило, что у полосы и у графа зависимостей: порядок один на всю
    /// студию, иначе одно и то же расширение стояло бы в разных местах разных
    /// списков.
    /// </remarks>
    [Fact]
    public void Module_pages_stand_before_the_plugin_ones()
    {
        var branch = Pages(Module(), Plugin()).Last();

        Assert.Equal(["Терминал", "Figma"], branch.Children.Select(child => child.Title));
    }

    /// <summary>Расширение без объявленных настроек страницы не получает.</summary>
    /// <remarks>
    /// Пустой узел обещал бы страницу и открывал пустоту — хуже, чем не
    /// показать ничего.
    /// </remarks>
    [Fact]
    public void An_extension_that_declared_nothing_takes_no_page()
    {
        var silent = new InstalledPlugin(
            _home,
            new PluginManifest { Id = "arxis.quiet", Name = "Тихий" },
            Error: null,
            IsEnabled: true,
            IsBuiltIn: false);

        Assert.Single(Pages(silent));
    }

    /// <summary>До «Сохранить» на диске не меняется ничего.</summary>
    [Fact]
    public void Nothing_reaches_the_file_until_it_is_saved()
    {
        var model = Model(out var values, Module());
        var row = Rows(model).Single();

        row.Text = "20";

        Assert.True(model.HasChanges);
        Assert.False(File.Exists(values.UserFile), "до сохранения писать некуда");
    }

    /// <summary>«Сохранить» пишет накопленное и говорит об этом расширению.</summary>
    [Fact]
    public async Task Saving_writes_the_staged_value_and_announces_it()
    {
        var told = new List<(string Plugin, string Key)>();
        var model = Model(out var values, [Module()], (plugin, key) => told.Add((plugin, key)));

        Rows(model).Single().Text = "20";

        Assert.True(await model.SaveAsync());
        Assert.Contains("20", File.ReadAllText(values.UserFile));
        Assert.Equal([("arxis.terminal", "terminal.fontSize")], told);
        Assert.False(model.HasChanges);
    }

    /// <summary>
    /// После «Сохранить» несохранённого не остаётся — и на странице оформления.
    /// </summary>
    /// <remarks>
    /// Страница держала тему и язык рядом с теми, что застала при открытии, и
    /// после записи их не обновляла: «Сохранить» закрывает окно, закрытие
    /// спрашивало о потере правок — уже лежащих в файле, — а ответ «закрыть»
    /// откатывал тему и язык к прежним при переписанном файле.
    /// </remarks>
    [Fact]
    public async Task Saving_the_appearance_leaves_nothing_unsaved()
    {
        var model = Model(out _, Module());
        var appearance = Assert.IsType<AppearancePage>(model.Nodes[0].Page);

        appearance.ThemeIndex = 1;

        Assert.True(await model.SaveAsync());
        Assert.False(model.HasChanges, "записанное несохранённым не считается");

        // Так закрывается окно после удавшегося сохранения.
        model.Revert();

        Assert.Equal(1, appearance.ThemeIndex);
    }

    /// <summary>«Отмена» забывает правку, не тронув файла.</summary>
    [Fact]
    public void Cancelling_forgets_the_edit()
    {
        var model = Model(out var values, Module());
        var row = Rows(model).Single();

        row.Text = "20";
        model.Revert();

        Assert.False(model.HasChanges);
        Assert.Equal("13", row.Text);
        Assert.False(File.Exists(values.UserFile), "отмена ничего не пишет");
    }

    /// <summary>
    /// Набранное обратно прежнее значение правкой не считается.
    /// </summary>
    /// <remarks>
    /// Иначе «правки не сохранены» спрашивали бы у того, кто ничего не менял.
    /// </remarks>
    [Fact]
    public void Typing_the_old_value_back_is_not_a_change()
    {
        var model = Model(out _, Module());
        var row = Rows(model).Single();

        row.Text = "20";
        row.Text = "13";

        Assert.False(model.HasChanges);
    }

    /// <summary>Поиск находит страницу по ключу настройки.</summary>
    /// <remarks>
    /// Ключ человек видит в файле настроек и в документации соседа — ищет он
    /// именно его, а не подпись.
    /// </remarks>
    [Fact]
    public void The_search_finds_a_page_by_the_key_of_its_setting()
    {
        var found = SettingsViewModel.Filter(Pages(Module()), "fontSize");
        var branch = Assert.Single(found);

        Assert.Equal("terminal.fontSize", Assert.IsType<ExtensionPage>(branch.Children.Single().Page).Rows[0].Key);
    }

    /// <summary>Что не совпало — того в дереве нет.</summary>
    [Fact]
    public void A_query_that_matches_nothing_empties_the_tree()
    {
        Assert.Empty(SettingsViewModel.Filter(Pages(Module()), "такого ключа нет"));
    }

    /// <summary>
    /// Страница, уцелевшая в поиске ради своих строк, показывает только совпавшие.
    /// </summary>
    /// <remarks>
    /// Правило то же, что у дерева, уровнем ниже: найденная настройка не тонет среди соседей, как
    /// в Project Settings у Unity. Стёртый поиск возвращает все строки.
    /// </remarks>
    [Fact]
    public void A_search_by_a_row_shows_only_the_rows_that_matched()
    {
        var model = Model(out _, Terminal());
        var rows = Rows(model);

        model.Search = "курсор";

        Assert.Equal(["terminal.cursorBlink"], rows.Where(row => row.IsShown).Select(row => row.Key));

        model.Search = "fontSize";

        Assert.Equal(["terminal.fontSize"], rows.Where(row => row.IsShown).Select(row => row.Key));

        model.Search = string.Empty;

        Assert.All(rows, row => Assert.True(row.IsShown, $"{row.Key} не вернулся, когда поиск стёрли"));
    }

    /// <summary>
    /// Страница, найденная по своей подписи или по подписи ветки над ней, показывается целиком.
    /// </summary>
    /// <remarks>Человек искал страницу, а не строку на ней, — и получает её всю.</remarks>
    [Fact]
    public void A_search_by_the_page_title_shows_the_whole_page()
    {
        var model = Model(out _, Terminal());
        var rows = Rows(model);

        foreach (var query in new[] { "Терминал", Localizer.Instance["settings.plugins"] })
        {
            model.Search = query;

            Assert.All(rows, row => Assert.True(row.IsShown, $"по запросу «{query}» спрятана строка {row.Key}"));
        }
    }

    /// <summary>Оформление находят и по его вариантам: «светлая» ведёт к строке темы, и только к ней.</summary>
    /// <remarks>Человек помнит, что хочет получить, а не как это называется.</remarks>
    [Fact]
    public void The_appearance_page_is_found_by_its_choices()
    {
        var model = Model(out _, Module());
        var appearance = Assert.IsType<AppearancePage>(model.Nodes[0].Page);

        model.Search = Localizer.Instance["settings.theme.light"];

        Assert.Same(appearance, model.Page);
        Assert.True(appearance.ShowsTheme, "строка темы спрятана поиском, который её нашёл");
        Assert.False(appearance.ShowsDensity, "строка плотности осталась, хотя поиск её не нашёл");
        Assert.False(appearance.ShowsLanguage, "строка языка осталась, хотя поиск её не нашёл");
    }

    /// <summary>Страницы, какими их видит окно.</summary>
    private IReadOnlyList<ISettingsPage> Pages(params InstalledPlugin[] extensions)
    {
        var model = Model(out _, extensions);

        return [.. model.Nodes.Select(node => node.Page)];
    }

    private SettingsViewModel Model(out PluginSettingsStore values, params InstalledPlugin[] extensions) =>
        Model(out values, extensions, announce: null);

    private SettingsViewModel Model(
        out PluginSettingsStore values,
        IReadOnlyList<InstalledPlugin> extensions,
        Action<string, string>? announce)
    {
        values = new PluginSettingsStore(userFile: Path.Combine(_home, "plugin-settings.json"));

        return new SettingsViewModel(
            new JsonSettingsStore(Path.Combine(_home, "settings.json")),
            values,
            extensions,
            announce);
    }

    private static IReadOnlyList<ViewModels.PluginSettingRow> Rows(SettingsViewModel model) =>
        model.Nodes.SelectMany(node => node.Children)
            .Select(node => node.Page)
            .OfType<ExtensionPage>()
            .SelectMany(page => page.Rows)
            .ToList();

    /// <summary>Модуль, объявивший одну настройку, — как его видит студия.</summary>
    /// <remarks>Папка — настоящая папка терминала: из неё берутся подписи настроек.</remarks>
    private static InstalledPlugin Module() => new(
        ModuleManifest.FolderOf(typeof(ArxisStudio.Modules.Terminal.TerminalModule).Assembly),
        new PluginManifest
        {
            Id = "arxis.terminal",
            Name = "Терминал",
            Contributions = new PluginContributions
            {
                Settings = { new PluginSetting("terminal.fontSize", "number", "user", "Кегль", 13) },
            },
        },
        Error: null,
        IsEnabled: true,
        IsBuiltIn: true);

    /// <summary>Модуль с двумя настройками: числом и флагом.</summary>
    private static InstalledPlugin Terminal() => new(
        ModuleManifest.FolderOf(typeof(ArxisStudio.Modules.Terminal.TerminalModule).Assembly),
        new PluginManifest
        {
            Id = "arxis.terminal",
            Name = "Терминал",
            Contributions = new PluginContributions
            {
                Settings =
                {
                    new PluginSetting("terminal.fontSize", "number", "user", "Кегль", 13),
                    new PluginSetting("terminal.cursorBlink", "bool", "user", "Мигающий курсор", true),
                },
            },
        },
        Error: null,
        IsEnabled: true,
        IsBuiltIn: true);

    /// <summary>Внешний плагин с одной настройкой.</summary>
    private InstalledPlugin Plugin() => new(
        _home,
        new PluginManifest
        {
            Id = "arxis.figma",
            Name = "Figma",
            Contributions = new PluginContributions
            {
                Settings = { new PluginSetting("figma.token", "string", "user", "Токен", "") },
            },
        },
        Error: null,
        IsEnabled: true,
        IsBuiltIn: false);
}
