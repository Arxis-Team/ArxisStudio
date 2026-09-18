using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.Services;

/// <summary>
/// Пишет в журнал о словарях, которые студия не прочла, и о словаре по умолчанию, которого у
/// расширения нет вовсе.
/// </summary>
/// <remarks>
/// Непрочитанный словарь студия берёт пустым, и подписи его хозяина становятся
/// ключами: <c>!panel.main!</c> вместо «Панели». Кроме журнала, сказать об этом
/// некому — у языкового пакета и у слоя поверх языка студии нет сборки, чьё
/// правило назвало бы порчу заранее.
/// <para>
/// Отсутствующий <c>lang/en.json</c> даёт то же самое, и сборки, которая сказала бы о нём, тоже нет:
/// меню и полосу студия строит по манифесту, а расширение, собранное до SDK 7.0, поднимает. Такое
/// расширение узнаётся по словарю под прежним именем рядом, и строка журнала называет, что делать.
/// </para>
/// </remarks>
internal static class DictionaryJournal
{
    /// <summary>Начинает писать; отпущенное — перестаёт.</summary>
    /// <param name="log">Журнал студии.</param>
    public static IDisposable Attach(IStudioLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        EventHandler<StringFileProblem> unreadable = (_, problem) =>
            log.Write(StudioLogLevel.Warning, "Strings",
                $"Словарь {problem.Path} не прочитан, и его строки покажутся ключами: {problem.Reason}");

        EventHandler<MissingDictionary> missing = (_, problem) =>
            log.Write(StudioLogLevel.Warning, "Strings", problem.Former is { } former
                ? $"У {problem.Owner} нет словаря {problem.Path}, и его строки покажутся ключами. Рядом лежит "
                    + $"{Path.GetFileName(former)} — так словарь назывался до SDK 7.0: пересоберите расширение под эту студию"
                : $"У {problem.Owner} нет словаря {problem.Path}, и его строки покажутся ключами");

        StringFile.Unreadable += unreadable;
        PluginStrings.Missing += missing;

        return new Detach(() =>
        {
            StringFile.Unreadable -= unreadable;
            PluginStrings.Missing -= missing;
        });
    }

    private sealed class Detach(Action detach) : IDisposable
    {
        public void Dispose() => detach();
    }
}
