using ArxisStudio.Sdk;
using ArxisStudio.Settings;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Welcome;
using Avalonia.Controls.ApplicationLifetimes;

namespace ArxisStudio.Services;

/// <summary>
/// Перезапуск процесса студии: снять сессию, поднять новую копию, дождаться её ответа и уйти.
/// </summary>
/// <remarks>
/// Вопрос о перезапуске задаёт <see cref="StudioRestart"/>, а здесь — то, что начинается после
/// согласия: процесс, окна и файл сессии. Жило это в приложении, которое, кроме того, поднимает
/// студию, отвечает второй копии и открывает проект; порядок перезапуска держится одним правилом и
/// читается теперь отдельно от остального.
/// </remarks>
/// <param name="desktop">Жизненный цикл приложения — его окна и его закрытие.</param>
/// <param name="studio">Окно студии.</param>
/// <param name="log">Журнал студии.</param>
internal sealed class StudioRestarter(
    IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow studio,
    IStudioLog log)
{
    /// <summary>
    /// Перезапускает студию: поднимает новую копию с сессией и закрывает эту.
    /// </summary>
    /// <returns><c>true</c> — эта копия закрывается; <c>false</c> — не вышло, и она работает дальше.</returns>
    /// <remarks>
    /// Порядок держит одно правило: до ответа новой копии эта не трогает ничего. Сессия снимается
    /// первой — пока всё открыто, — и ложится файлом; новая копия поднимается и отвечает, когда
    /// взяла его. Не ответила — её снимают, файл стирают, а человек остаётся в той же студии, со
    /// всем открытым.
    /// <para>
    /// Ответила — пути назад нет: сессия у неё. Эта перестаёт отвечать вторым студиям, закрывает
    /// окно настроек сама — его крестик отменяет первое закрытие, и вместе с ним отменилось бы
    /// закрытие студии, — дожидается закрытия документов и закрывается. Окно, отказавшееся
    /// закрыться, закрывается принудительно: новая копия ждёт папку данных, и две студии над ней
    /// хуже, чем окно плагина, не спросившее о своём.
    /// </para>
    /// </remarks>
    public async Task<bool> RestartAsync()
    {
        var pid = Environment.ProcessId;
        var file = StudioSession.FileFor(StudioPaths.UserData, pid);

        // Шире, чем бросает запись: перезапуск зовут из обработчиков щелчка, и сбой снимка, дошедший
        // до них, унёс бы студию вместе со всем, что перезапуск обещал вернуть.
        try
        {
            StudioSession.Write(Capture(), file);
        }
        catch (Exception e) when (Faults.Survivable(e))
        {
            StudioSession.Forget(file);

            return Refuse($"сессию не снять: {e.Message}");
        }

        System.Diagnostics.Process? successor;

        try
        {
            successor = StudioRelaunch.Launch(file);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException
                                      or IOException or ArgumentException or UnauthorizedAccessException
                                      or NotSupportedException)
        {
            StudioSession.Forget(file);

            return Refuse($"новая копия не поднялась: {e.Message}");
        }

        if (successor is null
            || !await StudioRelaunch.AwaitTakenAsync(file, () => successor.HasExited, StudioRelaunch.Handshake))
        {
            StudioRelaunch.Abort(successor);
            StudioSession.Forget(file);

            return Refuse("новая копия не приняла сессию");
        }

        log.Write(StudioLogLevel.Info, "Restart", $"Новая копия (процесс {successor.Id}) приняла сессию — эта закрывается");

        StudioInstance.Current?.Stop();

        foreach (var settings in desktop.Windows.OfType<SettingsWindow>().ToList())
            settings.CloseForRestart();

        await studio.PrepareForRestartAsync();

        if (!desktop.TryShutdown())
        {
            log.Write(StudioLogLevel.Warning, "Restart", "Окно отказалось закрыться — студия закрывается принудительно: сессия уже у новой копии");
            desktop.Shutdown();
        }

        return true;
    }

    /// <summary>
    /// Снимает сессию: какое окно спереди, рабочее место, окно настроек, Welcome и причины.
    /// </summary>
    /// <remarks>
    /// Окно студии спрашивается списком окон, а не видимостью: у окна, которое ещё не показывали,
    /// <c>IsVisible</c> уже истинно.
    /// </remarks>
    private StudioSession Capture()
    {
        var shown = desktop.Windows.Contains(studio);
        var session = shown
            ? studio.Snapshot()
            : new StudioSession { Welcome = desktop.Windows.OfType<WelcomeWindow>().FirstOrDefault()?.Snapshot() };

        return session with
        {
            Settings = desktop.Windows.OfType<SettingsWindow>().FirstOrDefault()?.Snapshot(),
            Reasons = new Dictionary<string, string>(studio.Extensions.AwaitingRestart, StringComparer.Ordinal),
        };
    }

    /// <summary>Перезапуск не состоялся: причина — в журнал, человеку — что не вышло.</summary>
    /// <returns>Всегда <c>false</c>: студия работает дальше.</returns>
    /// <remarks>
    /// Человеку говорится там, куда он смотрит: открытое окно настроек модальное и стоит поверх
    /// строки состояния, и перезапуск, начатый из него, отказывает его подвалом.
    /// </remarks>
    private bool Refuse(string reason)
    {
        log.Write(StudioLogLevel.Error, "Restart", $"Перезапуск не состоялся: {reason}");

        var said = Localizer.Instance["restart.failed"];

        if (desktop.Windows.OfType<SettingsWindow>().FirstOrDefault() is { } settings)
            settings.Say(said);
        else if (desktop.Windows.Contains(studio))
            studio.Say(said);
        else
            desktop.Windows.OfType<WelcomeWindow>().FirstOrDefault()?.Say(said);

        return false;
    }
}
