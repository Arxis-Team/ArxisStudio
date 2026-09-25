using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Файлы расширения, какими их подаёт сборка: манифест и словари, — и место находки в них.
/// </summary>
/// <remarks>
/// Пять правил узнавали манифест по имени, шесть считали место находки в тексте, два спрашивали
/// роль словаря — каждое своей копией. Правило, узнающее манифест иначе соседа, молчит там, где
/// сосед говорит: так когда-то <c>ARX0002</c> не видел <c>module.json</c>.
/// </remarks>
internal static class ManifestFiles
{
    /// <summary>Роль словаря по умолчанию: с его ключами сверяется манифест.</summary>
    public const string Default = "default";

    /// <summary>Роль перевода: его проверяют на чтение, но ключей манифесту он не даёт.</summary>
    public const string Translation = "translation";

    private static readonly string[] Names = { "plugin.json", "module.json" };

    private static readonly char[] Separators = { '/', '\\' };

    /// <summary>
    /// Манифест ли это — <c>plugin.json</c> плагина или <c>module.json</c> модуля.
    /// </summary>
    /// <remarks>
    /// По имени, а не по тому, что файл — JSON: рядом с манифестом сборка подаёт словарь
    /// расширения (<c>lang/en.json</c> и у плагина, и у модуля), и принятый за манифест словарь дал
    /// бы находки на пустом месте.
    /// </remarks>
    /// <param name="path">Путь входа сборки.</param>
    public static bool IsManifest(string path)
    {
        var separator = path.LastIndexOfAny(Separators);
        var name = separator < 0 ? path : path.Substring(separator + 1);

        foreach (var manifest in Names)
        {
            if (string.Equals(name, manifest, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Манифест среди входов сборки; null — у проекта манифеста нет.</summary>
    /// <param name="options">Входы сборки.</param>
    public static AdditionalText? Find(AnalyzerOptions options) =>
        options.AdditionalFiles.FirstOrDefault(file => IsManifest(file.Path));

    /// <summary>
    /// Роль входа сборки: <see cref="Default"/>, <see cref="Translation"/> или null — не словарь.
    /// </summary>
    /// <remarks>
    /// Роль ставит сборка метаданными <c>AxStrings</c> — ключ <see cref="StringsFileAnalyzer.RoleKey"/>.
    /// По имени или расширению словарь не угадывается: рядом с манифестом вполне может лежать чужой JSON.
    /// </remarks>
    /// <param name="options">Входы сборки.</param>
    /// <param name="file">Вход, о котором спрашивают.</param>
    public static string? Role(AnalyzerOptions options, AdditionalText file) =>
        options.AnalyzerConfigOptionsProvider.GetOptions(file).TryGetValue(StringsFileAnalyzer.RoleKey, out var role) && role.Length > 0
            ? role
            : null;

    /// <summary>Место отрезка текста в файле — там, где его покажет среда.</summary>
    /// <param name="path">Путь файла.</param>
    /// <param name="text">Его текст.</param>
    /// <param name="span">Отрезок.</param>
    public static Location At(string path, SourceText text, TextSpan span) =>
        Location.Create(path, span, text.Lines.GetLinePositionSpan(span));
}
