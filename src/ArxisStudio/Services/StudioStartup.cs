using System.Diagnostics;
using ArxisStudio.Sdk;
using ArxisStudio.Shell.Localization;
using ArxisStudio.ViewModels;
using Avalonia.Threading;

namespace ArxisStudio.Services;

/// <summary>
/// Запуск студии по этапам: что делается, в каком порядке и что об этом видно.
/// </summary>
/// <remarks>
/// До первого окна студия успевает прочитать настройки, поставить языковые
/// пакеты, разобрать словари, применить тему и обойти каталог плагинов. Раньше
/// всё это шло одним куском в <c>OnFrameworkInitializationCompleted</c>: на
/// экране не было ничего, а порядок держался тем, в каком порядке написаны
/// строки. Теперь порядок — это список, и список можно прочитать.
/// <para>
/// Два правила делают его пригодным для продукта. Первое: этап, упавший с
/// исключением, пишется в журнал, и запуск продолжается — испорченный языковой
/// пакет не повод не открыть студию. Второе: между этапами запуск уступает
/// поток интерфейса, иначе заставка не перерисуется ни разу и покажет первый
/// этап вместо всех.
/// </para>
/// </remarks>
public sealed class StudioStartup
{
    private readonly List<Stage> _stages = [];
    private readonly SplashViewModel _splash;
    private readonly StudioLog _log;

    /// <summary>
    /// Заводит запуск над моделью заставки.
    /// </summary>
    /// <param name="splash">Куда рассказывать о ходе.</param>
    /// <param name="log">Куда писать об упавшем этапе.</param>
    public StudioStartup(SplashViewModel splash, StudioLog log)
    {
        ArgumentNullException.ThrowIfNull(splash);
        ArgumentNullException.ThrowIfNull(log);

        _splash = splash;
        _log = log;
    }

    /// <summary>Сколько времени занял запуск.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>
    /// Сколько прошло с запуска процесса.
    /// </summary>
    /// <remarks>
    /// Считается от старта процесса, а не от первой своей строки: до неё
    /// успевают поднять среду и разобрать темы, и это тоже время, которое
    /// человек ждёт. Мерить с середины значило бы мерить не то.
    /// </remarks>
    public static TimeSpan SinceLaunch =>
        DateTime.Now - Process.GetCurrentProcess().StartTime;

    /// <summary>Этапы в порядке выполнения — их имена, как их видит человек.</summary>
    public IReadOnlyList<string> Stages => [.. _stages.Select(stage => stage.Key)];

    /// <summary>
    /// Добавляет этап в конец списка.
    /// </summary>
    /// <param name="key">Ключ словаря: подпись этапа на языке студии.</param>
    /// <param name="work">Что делается на этом этапе.</param>
    /// <returns>Тот же запуск — чтобы список читался одним выражением.</returns>
    public StudioStartup Add(string key, Action work)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(work);

        _stages.Add(new Stage(key, work));

        return this;
    }

    /// <summary>
    /// Проходит этапы по порядку.
    /// </summary>
    /// <remarks>
    /// Подпись объявляется до работы, доля растёт после: человек читает, чем
    /// студия занята сейчас.
    /// </remarks>
    public async Task RunAsync()
    {
        var clock = Stopwatch.StartNew();

        _splash.Expect(_stages.Count);

        foreach (var stage in _stages)
        {
            _splash.Begin(Localizer.Instance[stage.Key]);

            // Уступаем поток до работы, а не после: объявление, сделанное
            // строкой выше, иначе доедет до экрана вместе с концом этапа —
            // то есть никогда не будет прочитано.
            await Idle();

            Run(stage);

            _splash.Done();
        }

        Elapsed = clock.Elapsed;

        _log.Write(StudioLogLevel.Debug, "Startup",
            $"Этапы пройдены за {Elapsed.TotalMilliseconds:F0} мс, всего с запуска {SinceLaunch.TotalMilliseconds:F0} мс");
    }

    /// <summary>
    /// Делает этап, чего бы это ни стоило.
    /// </summary>
    /// <remarks>
    /// Исключение здесь — это сломанный файл настроек, занятый словарь, чужой
    /// плагин в каталоге. Ни одно из этого не стоит того, чтобы студия не
    /// открылась: человеку нужна студия, пусть и без языкового пакета, а
    /// причина остаётся в журнале.
    /// </remarks>
    private void Run(Stage stage)
    {
        try
        {
            stage.Work();
        }
        catch (Exception e)
        {
            _log.Write(StudioLogLevel.Error, "Startup",
                $"{Localizer.Instance[stage.Key]}: {e.Message}");
        }
    }

    /// <summary>Отдаёт поток интерфейса на один проход отрисовки.</summary>
    private static Task Idle() =>
        Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask();

    /// <summary>Этап запуска: подпись и работа.</summary>
    /// <param name="Key">Ключ словаря для подписи.</param>
    /// <param name="Work">Что делается.</param>
    private sealed record Stage(string Key, Action Work);
}
