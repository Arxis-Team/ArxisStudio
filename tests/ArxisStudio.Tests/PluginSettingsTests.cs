using System.Text.Json;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using ArxisStudio.ViewModels;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Настройки плагина: две области и одно объявление на обе.
/// </summary>
/// <remarks>
/// Пользовательская область — личное и машинное, проектная едет вместе с
/// проектом. Что где лежит, решает не тот, кто пишет значение, а манифест: иначе
/// один и тот же ключ у двух плагинов оказался бы в разных местах, и объяснить
/// человеку, где искать, было бы нечем.
/// </remarks>
public class PluginSettingsTests : IDisposable
{
    private static readonly PluginSetting Token =
        new("figma.token", "string", "user", "Токен", "пусто");

    private static readonly PluginSetting Format =
        new("figma.format", "string", "project", "Формат", "svg");

    /// <summary>
    /// Флажок, пришедший той же дорогой, что и настоящий, — разбором манифеста.
    /// </summary>
    /// <remarks>
    /// Записанный конструктором <c>true</c> здесь не годится: из файла значение
    /// по умолчанию приезжает <c>JsonElement</c>-ом, и читается оно иначе, чем
    /// обычный <c>bool</c>. Проверять надо ту дорогу, по которой ходит студия.
    /// </remarks>
    private static readonly PluginSetting Blink = JsonSerializer.Deserialize<PluginSetting>(
        """{ "key": "figma.blink", "type": "bool", "scope": "user", "title": "Мигание", "default": true }""",
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private readonly string _home = TempFolder.Reserve("settings");

    public PluginSettingsTests() => Directory.CreateDirectory(Project());

    public void Dispose()
    {
        TempFolder.Erase(_home, strict: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Пока ничего не записано, значение берётся из манифеста.</summary>
    [Fact]
    public void An_untouched_setting_reads_its_declared_default()
    {
        Assert.Equal("svg", Settings().Get<string>("figma.format"));
    }

    /// <summary>Пользовательская настройка ложится в файл рядом с настройками студии.</summary>
    [Fact]
    public void A_user_setting_lands_in_the_user_file()
    {
        var store = Store();

        Assert.Null(store.Write("arxis.figma", Token, "секрет"));

        Assert.Contains("секрет", File.ReadAllText(store.UserFile));
        Assert.False(File.Exists(store.ProjectFile!), "проектный файл трогать было незачем");
    }

    /// <summary>Проектная настройка ложится в проект и едет вместе с ним.</summary>
    [Fact]
    public void A_project_setting_lands_next_to_the_project()
    {
        var store = Store();

        Assert.Null(store.Write("arxis.figma", Format, "png"));

        Assert.Equal(Path.Combine(Project(), ".arxis", "settings.json"), store.ProjectFile);
        Assert.Contains("png", File.ReadAllText(store.ProjectFile!));
        Assert.DoesNotContain("png", File.Exists(store.UserFile) ? File.ReadAllText(store.UserFile) : string.Empty);
    }

    /// <summary>
    /// Договорённость проекта важнее привычки человека.
    /// </summary>
    /// <remarks>
    /// Так устроены и VS Code, и IntelliJ: значение, приехавшее с проектом,
    /// перекрывает то, что человек однажды поставил себе.
    /// </remarks>
    [Fact]
    public void The_project_value_wins_over_the_user_one()
    {
        var store = Store();
        var same = new PluginSetting("figma.format", "string", "user", null, "svg");

        store.Write("arxis.figma", same, "jpg");
        store.Write("arxis.figma", Format, "png");

        Assert.Equal("png", store.Read("arxis.figma", Format)!.GetValue<string>());
    }

    /// <summary>
    /// Проектную настройку без проекта записать некуда — и об этом говорится.
    /// </summary>
    /// <remarks>
    /// Сделать вид, что записали, нельзя: плагин прочтёт обратно не своё
    /// значение и решит, что человек его не менял.
    /// </remarks>
    [Fact]
    public void Without_a_project_a_project_setting_is_refused_with_a_word()
    {
        var store = new PluginSettingsStore(projectPath: null, userFile: Path.Combine(_home, "plugin-settings.json"));

        var error = store.Write("arxis.figma", Format, "png");

        Assert.NotNull(error);
        Assert.Contains("проект не открыт", error);
    }

    /// <summary>
    /// Ключ, не объявленный в манифесте, студия не принимает.
    /// </summary>
    /// <remarks>
    /// По объявлению студия знает, в какой области хранить значение и как
    /// показать его в настройках. Без объявления оно легло бы неизвестно куда и
    /// не показалось бы никому — включая того, кто его записал.
    /// </remarks>
    [Fact]
    public void An_undeclared_key_is_refused_and_said_so()
    {
        var log = new StudioLog();
        var settings = Settings(log);

        settings.Set("figma.secret", "значение");

        Assert.Null(settings.Get<string>("figma.secret"));
        Assert.Contains(log.Records, record => record.Message.Contains("не объявлена"));
    }

    /// <summary>Записанное читается обратно.</summary>
    [Fact]
    public void What_a_plugin_writes_it_reads_back()
    {
        var settings = Settings();
        var told = new List<string>();

        settings.Changed += (_, key) => told.Add(key);
        settings.Set("figma.token", "секрет");

        Assert.Equal("секрет", settings.Get<string>("figma.token"));
        Assert.Equal(["figma.token"], told);
    }

    /// <summary>
    /// Испорченный файл настроек не мешает студии работать.
    /// </summary>
    /// <remarks>
    /// Файл правят руками, и запятая не на месте — обычное дело. Настройки
    /// вернутся к объявленным по умолчанию, а починить файл человек сможет сам;
    /// отказ запускаться был бы несоразмерной ценой.
    /// </remarks>
    [Fact]
    public void A_broken_file_costs_the_values_and_nothing_more()
    {
        var file = Path.Combine(_home, "plugin-settings.json");

        File.WriteAllText(file, "{ это не json ");

        var store = new PluginSettingsStore(Project(), file);

        Assert.Equal("svg", store.Read("arxis.figma", Format)!.GetValue<string>());
    }

    /// <summary>
    /// Испорченный файл запись не стирает: она отказывает и называет файл.
    /// </summary>
    /// <remarks>
    /// Непрочитанный файл читался пустым, и первая же запись переписывала его одной веткой: пропадали
    /// все прочие настройки всех плагинов, а обещанное «починить файл человек сможет руками» чинить было
    /// уже нечего (запись 383). Так же поступает VS Code: в файл настроек с ошибкой он не пишет, а просит
    /// сперва его поправить.
    /// </remarks>
    [Theory]
    [InlineData("{ это не json ")]
    [InlineData("[ 1, 2 ]")]
    public void A_broken_file_is_not_overwritten_by_a_write(string broken)
    {
        var file = Path.Combine(_home, "plugin-settings.json");

        File.WriteAllText(file, broken);

        var error = new PluginSettingsStore(Project(), file).Write("arxis.figma", Token, "секрет");

        Assert.NotNull(error);
        Assert.Contains(file, error);
        Assert.Equal(broken, File.ReadAllText(file));
    }

    /// <summary>Испорченный проектный файл — тем более: он общий и едет в репозитории.</summary>
    /// <remarks>Маркеры конфликта слияния в нём — обычное дело, а стереть его значило бы стереть чужое.</remarks>
    [Fact]
    public void A_broken_project_file_is_not_overwritten_either()
    {
        var file = Path.Combine(Directory.CreateDirectory(Path.Combine(Project(), ".arxis")).FullName, "settings.json");
        var conflicted = string.Join('\n',
            "{",
            "<<<<<<< HEAD",
            """  "arxis.figma": { "figma.format": "png" }""",
            "=======",
            """  "arxis.figma": { "figma.format": "jpg" }""",
            ">>>>>>> feature",
            "}");

        File.WriteAllText(file, conflicted);

        Assert.NotNull(Store().Write("arxis.figma", Format, "svg"));
        Assert.Equal(conflicted, File.ReadAllText(file));
    }

    /// <summary>Поправленный руками файл снова принимает запись — без перезапуска студии.</summary>
    /// <remarks>Отказ перечитывает файл: человек мог поправить его, пока студия работала.</remarks>
    [Fact]
    public void A_file_fixed_by_hand_takes_writes_again()
    {
        var file = Path.Combine(_home, "plugin-settings.json");

        File.WriteAllText(file, "{ это не json ");

        var store = new PluginSettingsStore(Project(), file);

        Assert.NotNull(store.Write("arxis.figma", Token, "секрет"));

        File.WriteAllText(file, """{ "arxis.other": { "other.key": 1 } }""");

        Assert.Null(store.Write("arxis.figma", Token, "секрет"));

        var text = File.ReadAllText(file);

        Assert.Contains("other.key", text);
        Assert.Contains("секрет", text);
    }

    /// <summary>
    /// Строковая настройка не ломает привязку тумблера рядом с собой.
    /// </summary>
    /// <remarks>
    /// Тумблер и поле ввода стоят в одном шаблоне строки, и привязка тумблера
    /// вычисляется у каждой — в том числе у той, где он скрыт. Пока чтение было
    /// нетерпимым, каждая строковая настройка стоила журналу ошибки привязки
    /// про контрол, которого человек не видит.
    /// </remarks>
    [Fact]
    public void A_text_setting_does_not_break_the_toggle_beside_it()
    {
        var store = Store();

        store.Write("arxis.figma", Token, "секрет");

        Assert.False(Row(Token, store).Flag, "показывать тумблером строку нечем");
    }

    /// <summary>
    /// Объявленный манифестом флажок остаётся поднятым.
    /// </summary>
    /// <remarks>
    /// Терпимость к чужому типу не должна превратиться в «всегда ложь»:
    /// значение по умолчанию приезжает не из файла, а из манифеста, и читается
    /// другой дорогой. Отдай она здесь ложь — тумблер, объявленный включённым,
    /// показывался бы выключенным у каждого, кто его ни разу не трогал.
    /// </remarks>
    [Fact]
    public void A_toggle_still_reads_the_default_declared_in_the_manifest()
    {
        Assert.True(Row(Blink, Store()).Flag, "манифест объявил true");
    }

    /// <summary>
    /// Смена проекта приносит его значения и называет ключи, которые переменились.
    /// </summary>
    /// <remarks>
    /// Плагин своих настроек не перечитывает: он узнал их при подъёме. О том, что проектное
    /// значение стало другим, ему должна сказать студия — а сказать она может лишь о том, что
    /// хранилище ей назовёт.
    /// </remarks>
    [Fact]
    public void Following_another_project_names_the_keys_that_changed()
    {
        var store = Store();

        Assert.Null(store.Write("arxis.figma", Format, "png"));

        var another = Path.Combine(_home, "Другая");

        Directory.CreateDirectory(Path.Combine(another, ".arxis"));
        File.WriteAllText(
            Path.Combine(another, ".arxis", "settings.json"),
            """
            { "arxis.figma": { "figma.format": "pdf" } }
            """);

        var changed = store.Follow(Path.Combine(another, "Другая.sln"));

        Assert.Equal([("arxis.figma", "figma.format")], changed);
        Assert.Equal("pdf", Reader(store).Get<string>("figma.format"));
    }

    /// <summary>Тот же проект — ни перечитывания, ни слов.</summary>
    [Fact]
    public void Following_the_same_project_says_nothing()
    {
        var store = Store();

        Assert.Null(store.Write("arxis.figma", Format, "png"));
        Assert.Empty(store.Follow(Path.Combine(Project(), "ВолнаЧат.sln")));
    }

    /// <summary>
    /// Закрытие проекта называет ключи, которые он приносил.
    /// </summary>
    /// <remarks>
    /// Значение у них теперь другое — пользовательское или объявленное манифестом, — и молчание
    /// оставило бы панель плагина с тем, что уехало вместе с проектом.
    /// </remarks>
    [Fact]
    public void Closing_the_project_names_the_keys_it_carried()
    {
        var store = Store();

        Assert.Null(store.Write("arxis.figma", Format, "png"));

        var changed = store.Follow(null);

        Assert.Equal([("arxis.figma", "figma.format")], changed);
        Assert.Null(store.ProjectFile);
        Assert.Equal("svg", Reader(store).Get<string>("figma.format"));
    }

    /// <summary>Настройки плагина поверх названного хранилища.</summary>
    /// <param name="store">Хранилище.</param>
    private static PluginSettings Reader(PluginSettingsStore store) =>
        new("arxis.figma", [Token, Format], store, new StudioLog());

    private static PluginSettingRow Row(PluginSetting declared, PluginSettingsStore store) =>
        new("arxis.figma", "Figma", declared, store, PluginStrings.Studio);

    private string Project() => Path.Combine(_home, "ВолнаЧат");

    private PluginSettingsStore Store() =>
        new(Path.Combine(Project(), "ВолнаЧат.sln"), Path.Combine(_home, "plugin-settings.json"));

    private PluginSettings Settings(StudioLog? log = null) =>
        new("arxis.figma", [Token, Format], Store(), log ?? new StudioLog());
}
