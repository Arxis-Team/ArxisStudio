using ArxisStudio.Sdk;

namespace ArxisStudio.Services;

/// <summary>
/// Возвращает студии состояние, в котором её перезапустили: проект, документы, вкладки,
/// каретку и окно настроек.
/// </summary>
/// <remarks>
/// Порядок несущий, и он здесь целиком. Проект первым: его загрузка ставит панели и может выбрать
/// свои вкладки. Документы — после, по одному в порядке открытия: каждый открытый документ
/// показывается, и выбор в его группе переходит к нему. Поэтому следом выборы групп ставятся
/// заново, показанный документ — последним из них, а за ним каретка. Окно настроек — в самом
/// конце: оно модальное, и каретку главного окна поставить под ним было бы некуда.
/// <para>
/// Каждый шаг идёт порознь и пишет о своём отказе в журнал: документ, файла которого больше нет,
/// не мешает открыться остальным, а упавший проект — вернуть окно настроек.
/// </para>
/// <para>
/// Чем делать шаги, служба не знает — ей их дают: главное окно в наборе тестов не строит никто, а
/// порядок проверять нужно.
/// </para>
/// </remarks>
internal sealed class StudioResume
{
    /// <summary>Журнал: сюда пишутся причины перезапуска и отказы шагов.</summary>
    public required IStudioLog Log { get; init; }

    /// <summary>Открывает проект и ждёт, пока он загрузится.</summary>
    public required Func<string, Task> OpenProject { get; init; }

    /// <summary>Открывает документ.</summary>
    public required Func<string, Task> OpenDocument { get; init; }

    /// <summary>Выбирает вкладку в её группе — по имени в раскладке.</summary>
    public required Action<string> Show { get; init; }

    /// <summary>Отдаёт каретку панели или документу — по имени в раскладке.</summary>
    public required Action<string> Focus { get; init; }

    /// <summary>
    /// Открывает окно настроек. Не ждёт его закрытия: окно модальное и живёт, сколько захочет
    /// человек.
    /// </summary>
    public required Action<SettingsSession> OpenSettings { get; init; }

    /// <summary>Есть ли файл: документ могли удалить, пока студия перезапускалась.</summary>
    public Func<string, bool> Exists { get; init; } = File.Exists;

    /// <summary>Ставит сессию на место.</summary>
    /// <param name="session">Что было открыто в прежней копии.</param>
    public async Task RunAsync(StudioSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        Explain(Log, session);

        if (session.Project is { } project)
            await StepAsync($"проект {project}", () => OpenProject(project));

        foreach (var path in session.Documents)
        {
            if (!Exists(path))
            {
                Log.Write(StudioLogLevel.Warning, "Restart", $"{path}: файла больше нет — документ не открыт");
                continue;
            }

            await StepAsync($"документ {path}", () => OpenDocument(path));
        }

        foreach (var id in session.Onstage)
            Step($"вкладка {id}", () => Show(id));

        if (session.Active is { } active)
            Step($"показанный документ {active}", () => Show(active));

        if (session.Focused is { } focused)
            Step($"каретка в {focused}", () => Focus(focused));

        if (session.Settings is { } settings)
            Step("окно настроек", () => OpenSettings(settings));
    }

    /// <summary>
    /// Пишет в журнал, ради чего студию перезапускали.
    /// </summary>
    /// <param name="log">Журнал новой копии.</param>
    /// <param name="session">Сессия прежней.</param>
    /// <remarks>
    /// Журнал прежней копии умер вместе с ней, а автору плагина, согласившемуся на перезапуск,
    /// нужен ответ на вопрос «что держало мой код». Одно место на окно студии и на Welcome: две
    /// записи одного и того же разошлись бы словами.
    /// </remarks>
    public static void Explain(IStudioLog log, StudioSession session)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(session);

        foreach (var (plugin, reason) in session.Reasons)
            log.Write(StudioLogLevel.Info, "Restart", $"{plugin}: перезапуск применил изменения — {reason}");
    }

    private void Step(string what, Action act)
    {
        try
        {
            act();
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            Complain(what, e);
        }
    }

    private async Task StepAsync(string what, Func<Task> act)
    {
        try
        {
            await act();
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            Complain(what, e);
        }
    }

    private void Complain(string what, Exception e) =>
        Log.Write(StudioLogLevel.Error, "Restart", $"{what}: не восстановлено — {e.Message}");
}
