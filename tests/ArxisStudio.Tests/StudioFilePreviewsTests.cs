using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Превью файлов студии: поставщик по расширению, один на расширение и снимаемый со своим плагином;
/// чужой код — через шов сбоев, перемены — в потоке интерфейса.
/// </summary>
/// <remarks>
/// Плагины здесь — обёртки реестра под разными идентификаторами: так их выдаёт фабрика контекстов, а
/// поднимать ради этого сборки незачем. Что фабрика выдаёт обёртку каждому, видно у дизайнера и окна
/// проекта: первый ставит поставщика через свой контекст, второе через свой спрашивает.
/// </remarks>
public class StudioFilePreviewsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Превью файла спрашивается у поставщика его расширения без учёта регистра; объявленное не
    /// расширением не встаёт, и журнал говорит почему.
    /// </summary>
    [AvaloniaFact]
    public async Task A_file_is_previewed_by_the_provider_of_its_extension_in_any_case()
    {
        var log = new StudioLog();
        var registry = new StudioFilePreviews(log, new PluginGuard());
        var forms = new PluginFilePreviews(registry, "arxis.forms");
        var provider = new Provider(".axaml", "png", "*.svg");

        using var registration = forms.Register(provider);

        Assert.True(forms.CanPreview(@"C:\App\MainWindow.AXAML"), "регистр расширения различён");
        Assert.False(forms.CanPreview(@"C:\App\Program.cs"));
        Assert.False(forms.CanPreview(@"C:\App\axaml"), "файл без расширения взят по имени");
        Assert.False(forms.CanPreview(@"C:\App\logo.png"), "расширение без точки встало");

        var preview = await forms.GetAsync(@"C:\App\MainWindow.AXAML", 96, Token);

        Assert.Equal(Provider.Image, preview!.Image.ToArray());
        Assert.Equal((@"C:\App\MainWindow.AXAML", 96), Assert.Single(provider.Asked));
        Assert.Null(await forms.GetAsync(@"C:\App\Program.cs", 96, Token));
        Assert.Equal(2, log.Records.Count(record => record.Source == "Previews" && record.Level == StudioLogLevel.Warning));
    }

    /// <summary>
    /// Расширение, занятое другим плагином, остаётся за первым, а журнал называет обоих; своё плагин
    /// переставляет, и снятие прежней записи новую не трогает.
    /// </summary>
    [AvaloniaFact]
    public async Task An_extension_stays_with_the_plugin_that_took_it_first()
    {
        var log = new StudioLog();
        var registry = new StudioFilePreviews(log, new PluginGuard());
        var forms = new PluginFilePreviews(registry, "arxis.forms");
        var first = new Provider(".axaml");
        var stranger = new Provider(".AXAML", ".ttf");

        var kept = forms.Register(first);
        var refused = new PluginFilePreviews(registry, "acme.fonts").Register(stranger);

        await registry.GetAsync("Form.axaml", 64, Token);

        Assert.Single(first.Asked);
        Assert.Empty(stranger.Asked);
        Assert.True(registry.CanPreview("Sans.ttf"), "свободное расширение второго не встало");
        Assert.Contains(log.Records, record =>
            record.Message.Contains("acme.fonts", StringComparison.Ordinal) && record.Message.Contains("arxis.forms", StringComparison.Ordinal));

        refused.Dispose();

        Assert.True(registry.CanPreview("Form.axaml"), "снятие отказанного сняло чужое");
        Assert.False(registry.CanPreview("Sans.ttf"), "снятая запись оставила своё");

        // Тот же поставщик, поставленный заново: вытесненная запись уже ничего не держит.
        using var again = forms.Register(first);

        kept.Dispose();

        Assert.True(registry.CanPreview("Form.axaml"), "снятие вытесненной записи сняло новую");
    }

    /// <summary>Ушедший плагин уносит своих поставщиков и подписки, а чужих не трогает.</summary>
    [AvaloniaFact]
    public void A_plugin_taken_away_takes_its_providers_and_subscriptions_with_it()
    {
        var registry = new StudioFilePreviews(new StudioLog(), new PluginGuard());
        var forms = new PluginFilePreviews(registry, "arxis.forms");
        var window = new PluginFilePreviews(registry, "arxis.project");
        var heard = new List<string>();

        forms.Register(new Provider(".axaml"));
        forms.Changed += (_, _) => heard.Add("arxis.forms");
        window.Changed += (_, _) => heard.Add("arxis.project");

        registry.RemoveOwnedBy("arxis.forms");
        registry.Invalidate("Form.axaml");
        Dispatcher.UIThread.RunJobs();

        Assert.False(registry.CanPreview("Form.axaml"), "поставщик ушедшего плагина остался");
        Assert.Equal(["arxis.project"], heard);
    }

    /// <summary>Упавший поставщик отвечает пустотой, и падение засчитано ему, а не студии.</summary>
    [AvaloniaFact]
    public async Task A_failing_provider_answers_nothing_and_the_failure_is_its_own()
    {
        var guard = new PluginGuard();
        var failures = new List<PluginFailure>();
        var registry = new StudioFilePreviews(new StudioLog(), guard);

        guard.Failed += (_, failure) => failures.Add(failure);

        using var registration = new PluginFilePreviews(registry, "acme.broken").Register(new Provider(".axaml") { Fails = true });

        Assert.Null(await registry.GetAsync("Form.axaml", 64, Token));
        Assert.Equal("acme.broken", Assert.Single(failures).PluginId);
    }

    /// <summary>
    /// Отменённый вопрос бросает отмену спросившему, а поставщику не засчитывается: плитка ушла из вида,
    /// и он тут ни при чём.
    /// </summary>
    [AvaloniaFact]
    public async Task A_question_cancelled_while_the_provider_works_is_not_its_failure()
    {
        var guard = new PluginGuard();
        var failures = new List<PluginFailure>();
        var registry = new StudioFilePreviews(new StudioLog(), guard);
        var provider = new Provider(".axaml") { Hold = new TaskCompletionSource() };

        guard.Failed += (_, failure) => failures.Add(failure);

        using var registration = new PluginFilePreviews(registry, "arxis.forms").Register(provider);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var asked = registry.GetAsync("Form.axaml", 64, cancel.Token);

        await XamlStudio.UntilAsync(() => provider.Asked.Count == 1, "поставщика не спросили");
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asked);
        Assert.Empty(failures);
    }

    /// <summary>
    /// О перемене, объявленной из любого потока, подписчики слышат в потоке интерфейса; упавший
    /// подписчик не мешает остальным, отписавшийся не слышит.
    /// </summary>
    [AvaloniaFact]
    public async Task A_change_announced_from_any_thread_is_heard_on_the_ui_thread()
    {
        var guard = new PluginGuard();
        var failures = new List<PluginFailure>();
        var registry = new StudioFilePreviews(new StudioLog(), guard);
        var heard = new List<(string Path, bool OnUiThread)>();
        var forms = new PluginFilePreviews(registry, "arxis.forms");
        var window = new PluginFilePreviews(registry, "arxis.project");
        EventHandler<FilePreviewChangedEventArgs> gone = (_, _) => heard.Add(("отписавшийся", true));

        guard.Failed += (_, failure) => failures.Add(failure);
        new PluginFilePreviews(registry, "acme.broken").Changed += (_, _) => throw new InvalidOperationException("подписчик упал");
        window.Changed += (_, e) => heard.Add((e.FilePath, Dispatcher.UIThread.CheckAccess()));
        window.Changed += gone;
        window.Changed -= gone;

        await Task.Run(() => forms.Invalidate(@"C:\App\MainWindow.axaml"), Token);
        await XamlStudio.UntilAsync(() => heard.Count > 0, "перемену не услышали");

        Assert.Equal((@"C:\App\MainWindow.axaml", true), Assert.Single(heard));
        Assert.Equal("acme.broken", Assert.Single(failures).PluginId);
    }

    /// <summary>Поставщик теста: отдаёт одну картинку и помнит, о чём его спросили.</summary>
    /// <param name="extensions">Что он объявляет.</param>
    private sealed class Provider(params string[] extensions) : IFilePreviewProvider
    {
        /// <summary>Картинка, которую он отдаёт: реестр её не читает, а только передаёт.</summary>
        public static readonly byte[] Image = [0x89, 0x50, 0x4E, 0x47];

        public IReadOnlyList<string> Extensions { get; } = extensions;

        /// <summary>О чём спросили.</summary>
        public List<(string Path, int Pixels)> Asked { get; } = [];

        /// <summary>Падает — после первого ожидания, как падает асинхронный код.</summary>
        public bool Fails { get; init; }

        /// <summary>Держит ответ, пока его не отменят.</summary>
        public TaskCompletionSource? Hold { get; init; }

        public async Task<FilePreview?> GetPreviewAsync(string filePath, int pixels, CancellationToken cancellationToken)
        {
            Asked.Add((filePath, pixels));

            await Task.Yield();

            if (Fails)
                throw new InvalidOperationException("поставщик упал");

            if (Hold is { } hold)
                await hold.Task.WaitAsync(cancellationToken);

            return new FilePreview(Image);
        }
    }
}
