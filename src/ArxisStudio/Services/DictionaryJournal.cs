using ArxisStudio.Sdk;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.Services;

/// <summary>
/// Пишет в журнал о словарях, которые студия не прочла.
/// </summary>
/// <remarks>
/// Непрочитанный словарь студия берёт пустым, и подписи его хозяина становятся
/// ключами: <c>!panel.main!</c> вместо «Панели». Кроме журнала, сказать об этом
/// некому — у языкового пакета и у слоя поверх языка студии нет сборки, чьё
/// правило назвало бы порчу заранее.
/// </remarks>
internal static class DictionaryJournal
{
    /// <summary>Начинает писать; отпущенное — перестаёт.</summary>
    /// <param name="log">Журнал студии.</param>
    public static IDisposable Attach(IStudioLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        EventHandler<StringFileProblem> write = (_, problem) =>
            log.Write(StudioLogLevel.Warning, "Strings",
                $"Словарь {problem.Path} не прочитан, и его строки покажутся ключами: {problem.Reason}");

        StringFile.Unreadable += write;

        return new Detach(() => StringFile.Unreadable -= write);
    }

    private sealed class Detach(Action detach) : IDisposable
    {
        public void Dispose() => detach();
    }
}
