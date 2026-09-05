using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Splash;
using ArxisStudio.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Заставка запуска: что студия рассказывает о себе, пока поднимается.
/// </summary>
/// <remarks>
/// От запуска процесса до первого окна проходит около секунды, и до этой работы
/// на экране не было ничего. Проверяется здесь не картинка, а договор под ней:
/// порядок этапов, доля, поведение упавшего этапа и то, что подпись объявляется
/// до работы, а не после.
/// <para>
/// Очередь общая с остальными: подписи этапов берутся из словарей, а
/// <c>Localizer</c> один на процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class SplashTests : IDisposable
{
    private readonly StudioLog _log = new();

    /// <summary>Возвращает студии язык, на котором её застали.</summary>
    public void Dispose()
    {
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Пока этапов не объявили, полоса бежит.
    /// </summary>
    /// <remarks>
    /// Показать ноль значило бы сказать «сделано нисколько»; честный ответ —
    /// «считать пока не по чему».
    /// </remarks>
    [AvaloniaFact]
    public void Before_the_stages_are_known_the_bar_runs()
    {
        var model = new SplashViewModel();

        Assert.True(model.IsIndeterminate);
        Assert.Equal(0d, model.Progress);
        Assert.Equal(string.Empty, model.Stage);
    }

    /// <summary>Доля растёт с каждым пройденным этапом.</summary>
    [AvaloniaFact]
    public void The_share_grows_with_every_stage_passed()
    {
        var model = new SplashViewModel();

        model.Expect(4);

        Assert.False(model.IsIndeterminate);
        Assert.Equal(0d, model.Progress);

        model.Done();
        model.Done();

        Assert.Equal(50d, model.Progress);
    }

    /// <summary>
    /// Доля не переваливает за конец, сколько ни отчитывайся.
    /// </summary>
    /// <remarks>
    /// Этап, отчитавшийся дважды, — это ошибка зовущего, а не повод показать
    /// человеку полосу, ушедшую за край.
    /// </remarks>
    [AvaloniaFact]
    public void The_share_never_passes_the_end()
    {
        var model = new SplashViewModel();

        model.Expect(2);

        for (var extra = 0; extra < 5; extra++)
            model.Done();

        Assert.Equal(100d, model.Progress);
    }

    /// <summary>Смена подписи объявляется — иначе привязка о ней не узнает.</summary>
    [AvaloniaFact]
    public void Beginning_a_stage_announces_it()
    {
        var model = new SplashViewModel();
        var heard = new List<string>();

        model.PropertyChanged += (_, e) => heard.Add(e.PropertyName ?? string.Empty);

        model.Begin("Чтение настроек…");

        Assert.Equal("Чтение настроек…", model.Stage);
        Assert.Contains(nameof(model.Stage), heard);
    }

    /// <summary>Релиз и сборка пишутся одной строкой.</summary>
    [AvaloniaFact]
    public void The_version_reads_as_one_line()
    {
        var model = new SplashViewModel();

        Assert.Contains(StudioRelease.Version, model.Edition, StringComparison.Ordinal);
        Assert.Contains(StudioRelease.Build, model.Edition, StringComparison.Ordinal);
        Assert.Contains(StudioRelease.Toolkit, model.Credit, StringComparison.Ordinal);
    }

    /// <summary>
    /// Номер сборки идёт от версии файла, а не от версии связывания.
    /// </summary>
    /// <remarks>
    /// Версия сборки у студии закреплена навсегда — ею плагины ссылаются на
    /// SDK. Номер, который человек называет в отчёте о сбое, обязан меняться, и
    /// берётся он из версии файла. Сойдись они — значит версия файла молча
    /// поехала за закреплённой единицей, и заставка показывает «1.0.0» на любой
    /// сборке. Так однажды и вышло.
    /// </remarks>
    [Fact]
    public void The_build_number_is_not_the_binding_identity()
    {
        var binding = typeof(StudioRelease).Assembly.GetName().Version?.ToString(3);

        Assert.NotEqual(binding, StudioRelease.Build);
    }

    /// <summary>
    /// Смена языка доходит до строки версии.
    /// </summary>
    /// <remarks>
    /// Язык выбирается одним из этапов запуска — то есть при уже показанной
    /// заставке. Слово «сборка» в строке версии обязано переехать вместе с ним.
    /// </remarks>
    [AvaloniaFact]
    public void A_change_of_language_reaches_the_version_line()
    {
        var model = new SplashViewModel();
        var heard = new List<string>();

        model.PropertyChanged += (_, e) => heard.Add(e.PropertyName ?? string.Empty);

        Localizer.Instance.SetLanguage("ru");

        Assert.Contains(nameof(model.Edition), heard);
        Assert.Contains(Localizer.Instance["splash.build"], model.Edition, StringComparison.Ordinal);
    }

    /// <summary>Этапы идут в том порядке, в каком их назвали.</summary>
    [AvaloniaFact]
    public async Task Stages_run_in_the_order_they_were_added()
    {
        var order = new List<string>();

        await new StudioStartup(new SplashViewModel(), _log)
            .Add("splash.stage.paths", () => order.Add("пути"))
            .Add("splash.stage.settings", () => order.Add("настройки"))
            .Add("splash.stage.theme", () => order.Add("тема"))
            .RunAsync();

        Assert.Equal(["пути", "настройки", "тема"], order);
    }

    /// <summary>
    /// Подпись объявляется до работы, а не после.
    /// </summary>
    /// <remarks>
    /// Ради этого этапы и заведены: человек читает, чем студия занята сейчас.
    /// Подпись, объявленная после работы, рассказывала бы о прошлом — и на
    /// последнем этапе не была бы прочитана вовсе.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_stage_is_announced_before_its_work()
    {
        var model = new SplashViewModel();
        var seen = string.Empty;

        await new StudioStartup(model, _log)
            .Add("splash.stage.settings", () => seen = model.Stage)
            .RunAsync();

        Assert.Equal(Localizer.Instance["splash.stage.settings"], seen);
    }

    /// <summary>
    /// Упавший этап не уносит с собой остальные.
    /// </summary>
    /// <remarks>
    /// Это и есть главное правило запуска: испорченный языковой пакет, занятый
    /// файл настроек, чужая папка в каталоге плагинов — ни одно из этого не
    /// стоит того, чтобы студия не открылась. Причина остаётся в журнале.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_stage_that_falls_does_not_take_the_rest_with_it()
    {
        var after = false;

        var startup = new StudioStartup(new SplashViewModel(), _log)
            .Add("splash.stage.settings", () => throw new InvalidOperationException("файл занят"))
            .Add("splash.stage.theme", () => after = true);

        await startup.RunAsync();

        Assert.True(after, "этап после упавшего не выполнился");

        Assert.Contains(_log.Records, record =>
            record.Level == StudioLogLevel.Error && record.Message.Contains("файл занят"));
    }

    /// <summary>Упавший этап всё равно считается пройденным — полоса не встаёт.</summary>
    [AvaloniaFact]
    public async Task A_fallen_stage_still_moves_the_bar()
    {
        var model = new SplashViewModel();

        await new StudioStartup(model, _log)
            .Add("splash.stage.settings", () => throw new InvalidOperationException("сломалось"))
            .Add("splash.stage.theme", () => { })
            .RunAsync();

        Assert.Equal(100d, model.Progress);
    }

    /// <summary>
    /// Каждый этап отмечается на часах запуска — своим коротким именем.
    /// </summary>
    /// <remarks>
    /// Из этих отметок собирается строка отчёта в журнале: следующему, кто
    /// спросит «почему студия стартует секунду», отвечать будет она, а не
    /// расставленные заново замеры. Имя в отметке — ключ, а не подпись: подпись
    /// переводится, а отчёт ищут грепом.
    /// </remarks>
    [AvaloniaFact]
    public async Task Every_stage_leaves_a_mark_on_the_launch_clock()
    {
        StudioLaunch.Forget();

        var startup = new StudioStartup(new SplashViewModel(), _log)
            .Add("splash.stage.theme", () => { })
            .Add("splash.stage.welcome", () => { });

        await startup.RunAsync();

        Assert.True(startup.Elapsed > TimeSpan.Zero, "запуск не измерил себя");

        Assert.Equal(
            ["кадр", "theme", "welcome"],
            StudioLaunch.Phases.Select(phase => phase.What));

        StudioLaunch.Forget();
    }

    /// <summary>
    /// Заставка держится на экране столько, чтобы её успели прочитать.
    /// </summary>
    /// <remarks>
    /// Замерено: этапы проходят за 174 мс, и без этого правила заставка мелькала
    /// бы вспышкой перед окном. Правило перестанет что-либо задерживать само —
    /// как только работы на запуске станет больше этого срока.
    /// </remarks>
    [Fact]
    public void The_splash_waits_to_be_read()
    {
        Assert.Equal(SplashWindow.Patience, SplashWindow.Rest(TimeSpan.Zero));

        Assert.Equal(
            SplashWindow.Patience - TimeSpan.FromMilliseconds(200),
            SplashWindow.Rest(TimeSpan.FromMilliseconds(200)));

        Assert.Equal(TimeSpan.Zero, SplashWindow.Rest(SplashWindow.Patience));
        Assert.Equal(TimeSpan.Zero, SplashWindow.Rest(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// Заставка рисуется и показывает то, что ей рассказали.
    /// </summary>
    /// <remarks>
    /// Разметка проверяется целиком, вместе с оформлением релиза: знак, водяной
    /// знак и полоса хода — это её части, и сломанная привязка в любой из них
    /// видна здесь, а не на запуске у человека.
    /// </remarks>
    [AvaloniaFact]
    public void The_splash_shows_what_it_was_told()
    {
        var model = new SplashViewModel();
        var splash = new SplashWindow(model);

        splash.Show();
        Dispatcher.UIThread.RunJobs();

        model.Expect(2);
        model.Begin("Словари интерфейса…");
        model.Done();

        Dispatcher.UIThread.RunJobs();

        var shown = Texts(splash);

        Assert.Contains("Словари интерфейса…", shown);
        Assert.Contains(model.Edition, shown);
        Assert.Contains(model.Credit, shown);
        Assert.Contains(model.Runtime, shown);

        var bar = Assert.Single(splash.GetVisualDescendants().OfType<AxProgressBar>());

        Assert.Equal(50d, bar.Value);
        Assert.False(bar.IsIndeterminate);

        splash.Close();
    }

    /// <summary>
    /// Заставка верна в обеих темах.
    /// </summary>
    /// <remarks>
    /// Тема выбирается одним из этапов запуска — то есть у показанной заставки
    /// вариант меняется прямо под ногами. Проверяется здесь именно это: окно,
    /// уже стоящее на экране, обязано перекраситься, а не побелеть.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_splash_is_right_in_both_themes(string theme)
    {
        var model = new SplashViewModel();
        var splash = new SplashWindow(model);

        splash.Show();
        Dispatcher.UIThread.RunJobs();

        model.Begin("Оформление…");
        splash.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

        Dispatcher.UIThread.RunJobs();

        Assert.Contains("Оформление…", Texts(splash));

        var surface = Assert.Single(
            splash.GetVisualDescendants().OfType<Border>(),
            border => border.CornerRadius.TopLeft == 12);

        var paint = Assert.IsType<ISolidColorBrush>(surface.Background, exactMatch: false);
        var text = Assert.Single(
            splash.GetVisualDescendants().OfType<TextBlock>(),
            block => block.Text == "Оформление…");

        var ink = Assert.IsType<ISolidColorBrush>(text.Foreground, exactMatch: false);

        // Не «какой-то цвет», а разный: подложка и текст, сошедшиеся в один
        // цвет, — это и есть побелевшее окно.
        Assert.NotEqual(paint.Color, ink.Color);

        splash.Close();
    }

    /// <summary>Всё, что заставка написала на экране.</summary>
    private static IReadOnlyList<string> Texts(Visual root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? string.Empty)];
}
