using System.Diagnostics;
using System.Text;

namespace ArxisStudio.Services;

/// <summary>
/// Часы запуска: из чего складывается время до первого окна.
/// </summary>
/// <remarks>
/// Заведено после того, как один и тот же вопрос — «почему студия стартует
/// секунду» — пришлось разбирать дважды, и оба раза руками: расставить отметки,
/// собрать, прогнать, убрать. Ответ на такой вопрос должен доставаться из
/// журнала, а не из повторной инструментовки.
/// <para>
/// Замеренное однажды: до первого кадра около 720 мс, из них 40 — старт среды,
/// 190 — инициализация платформы, 80 — разбор четырёх слоёв стилей, 150 —
/// построение окна заставки, 110 — создание окна платформы. Всё, кроме стилей и
/// самой заставки, приложению не принадлежит. Числа стареют, отметки — нет.
/// </para>
/// <para>
/// Состояние здесь общее на процесс, и иначе быть не может: первая отметка
/// ставится в <c>Main</c>, где ещё нет ни одной службы студии. Тесты, которые
/// его трогают, идут по очереди и убирают за собой.
/// </para>
/// </remarks>
public static class StudioLaunch
{
    private static readonly List<Phase> Recorded = [];

    /// <summary>
    /// Когда запустился процесс.
    /// </summary>
    /// <remarks>
    /// Считается от старта процесса, а не от первой своей строки: до неё
    /// успевает подняться среда, и это тоже время, которое человек ждёт.
    /// </remarks>
    private static readonly DateTime Started = Process.GetCurrentProcess().StartTime;

    /// <summary>Сколько прошло с запуска процесса.</summary>
    public static TimeSpan Since => DateTime.Now - Started;

    /// <summary>Отмеченные фазы в порядке прохождения.</summary>
    public static IReadOnlyList<Phase> Phases => Recorded;

    /// <summary>
    /// Отмечает пройденную фазу.
    /// </summary>
    /// <param name="what">Чем она была — одним словом, без перевода.</param>
    /// <remarks>
    /// Имя фазы не переводится нарочно: по журналу ищут глазами и грепом, и
    /// строка, меняющаяся вместе с языком интерфейса, ищется вдвое хуже.
    /// </remarks>
    public static void Mark(string what)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(what);

        Recorded.Add(new Phase(what, Since));
    }

    /// <summary>
    /// Складывает отметки в одну строку журнала.
    /// </summary>
    /// <remarks>
    /// Одной строкой, а не таблицей: журнал студии отражается в стандартный
    /// вывод, и человек, запустивший её из терминала, не должен получать
    /// простыню на каждом старте. Показывается длительность каждой фазы, а не
    /// момент её конца: искать медленное по разностям, посчитанным в уме, —
    /// работа, которую делает эта строка.
    /// </remarks>
    public static string Report() => Report(Recorded);

    /// <summary>Тот же отчёт, но по данным фазам — так его и проверяют.</summary>
    /// <param name="phases">Фазы в порядке прохождения.</param>
    internal static string Report(IReadOnlyList<Phase> phases)
    {
        if (phases.Count == 0)
            return "Запуск не размечен";

        var text = new StringBuilder($"Запуск {phases[^1].At.TotalMilliseconds:F0} мс:");
        var previous = TimeSpan.Zero;

        foreach (var phase in phases)
        {
            text.Append($" {phase.What} {(phase.At - previous).TotalMilliseconds:F0}");
            previous = phase.At;
        }

        return text.ToString();
    }

    /// <summary>Забывает отметки — нужно тестам, которые размечают свой запуск.</summary>
    public static void Forget() => Recorded.Clear();

    /// <summary>Пройденная фаза запуска.</summary>
    /// <param name="What">Чем она была.</param>
    /// <param name="At">Когда кончилась, считая от запуска процесса.</param>
    public readonly record struct Phase(string What, TimeSpan At);
}
