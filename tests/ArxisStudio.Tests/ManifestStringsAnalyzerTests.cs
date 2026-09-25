using System.Collections.Immutable;
using ArxisStudio.Sdk.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правило «ключ манифеста должен найтись в словаре плагина».
/// </summary>
/// <remarks>
/// Ненайденный ключ студия показывает как <c>!ключ!</c>. Пропуск при этом виден
/// — но человеку и в чужой уже студии; автору он должен быть виден при сборке,
/// пока опечатку исправить дешевле всего.
/// </remarks>
public class ManifestStringsAnalyzerTests
{
    private const string Manifest = """
        {
          "id": "arxis.probe",
          "name": "Проба",
          "contributions": {
            "toolWindows": [ { "id": "probe.panel", "title": "%panel.probe%" } ]
          }
        }
        """;

    /// <summary>Ключа нет в словаре — о нём говорят при сборке.</summary>
    [Fact]
    public async Task A_key_missing_from_the_dictionary_is_reported()
    {
        var found = await AnalyzeAsync(Manifest, """{ "panel.other": "Другая" }""");

        var diagnostic = Assert.Single(found);

        Assert.Equal(ManifestStringsAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Contains("panel.probe", diagnostic.GetMessage());

        // Место находки — сам манифест: править нужно там, а не в коде.
        Assert.EndsWith("plugin.json", diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
    }

    /// <summary>Ключ на месте — правило молчит.</summary>
    [Fact]
    public async Task A_key_that_is_in_place_is_left_alone()
    {
        Assert.Empty(await AnalyzeAsync(Manifest, """{ "panel.probe": "Проба" }"""));
    }

    /// <summary>
    /// Словаря нет вовсе, а ключи есть — это тот же пропуск.
    /// </summary>
    /// <remarks>
    /// Забытая папка <c>lang/</c> — обычная ошибка при первой локализации:
    /// строки в манифесте уже ключами, а взять их неоткуда.
    /// </remarks>
    [Fact]
    public async Task Keys_without_any_dictionary_are_reported()
    {
        Assert.Single(await AnalyzeAsync(Manifest, dictionary: null));
    }

    /// <summary>
    /// Манифест без ключей словаря не требует.
    /// </summary>
    /// <remarks>
    /// Локализация необязательна: плагин, написанный на один язык, пишет
    /// подписи прямо в манифест, и правило к нему отношения не имеет.
    /// </remarks>
    [Fact]
    public async Task A_manifest_without_keys_needs_no_dictionary()
    {
        var manifest = Manifest.Replace("%panel.probe%", "Проба", StringComparison.Ordinal);

        Assert.Empty(await AnalyzeAsync(manifest, dictionary: null));
    }

    /// <summary>
    /// Ключи из секции полосы проверяются той же дорогой.
    /// </summary>
    /// <remarks>
    /// Правило ищет ключи по всему манифесту, а не по известным ему полям, —
    /// поэтому новая секция попадает под него без правки анализатора. Тест это
    /// закрепляет: появись у правила список полей, полоса выпала бы из него
    /// молча.
    /// </remarks>
    [Fact]
    public async Task A_key_in_the_toolbar_section_is_checked_too()
    {
        const string manifest = """
            {
              "id": "arxis.probe",
              "contributions": {
                "toolBar": [ { "id": "probe.run", "command": "probe.run", "title": "%toolbar.run%" } ]
              }
            }
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(manifest, """{ "panel.other": "Другая" }"""));

        Assert.Contains("toolbar.run", diagnostic.GetMessage());
    }

    /// <summary>
    /// Ключ в комментарии правилу не интересен — студия его не прочтёт.
    /// </summary>
    /// <remarks>
    /// Старую подпись автор вполне может оставить комментарием, а её ключ из словаря — убрать.
    /// Правило, искавшее ключи по всему тексту, давало на неё находку на пустом месте, а сборка с
    /// предупреждениями-ошибками из-за неё не собиралась.
    /// </remarks>
    [Fact]
    public async Task A_key_in_a_comment_is_not_asked_about()
    {
        const string manifest = """
            {
              "id": "arxis.probe",
              // "name": "%plugin.old%",
              "contributions": {
                /* "toolWindows": [ { "id": "probe.old", "title": "%panel.gone%" } ], */
                "toolWindows": [ { "id": "probe.panel", "title": "%panel.probe%" } ]
              }
            }
            """;

        Assert.Empty(await AnalyzeAsync(manifest, """{ "panel.probe": "Проба" }"""));
    }

    /// <summary>
    /// Ключ, который есть только в переводе, в словаре по умолчанию всё равно отсутствует.
    /// </summary>
    /// <remarks>
    /// Переводы сборка подаёт анализаторам ради проверки порчи и помечает ролью <c>translation</c>.
    /// Посчитай правило их ключи, опечатка в словаре по умолчанию пряталась бы за переводом — а на
    /// любом другом языке студия показала бы <c>!ключ!</c>.
    /// </remarks>
    [Fact]
    public async Task A_key_only_in_a_translation_is_still_missing()
    {
        var found = await AnalyzeAsync(
            Manifest,
            """{ "panel.other": "Другая" }""",
            translation: """{ "panel.probe": "Probe" }""");

        Assert.Contains("panel.probe", Assert.Single(found).GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Закомментированная строка словаря ключа не даёт — его нет.
    /// </summary>
    /// <remarks>
    /// Студия такую строку не прочтёт, и подпись с этим ключом покажется как <c>!ключ!</c>. Правило,
    /// собиравшее ключи словаря по всему тексту, считало закомментированный ключ на месте и молчало.
    /// </remarks>
    [Fact]
    public async Task A_key_commented_out_in_the_dictionary_is_missing()
    {
        var found = await AnalyzeAsync(Manifest, """
            {
              // "panel.probe": "Проба",
              "panel.other": "Другая",
            }
            """);

        Assert.Contains("panel.probe", Assert.Single(found).GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Словарь с комментариями и висячими запятыми отвечает за свои ключи.
    /// </summary>
    /// <remarks>Студия читает словарь так же мягко, как манифест, и правило не вправе быть строже.</remarks>
    [Fact]
    public async Task A_dictionary_with_comments_and_trailing_commas_answers_for_its_keys()
    {
        Assert.Empty(await AnalyzeAsync(Manifest, """
            {
              /* Подписи панели. */
              "panel.probe": "Проба",
            }
            """));
    }

    /// <summary>
    /// Находка стоит ровно на ключе — и тогда, когда перед ним в строке экранирование.
    /// </summary>
    /// <remarks>
    /// В пути меню ключей несколько, и косая между ними вправе быть записана как <c>\/</c>. Ключ ищется
    /// в записанном тексте строки, а не в разобранном: разобранный на знак короче, и место находки
    /// уехало бы с ключа.
    /// </remarks>
    [Fact]
    public async Task A_missing_key_is_marked_where_it_is_written()
    {
        const string manifest = """
            {
              "id": "arxis.probe",
              "contributions": {
                "menus": [ { "path": "%menu.tools%\/%menu.gone%", "command": "probe.run" } ]
              }
            }
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(manifest, """{ "menu.tools": "Инструменты" }"""));

        Assert.Equal(
            "%menu.gone%",
            manifest.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    /// <summary>
    /// Манифест встроенного модуля проверяется так же, как манифест плагина.
    /// </summary>
    /// <remarks>
    /// Прежде правило смотрело только на имя <c>plugin.json</c>, и у модулей
    /// ключ без строки не ловился вовсе: манифест зовётся <c>module.json</c>.
    /// Правила у модуля и у плагина одни — иначе код, переносимый между
    /// режимами, менял бы смысл при переносе.
    /// <para>
    /// Словарь модулю дают словари студии: <c>lang/</c> в его папке нет. Имя файла
    /// проверке безразлично — словарём считается всё поданное, кроме самих
    /// манифестов.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_manifest_of_a_built_in_module_is_checked_too()
    {
        var found = await AnalyzeAsync(
            Manifest,
            """{ "panel.other": "Другая" }""",
            manifestName: "module.json",
            dictionaryPath: "C:/studio/Localization/Strings/en.json");

        var diagnostic = Assert.Single(found);

        Assert.Contains("panel.probe", diagnostic.GetMessage());
        Assert.EndsWith("module.json", diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
    }

    /// <summary>Ключ модуля, который в словаре студии есть, замечанием не становится.</summary>
    [Fact]
    public async Task A_module_key_that_the_studio_declares_is_left_alone()
    {
        var found = await AnalyzeAsync(
            Manifest,
            """{ "panel.probe": "Проба" }""",
            manifestName: "module.json",
            dictionaryPath: "C:/studio/Localization/Strings/en.json");

        Assert.Empty(found);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string manifest,
        string? dictionary,
        string manifestName = "plugin.json",
        string dictionaryPath = "C:/probe/lang/en.json",
        string? translation = null)
    {
        var files = new List<AdditionalText> { new AdditionalFile($"C:/probe/{manifestName}", manifest) };

        var roles = new Dictionary<string, string>(StringComparer.Ordinal);

        if (dictionary is not null)
        {
            files.Add(new AdditionalFile(dictionaryPath, dictionary));
            roles[dictionaryPath] = "default";
        }

        // Перевод сборка подаёт с ролью translation — так его и отличают от словаря по умолчанию.
        if (translation is not null)
        {
            const string translationPath = "C:/probe/lang/de.json";

            files.Add(new AdditionalFile(translationPath, translation));
            roles[translationPath] = "translation";
        }

        return AnalyzerRun.Probe(AnalyzerRun.EmptyProbe)
            .RunAsync(new ManifestStringsAnalyzer(), files, new StringsRoles(roles));
    }
}
