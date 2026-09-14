using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Splash;
using ArxisStudio.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

    /// <summary>
    /// Студия одевается в выбранный язык до заставки, а не посреди неё.
    /// </summary>
    /// <remarks>
    /// Тема и плотность ставились до первого кадра, язык — этапом запуска, уже при показанной
    /// заставке: у выбравшего русский строка версии и первый этап начинались по-английски и
    /// переключались на полпути. Тема здесь подаётся той, что уже стоит, а ступень плотности —
    /// обычной, и обе возвращаются как были: соседние по прогону классы на них смотрят.
    /// </remarks>
    [AvaloniaFact]
    public void The_studio_is_dressed_in_the_chosen_language_before_the_splash()
    {
        var app = Application.Current!;
        var variant = app.RequestedThemeVariant;
        var tiers = DensityTiers();

        try
        {
            Localizer.Instance.SetLanguage("en");

            string english;

            using (var before = new SplashViewModel())
                english = before.Edition;

            App.Dress(new Shell.Settings.StudioSettings
            {
                Theme = app.ActualThemeVariant == ThemeVariant.Light
                    ? Shell.Settings.StudioTheme.Light
                    : Shell.Settings.StudioTheme.Dark,
                Language = "ru",
            });

            using var model = new SplashViewModel();

            Assert.Equal("ru", Localizer.Instance.Language);
            Assert.NotEqual(english, model.Edition);
        }
        finally
        {
            app.RequestedThemeVariant = variant;

            foreach (var tier in DensityTiers())
                app.Resources.MergedDictionaries.Remove(tier);

            foreach (var tier in tiers)
                app.Resources.MergedDictionaries.Add(tier);
        }
    }

    /// <summary>
    /// Язык, который принесёт только пакет плагина, до заставки не ставится — и не ломает её.
    /// </summary>
    /// <remarks>
    /// Пакеты разбираются этапом запуска, раньше их нет. Отказ словаря ничего не меняет: студия
    /// остаётся на запасном языке, а этап языка переключит её, когда пакет будет поставлен.
    /// </remarks>
    [AvaloniaFact]
    public void A_language_only_a_plugin_brings_waits_for_its_stage()
    {
        var app = Application.Current!;
        var variant = app.RequestedThemeVariant;
        var tiers = DensityTiers();

        try
        {
            Localizer.Instance.SetLanguage("en");

            App.Dress(new Shell.Settings.StudioSettings
            {
                Theme = app.ActualThemeVariant == ThemeVariant.Light
                    ? Shell.Settings.StudioTheme.Light
                    : Shell.Settings.StudioTheme.Dark,
                Language = "tlh",
            });

            Assert.Equal("en", Localizer.Instance.Language);
        }
        finally
        {
            app.RequestedThemeVariant = variant;

            foreach (var tier in DensityTiers())
                app.Resources.MergedDictionaries.Remove(tier);

            foreach (var tier in tiers)
                app.Resources.MergedDictionaries.Add(tier);
        }
    }

    private static List<Avalonia.Markup.Xaml.Styling.ResourceInclude> DensityTiers() =>
        [.. Application.Current!.Resources.MergedDictionaries
            .OfType<Avalonia.Markup.Xaml.Styling.ResourceInclude>()
            .Where(include => include.Source?.OriginalString.Contains("/Density/", StringComparison.Ordinal) == true)];

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

    /// <summary>
    /// Удавшемуся запуску сказать человеку нечего.
    /// </summary>
    /// <remarks>
    /// Сообщение о неполном запуске должно что-то значить. Появившись у каждого
    /// запуска, оно значило бы «студия открылась» и перестало бы читаться
    /// первым же вечером.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_startup_that_succeeded_has_nothing_to_say()
    {
        var report = await new StudioStartup(new SplashViewModel(), _log)
            .Add("splash.stage.paths", () => { })
            .RunAsync();

        Assert.Null(report.Complaint);
    }

    /// <summary>
    /// Неполный запуск говорит о себе и отправляет в журнал.
    /// </summary>
    /// <remarks>
    /// Студия без языкового пакета — открытая студия, и это верно. Но человек
    /// видит следствие, а причина остаётся в журнале, о котором он не знает.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_startup_that_fell_short_says_so()
    {
        var report = await new StudioStartup(new SplashViewModel(), _log)
            .Add("splash.stage.language", () => throw new InvalidOperationException("пакет испорчен"))
            .Add("splash.stage.theme", () => { })
            .RunAsync();

        Assert.False(report.Broken, "ослабленный отказ студии не отменяет");
        Assert.Equal(Localizer.Instance["startup.degraded"], report.Complaint);
    }

    /// <summary>
    /// Роковой этап останавливает список, а не портит следующие.
    /// </summary>
    /// <remarks>
    /// Без этого правила сборка оболочки, не давшая окна, роняла три следующих
    /// этапа об пустое место: в журнал шло четыре исключения, из которых три —
    /// о последствии, и ни одно не называло причину первым.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_fatal_stage_stops_the_list_instead_of_spoiling_the_rest()
    {
        var after = false;

        var report = await new StudioStartup(new SplashViewModel(), _log)
            .Add("splash.stage.shell", () => throw new InvalidOperationException("оболочка не собралась"), fatal: true)
            .Add("splash.stage.modules", () => after = true)
            .RunAsync();

        Assert.False(after, "этап после рокового брался бы за то, чего нет");

        Assert.True(report.Broken);

        var failure = Assert.Single(report.Failed);

        Assert.Equal("splash.stage.shell", failure.Key);
        Assert.True(failure.Fatal);
        Assert.Contains("оболочка не собралась", failure.Reason, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), failure.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ослабленный отказ попадает в отчёт и не ломает запуск.
    /// </summary>
    /// <remarks>
    /// Испорченный языковой пакет — это студия без пакета, а не отсутствие
    /// студии. Но и молчать о нём нельзя: человеку надо сказать, чего у него
    /// сегодня нет.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_stage_that_is_not_fatal_is_reported_and_forgiven()
    {
        var after = false;

        var report = await new StudioStartup(new SplashViewModel(), _log)
            .Add("splash.stage.language", () => throw new InvalidOperationException("пакет испорчен"))
            .Add("splash.stage.theme", () => after = true)
            .RunAsync();

        Assert.True(after, "ослабленный отказ не повод не открыть студию");

        Assert.False(report.Broken);
        Assert.Equal("splash.stage.language", Assert.Single(report.Failed).Key);
    }

    /// <summary>
    /// Этапу, которому нужно время, его дают.
    /// </summary>
    /// <remarks>
    /// Обход каталога плагинов — это диск, а диск бывает сетевым. Синхронный
    /// этап держит поток отрисовки, и заставка замирает ровно там, где должна
    /// была рассказывать.
    /// </remarks>
    [AvaloniaFact]
    public async Task An_asynchronous_stage_is_awaited_to_its_end()
    {
        var order = new List<string>();
        var gate = new TaskCompletionSource();

        var running = new StudioStartup(new SplashViewModel(), _log)
            .Add("splash.stage.plugins", async _ =>
            {
                order.Add("начали");

                await gate.Task;

                order.Add("кончили");
            })
            .Add("splash.stage.theme", () => order.Add("следующий"))
            .RunAsync();

        Dispatcher.UIThread.RunJobs();

        // Держать этап заслонкой, а не временем: Task.Yield успевает
        // завершиться на той же уступке потока, что стоит между этапами, и
        // порядок совпадал бы даже у незаконченного этапа. Проверено поломкой:
        // с Yield проверка проходила и без ожидания.
        Assert.Equal(["начали"], order);

        gate.SetResult();

        await running;

        Assert.Equal(["начали", "кончили", "следующий"], order);
    }

    /// <summary>Отмена доезжает до этапа.</summary>
    /// <remarks>
    /// Сторож запуска придёт отдельной работой, но дорога для него должна быть
    /// проложена раньше: этап, не видящий токена, нельзя ни прервать, ни
    /// ограничить по времени.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_token_reaches_the_stage()
    {
        using var source = new CancellationTokenSource();
        var seen = false;

        await source.CancelAsync();

        await new StudioStartup(new SplashViewModel(), _log)
            .Add("splash.stage.paths", token =>
            {
                seen = token.IsCancellationRequested;

                return Task.CompletedTask;
            })
            .RunAsync(source.Token);

        Assert.True(seen, "этап получил чужой токен вместо переданного");
    }

    /// <summary>
    /// До первого кадра доля не объявляется.
    /// </summary>
    /// <remarks>
    /// Объявленная раньше кадра, она делала бегущую полосу невидимой: заставка
    /// появлялась уже с долей, и состояние «считать пока не по чему» не видел
    /// никто, хотя проверялось оно исправно.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_share_is_not_announced_before_the_first_frame()
    {
        var model = new SplashViewModel();

        var running = new StudioStartup(model, _log)
            .Add("splash.stage.paths", () => { })
            .RunAsync();

        Assert.True(model.IsIndeterminate, "доля объявлена до первого кадра — полоса не побежит ни разу");

        await running;

        Assert.False(model.IsIndeterminate);
    }

    /// <summary>
    /// Упавший этап оставляет в журнале стек, а не одну строку.
    /// </summary>
    /// <remarks>
    /// Сообщение без стека не называет ни места, ни причины. Тихий выход без
    /// окна прожил ровно столько, сколько журнал писал один <c>Message</c>:
    /// причина была видна в коде и невидима в отчёте.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_fallen_stage_leaves_its_stack_in_the_log()
    {
        await new StudioStartup(new SplashViewModel(), _log)
            .Add("splash.stage.settings", () => throw new InvalidOperationException("файл занят"))
            .RunAsync();

        var record = Assert.Single(_log.Records, record => record.Level == StudioLogLevel.Error);

        // Имя типа есть у ToString и нет у Message — по нему и видно, что записано.
        Assert.Contains(nameof(InvalidOperationException), record.Message, StringComparison.Ordinal);
        // И сам стек: в нём стоит тип, через который прошёл вызов. Проверять
        // «at» нельзя — в стеке .NET это слово переводится вместе со средой.
        Assert.Contains(nameof(StudioStartup), record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Отказавший запуск говорит об этом, а не называет этап.
    /// </summary>
    /// <remarks>
    /// Имя этапа, на котором всё встало, читалось бы как «идёт», хотя не идёт
    /// уже ничего. Причина при этом не переводится: её несут в отчёт о сбое.
    /// </remarks>
    [AvaloniaFact]
    public void Failing_says_so_instead_of_naming_a_stage()
    {
        var model = new SplashViewModel();
        var heard = new List<string>();

        model.Begin("Сборка оболочки…");
        model.PropertyChanged += (_, e) => heard.Add(e.PropertyName ?? string.Empty);

        model.Fail("InvalidOperationException: файл занят");

        Assert.True(model.HasFailed);
        Assert.Equal("InvalidOperationException: файл занят", model.Failure);
        Assert.Equal(Localizer.Instance["splash.failed"], model.Stage);

        Assert.Contains(nameof(model.HasFailed), heard);
        Assert.Contains(nameof(model.Stage), heard);
    }

    /// <summary>
    /// Отказавшая заставка называет причину и даёт выход.
    /// </summary>
    /// <remarks>
    /// Рамы у окна нет вовсе, и без кнопки человек остался бы с незакрываемой
    /// заставкой — хуже, чем с тихим выходом. Полоса хода при этом уходит:
    /// бегущая, она обещала бы, что студия ещё поднимается.
    /// </remarks>
    [AvaloniaFact]
    public void A_failed_splash_names_the_cause_and_gives_a_way_out()
    {
        var model = new SplashViewModel();
        var splash = new SplashWindow(model);

        splash.Show();
        Dispatcher.UIThread.RunJobs();

        model.Expect(3);
        model.Begin("Сборка оболочки…");

        Dispatcher.UIThread.RunJobs();

        // Видно ли человеку, а не заведён ли контрол: скрытый родитель детей
        // из дерева не убирает, и наличие кнопки о ней ничего не говорит.
        Assert.True(Bar(splash).IsEffectivelyVisible, "на идущем запуске полоса хода обязана быть видна");
        Assert.False(Way(splash).IsEffectivelyVisible, "до отказа выходу на заставке делать нечего");

        model.Fail("InvalidOperationException: файл занят");

        Dispatcher.UIThread.RunJobs();

        var shown = Texts(splash);

        Assert.Contains(Localizer.Instance["splash.failed"], shown);
        Assert.Contains("InvalidOperationException: файл занят", shown);

        Assert.False(Bar(splash).IsEffectivelyVisible, "после отказа полоса хода обещала бы ход, которого нет");
        Assert.True(Way(splash).IsEffectivelyVisible, "рамы у окна нет — без кнопки заставку нечем закрыть");

        splash.Close();
    }

    /// <summary>
    /// Esc закрывает заставку, но только отказавшую.
    /// </summary>
    /// <remarks>
    /// На идущем запуске отменять нечем: этапы про отмену пока не знают, и
    /// закрытое окно оставило бы студию поднимающейся втайне.
    /// </remarks>
    [AvaloniaFact]
    public void Escape_closes_the_splash_only_after_it_failed()
    {
        var model = new SplashViewModel();
        var splash = new SplashWindow(model);
        var closed = false;

        splash.Closed += (_, _) => closed = true;

        splash.Show();
        Dispatcher.UIThread.RunJobs();

        splash.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.False(closed, "идущий запуск отменять нечем — заставка обязана остаться");

        model.Fail("InvalidOperationException: файл занят");

        Dispatcher.UIThread.RunJobs();

        splash.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed, "отказавшую заставку обязан закрывать Esc");
    }

    /// <summary>
    /// Часы заставки идут с её появления, а не с её создания.
    /// </summary>
    /// <remarks>
    /// Отсчёт стоял в инициализаторе поля — то есть до разбора разметки и
    /// задолго до показа. Заставка «бывала на экране» то время, пока её
    /// собирали, и срок, ради которого правило заведено, выходил раньше, чем
    /// человек что-либо видел.
    /// </remarks>
    [AvaloniaFact]
    public void The_clock_of_the_splash_starts_when_it_opens()
    {
        var splash = new SplashWindow(new SplashViewModel());

        Assert.Equal(TimeSpan.Zero, splash.Visible);

        splash.Show();
        Dispatcher.UIThread.RunJobs();

        splash.Close();
    }

    /// <summary>
    /// Последний кадр отдают всегда, даже когда ждать уже нечего.
    /// </summary>
    /// <remarks>
    /// Когда этапы заняли больше срока, <c>Task.Delay(Zero)</c> завершается
    /// синхронно: продолжение идёт тем же заходом, прохода отрисовки не
    /// случается, и кадр со стопроцентной полосой не рисуется никогда —
    /// заставка замирает на предпоследнем этапе и так и уходит.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_last_frame_is_given_its_pass_even_when_there_is_nothing_to_wait_for()
    {
        var splash = new SplashWindow(new SplashViewModel());
        var painted = false;

        splash.Show();
        Dispatcher.UIThread.RunJobs();

        Dispatcher.UIThread.Post(() => painted = true, DispatcherPriority.Background);

        // Срок вышел: ждать нечего, и без безусловной уступки потока очередь
        // отрисовки не сдвинулась бы ни на шаг.
        await splash.LingerAsync(SplashWindow.Patience + TimeSpan.FromSeconds(1));

        Assert.True(painted, "кадр со стопроцентной полосой не был бы нарисован никогда");

        splash.Close();
    }

    /// <summary>
    /// Заставка стоит в панели задач.
    /// </summary>
    /// <remarks>
    /// Почти секунду это единственное окно студии. Без кнопки человек, ушедший
    /// на Alt+Tab в ту самую секунду, которую он и ждёт, вернуться к ней не
    /// может. «Заставка — не окно» здесь проигрывает.
    /// </remarks>
    [AvaloniaFact]
    public void The_splash_stands_in_the_taskbar()
    {
        var splash = new SplashWindow(new SplashViewModel());

        Assert.True(splash.ShowInTaskbar);
    }

    /// <summary>
    /// Отпущенная модель перестаёт слушать язык студии.
    /// </summary>
    /// <remarks>
    /// <c>Localizer</c> один на процесс и живёт дольше заставки. Подписка,
    /// сделанная лямбдой в конструкторе, не снималась никогда: модель
    /// оставалась жива подпиской на синглтон, и в прогоне их копилось по одной
    /// на каждый тест.
    /// </remarks>
    [AvaloniaFact]
    public void A_model_that_was_let_go_stops_listening_to_the_language()
    {
        var model = new SplashViewModel();
        var heard = 0;

        model.PropertyChanged += (_, _) => heard++;

        Localizer.Instance.SetLanguage("ru");

        Assert.True(heard > 0, "живая модель обязана слышать смену языка");

        model.Dispose();

        var quiet = heard;

        Localizer.Instance.SetLanguage("en");

        Assert.Equal(quiet, heard);
    }

    /// <summary>Полоса хода заставки — она на ней одна.</summary>
    private static AxProgressBar Bar(Visual root) =>
        root.GetVisualDescendants().OfType<AxProgressBar>().Single();

    /// <summary>Выход с отказавшей заставки — кнопка на ней тоже одна.</summary>
    private static AxButton Way(Visual root) =>
        root.GetVisualDescendants().OfType<AxButton>().Single();

    /// <summary>Всё, что заставка написала на экране.</summary>
    private static IReadOnlyList<string> Texts(Visual root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? string.Empty)];
}
