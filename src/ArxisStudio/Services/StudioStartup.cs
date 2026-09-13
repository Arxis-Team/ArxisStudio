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
/// <para>
/// Первое правило знает исключение, и оно названо словом: этап роковой, если
/// студии без него не бывает. Таких два — сборка оболочки и первое окно.
/// Упавший роковой останавливает список: этапы за ним не делают своей работы,
/// а падают об пустое окно, и в журнал шло три исключения вместо одной причины.
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

    /// <summary>Этапы в порядке выполнения — их имена, как их видит человек.</summary>
    public IReadOnlyList<string> Stages => [.. _stages.Select(stage => stage.Key)];

    /// <summary>
    /// Добавляет этап в конец списка.
    /// </summary>
    /// <param name="key">Ключ словаря: подпись этапа на языке студии.</param>
    /// <param name="work">Что делается на этом этапе.</param>
    /// <param name="fatal">Есть ли студия без этого этапа.</param>
    /// <returns>Тот же запуск — чтобы список читался одним выражением.</returns>
    /// <remarks>
    /// Не всякая работа асинхронна, и заворачивать чтение поля в задачу ради
    /// единообразия значило бы врать о её природе.
    /// </remarks>
    public StudioStartup Add(string key, Action work, bool fatal = false)
    {
        ArgumentNullException.ThrowIfNull(work);

        return Add(
            key,
            _ =>
            {
                work();

                return Task.CompletedTask;
            },
            fatal);
    }

    /// <summary>
    /// Добавляет этап, которому нужно время.
    /// </summary>
    /// <param name="key">Ключ словаря: подпись этапа на языке студии.</param>
    /// <param name="work">Что делается на этом этапе.</param>
    /// <param name="fatal">Есть ли студия без этого этапа.</param>
    /// <returns>Тот же запуск.</returns>
    /// <remarks>
    /// Обход каталога плагинов и чтение настроек — это диск, а диск бывает
    /// сетевым. Синхронный этап держит поток отрисовки, и заставка, ради
    /// которой всё затевалось, замирает ровно там, где должна была
    /// рассказывать.
    /// </remarks>
    public StudioStartup Add(string key, Func<CancellationToken, Task> work, bool fatal = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(work);

        _stages.Add(new Stage(key, work, fatal));

        return this;
    }

    /// <summary>
    /// Проходит этапы по порядку.
    /// </summary>
    /// <remarks>
    /// Подпись объявляется до работы, доля растёт после: человек читает, чем
    /// студия занята сейчас.
    /// </remarks>
    public async Task<StartupReport> RunAsync(CancellationToken token = default)
    {
        var clock = Stopwatch.StartNew();
        var failed = new List<StageFailure>();

        // Первый кадр — отдельная фаза, и ждать его надо здесь. Окно заставки
        // показано, но ещё не нарисовано: рисует его тот же поток, который
        // сейчас читает эти строки. Не уступив ему до первого этапа, студия
        // приписала бы стоимость первой отрисовки чтению папок — так и вышло,
        // и в отчёте «paths» стоил двести миллисекунд вместо двух.
        await Idle();
        StudioLaunch.Mark("кадр");

        // Число этапов объявляется после первого кадра, а не до него. До кадра
        // «полоса бежит» существовало только в модели: заставка появлялась уже
        // с долей, и бегущей полосы не видел никто.
        _splash.Expect(_stages.Count);

        foreach (var stage in _stages)
        {
            _splash.Begin(Localizer.Instance[stage.Key]);

            // Уступаем поток до работы, а не после: объявление, сделанное
            // строкой выше, иначе доедет до экрана вместе с концом этапа —
            // то есть никогда не будет прочитано.
            await Idle();

            if (await RunAsync(stage, token) is { } failure)
            {
                failed.Add(failure);

                // Этап после рокового не выполняется — и не падает об него.
                // Три подряд NullReferenceException, которыми кончалась
                // упавшая сборка оболочки, были ровно этим: modules,
                // extensions и welcome брались за пустое окно по очереди.
                if (failure.Fatal)
                    break;
            }

            _splash.Done();

            StudioLaunch.Mark(Short(stage.Key));
        }

        Elapsed = clock.Elapsed;

        return new StartupReport(Elapsed, failed);
    }

    /// <summary>
    /// Короткое имя этапа для журнала: <c>splash.stage.settings</c> → <c>settings</c>.
    /// </summary>
    /// <remarks>
    /// В журнал идёт ключ, а не подпись: подпись переводится, а строку отчёта
    /// ищут глазами и грепом — она обязана быть одной и той же на любом языке.
    /// </remarks>
    private static string Short(string key) => key[(key.LastIndexOf('.') + 1)..];

    /// <summary>
    /// Делает этап, чего бы это ни стоило.
    /// </summary>
    /// <remarks>
    /// Исключение здесь — это сломанный файл настроек, занятый словарь, чужой
    /// плагин в каталоге. Ни одно из этого не стоит того, чтобы студия не
    /// открылась: человеку нужна студия, пусть и без языкового пакета, а
    /// причина остаётся в журнале.
    /// </remarks>
    private async Task<StageFailure?> RunAsync(Stage stage, CancellationToken token)
    {
        try
        {
            await stage.Work(token);

            return null;
        }
        catch (Exception e)
        {
            // Целиком, а не одним Message: сообщение без стека не называет ни
            // места, ни причины, и запуск, упавший у человека, остаётся
            // недиагностируемым. Ровно так и вышло с тихим выходом без окна.
            _log.Write(StudioLogLevel.Error, "Startup",
                $"{Localizer.Instance[stage.Key]}: {e}");

            // На экран уходит тип с сообщением, а не стек: человеку нужно
            // название беды, а стек нужен журналу.
            return new StageFailure(stage.Key, $"{e.GetType().Name}: {e.Message}", stage.Fatal);
        }
    }

    /// <summary>Отдаёт поток интерфейса на один проход отрисовки.</summary>
    private static Task Idle() =>
        Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask();

    /// <summary>Этап запуска: подпись, работа и цена его отсутствия.</summary>
    /// <param name="Key">Ключ словаря для подписи.</param>
    /// <param name="Work">Что делается.</param>
    /// <param name="Fatal">Есть ли студия без этого этапа.</param>
    private sealed record Stage(string Key, Func<CancellationToken, Task> Work, bool Fatal);
}

/// <summary>Чего этап не сделал.</summary>
/// <param name="Key">Ключ словаря: какой это был этап.</param>
/// <param name="Reason">Тип и сообщение — то, что показывают человеку.</param>
/// <param name="Fatal">Студии без этого этапа нет.</param>
/// <remarks>
/// Ключ, а не подпись: подпись переводится, а отчёт читают и грепом тоже.
/// </remarks>
public sealed record StageFailure(string Key, string Reason, bool Fatal);

/// <summary>Чем кончился запуск.</summary>
/// <param name="Elapsed">Сколько он занял.</param>
/// <param name="Failed">Этапы, которые не сделали своего, по порядку.</param>
/// <remarks>
/// Пустой список отказов — не единственный хороший исход. Запуск с парой
/// ослабленных отказов — это открытая студия без языкового пакета, и она нужнее
/// закрытой; отличать её от совсем удавшейся надо не для того, чтобы отказать, а
/// для того, чтобы сказать человеку, чего у него сегодня нет.
/// </remarks>
public sealed record StartupReport(TimeSpan Elapsed, IReadOnlyList<StageFailure> Failed)
{
    /// <summary>Упал этап, без которого студии не бывает.</summary>
    public bool Broken => Failed.Any(failure => failure.Fatal);

    /// <summary>
    /// Что сказать человеку о неполном запуске; <c>null</c> — говорить нечего.
    /// </summary>
    /// <remarks>
    /// Ослабленный отказ — это открытая студия без языкового пакета или без
    /// панелей модуля. Что чего-то нет, человек увидит и без нас; чего он не
    /// увидит — так это причины, а журнал сам о себе не напомнит.
    /// <para>
    /// Перечислять упавшие этапы здесь незачем: их подписи — это «Чтение
    /// настроек…», фразы о ходе, а не о беде, и склеенные в строку они читались
    /// бы как список дел. Имена и стек уже в журнале, и строка отправляет
    /// именно туда.
    /// </para>
    /// </remarks>
    public string? Complaint => Failed.Count == 0 ? null : Localizer.Instance["startup.degraded"];
}
