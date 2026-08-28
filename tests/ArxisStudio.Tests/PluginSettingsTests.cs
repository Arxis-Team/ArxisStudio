using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
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

    private readonly string _home = Path.Combine(Path.GetTempPath(), $"arxis-settings-{Guid.NewGuid():N}");

    public PluginSettingsTests() => Directory.CreateDirectory(Project());

    public void Dispose()
    {
        if (Directory.Exists(_home))
            Directory.Delete(_home, recursive: true);

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

    private string Project() => Path.Combine(_home, "ВолнаЧат");

    private PluginSettingsStore Store() =>
        new(Path.Combine(Project(), "ВолнаЧат.sln"), Path.Combine(_home, "plugin-settings.json"));

    private PluginSettings Settings(StudioLog? log = null) =>
        new("arxis.figma", [Token, Format], Store(), log ?? new StudioLog());
}
