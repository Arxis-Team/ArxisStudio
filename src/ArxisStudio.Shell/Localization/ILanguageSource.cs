namespace ArxisStudio.Shell.Localization;

/// <summary>
/// Языки, пришедшие к студии откуда-то ещё — например, из установленных
/// языковых пакетов.
/// </summary>
/// <remarks>
/// Shell о плагинах не знает и знать не должен: словарь для него — это
/// «коды, которые кто-то умеет» и «строки по коду». Кто принёс эти строки,
/// откуда их прочитал и как обновляет — забота того, кто отдаёт источник.
/// </remarks>
public interface ILanguageSource
{
    /// <summary>Коды языков, которые источник знает.</summary>
    IReadOnlyCollection<string> Codes { get; }

    /// <summary>
    /// Словарь языка.
    /// </summary>
    /// <param name="language">Код языка.</param>
    /// <returns>Строки или пустой словарь, если такого языка у источника нет.</returns>
    IReadOnlyDictionary<string, string> Read(string language);
}
