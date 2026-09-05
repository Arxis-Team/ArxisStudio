using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Часы запуска: из чего складывается время до первого окна.
/// </summary>
/// <remarks>
/// Отчёт заведён после того, как один и тот же вопрос — «почему студия
/// стартует секунду» — пришлось разбирать дважды, и оба раза расставляя отметки
/// руками. Проверяется здесь его единственное правило: в строке стоит
/// длительность каждой фазы, а не момент её конца. Разница не косметическая —
/// накопительные числа заставляют искать медленное вычитанием в уме, и первый
/// же такой отчёт соврал: стоимость первой отрисовки досталась чтению папок.
/// <para>
/// Отметки общие на процесс: первая ставится в <c>Main</c>, где служб студии
/// ещё нет. Поэтому очередь общая с остальными и уборка за собой.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioLaunchTests : IDisposable
{
    /// <summary>Возвращает часам то состояние, в котором их застали.</summary>
    public void Dispose()
    {
        StudioLaunch.Forget();
        GC.SuppressFinalize(this);
    }

    /// <summary>В отчёте стоит длительность фазы, а не момент её конца.</summary>
    [Fact]
    public void The_report_shows_how_long_each_phase_took()
    {
        var phases = new[]
        {
            new StudioLaunch.Phase("среда", TimeSpan.FromMilliseconds(40)),
            new StudioLaunch.Phase("платформа", TimeSpan.FromMilliseconds(230)),
            new StudioLaunch.Phase("стили", TimeSpan.FromMilliseconds(310)),
        };

        Assert.Equal("Запуск 310 мс: среда 40 платформа 190 стили 80", StudioLaunch.Report(phases));
    }

    /// <summary>Неразмеченный запуск так и говорит, а не показывает пустоту.</summary>
    [Fact]
    public void An_unmarked_launch_says_so()
    {
        StudioLaunch.Forget();

        Assert.Equal("Запуск не размечен", StudioLaunch.Report());
    }

    /// <summary>Отметки ложатся по порядку и со временем, которое не идёт назад.</summary>
    [Fact]
    public void Marks_are_kept_in_order()
    {
        StudioLaunch.Forget();

        StudioLaunch.Mark("первая");
        StudioLaunch.Mark("вторая");

        Assert.Equal(["первая", "вторая"], StudioLaunch.Phases.Select(phase => phase.What));
        Assert.True(StudioLaunch.Phases[0].At <= StudioLaunch.Phases[1].At, "время пошло назад");
        Assert.True(StudioLaunch.Phases[0].At > TimeSpan.Zero, "отметка считается не от запуска процесса");
    }

    /// <summary>Безымянная фаза — ошибка зовущего, а не строка «пусто» в отчёте.</summary>
    [Fact]
    public void A_nameless_phase_is_refused()
    {
        Assert.Throws<ArgumentException>(() => StudioLaunch.Mark("  "));
    }
}
