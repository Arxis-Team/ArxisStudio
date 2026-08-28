using System.Globalization;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Насколько язык из пакета закрывает строки студии.
/// </summary>
/// <remarks>
/// Отвечает на вопрос, который человек задаёт первым: пакет установлен,
/// язык выбран, а половина студии по-прежнему по-английски — сломано или так
/// и задумано. Считается по ключам студии, а не по ключам пакета: лишние
/// ключи, которых у студии нет, полноты не прибавляют.
/// <para>
/// Число живое: студия растёт, ключей прибавляется, и вчера полный перевод
/// сегодня показывает меньше. Это и есть то, что человеку нужно знать.
/// </para>
/// </remarks>
/// <param name="Name">Название языка так, как назвал его пакет.</param>
/// <param name="Translated">Сколько ключей студии закрыто.</param>
/// <param name="Total">Сколько ключей у студии всего.</param>
public sealed record LanguageCoverage(string Name, int Translated, int Total)
{
    /// <summary>Подпись для человека: «Deutsch — 104 из 128».</summary>
    public string Label => string.Format(
        CultureInfo.CurrentCulture,
        Localizer.Instance["plugins.coverage"],
        Name,
        Translated,
        Total);

    /// <summary>
    /// Считает полноту объявленного языка.
    /// </summary>
    /// <param name="directory">Папка пакета.</param>
    /// <param name="declared">Объявление языка из манифеста.</param>
    /// <returns>Полнота или null, если словаря нет.</returns>
    internal static LanguageCoverage? Of(string directory, Sdk.Plugins.PluginLanguage declared)
    {
        if (declared.File is not { Length: > 0 } file)
            return null;

        var strings = StringFile.Read(Path.Combine(directory, file));

        if (strings.Count == 0)
            return null;

        var keys = Localizer.Instance.Keys;
        var translated = keys.Count(strings.ContainsKey);

        return new LanguageCoverage(
            declared.Name is { Length: > 0 } name ? name : declared.Code,
            translated,
            keys.Count);
    }
}
