using ArxisStudio.Extensibility;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Surface.UiDesigner;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Снимки форм на доске: форма, которая не стоит живой, показывает на своём месте снимок — тот же, что на
/// плитке окна проекта; без снимка — рамку своего размера. Снимок ставится форме на виду и уходит, когда
/// она уходит с виду или с доски, а объявленный поставщиком — встаёт сразу.
/// </summary>
/// <remarks>
/// Снимки отдаёт поставщик теста через службу превью студии — той же дорогой, какой их отдаёт дизайнер:
/// доска знает только службу. Службы XAML здесь нет, и живой форма не встаёт; живые — в
/// <see cref="UiDesignerLiveBoardTests"/>, а как дизайнер снимает формы, проверяет <see cref="FormSnapshotTests"/>.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class UiDesignerBoardPreviewTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui" Width="400" Height="200" />
        """;

    /// <summary>
    /// Форма, которая не стоит живой, показывает снимок на своём месте — во весь свой объявленный размер.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_that_is_not_live_shows_its_snapshot_in_its_place()
    {
        var registry = new StudioFilePreviews(new StudioLog(), new PluginGuard());
        var forms = new Forms { Image = Png(384, 192) };

        using var registration = new PluginFilePreviews(registry, "test.forms").Register(forms);
        using var studio = new UiDesignerStudio(previews: registry);

        await studio.Open(studio.Solution(("MainWindow.axaml", Form)));
        await Shown(studio, studio.Card("MainWindow.axaml"));

        var card = studio.Card("MainWindow.axaml");
        var item = studio.Container(card);

        Assert.Equal(new PixelSize(384, 192), Assert.IsAssignableFrom<Bitmap>(card.Preview).PixelSize);
        Assert.Same(card.Preview, Picture(item));
        Assert.Equal(new Size(400, 200), item.Bounds.Size);
    }

    /// <summary>
    /// Без снимка форма стоит рамкой своего размера — подложкой и контуром темы: на подложке холста пустое
    /// место формы иначе не видно. Зазор доски — в клетку сетки холста.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_without_a_snapshot_stands_as_a_frame_of_its_size()
    {
        var registry = new StudioFilePreviews(new StudioLog(), new PluginGuard());

        using var registration = new PluginFilePreviews(registry, "test.forms").Register(new Forms());
        using var studio = new UiDesignerStudio(previews: registry);

        await studio.Open(studio.Solution(("MainWindow.axaml", Form)));
        await Settled(studio);

        var item = studio.Container(studio.Card("MainWindow.axaml"));

        Assert.True(studio.View.TryFindResource("AxSurfaceBaseBrush", studio.View.ActualThemeVariant, out var surface));
        Assert.True(studio.View.TryFindResource("AxStrokeControlBrush", studio.View.ActualThemeVariant, out var stroke));
        Assert.Same(surface, item.Background);
        Assert.Same(stroke, item.BorderBrush);
        Assert.Equal(new Size(400, 200), item.Bounds.Size);
        Assert.Equal(0, Length(studio, "AxFormBoardGap") % 20);
    }

    /// <summary>Снимок старше файла стоит на месте формы, пока живая не встала: старое лучше пустоты.</summary>
    [AvaloniaFact]
    public async Task A_stale_snapshot_shows_until_the_form_is_live()
    {
        var registry = new StudioFilePreviews(new StudioLog(), new PluginGuard());

        using var registration = new PluginFilePreviews(registry, "test.forms").Register(new Forms { Image = Png(384, 192), Stale = true });
        using var studio = new UiDesignerStudio(previews: registry);

        await studio.Open(studio.Solution(("MainWindow.axaml", Form)));

        var card = studio.Card("MainWindow.axaml");

        await Shown(studio, card);

        Assert.True(card.IsPreviewStale);
        Assert.Same(card.Preview, Picture(studio.Container(card)));
    }

    /// <summary>
    /// Снимок, о котором поставщик объявил, встаёт на видимую форму сразу: форму сняли в фоне, а файл при
    /// этом не менялся.
    /// </summary>
    [AvaloniaFact]
    public async Task A_snapshot_the_provider_announces_reaches_the_card()
    {
        var registry = new StudioFilePreviews(new StudioLog(), new PluginGuard());
        var forms = new Forms();

        using var registration = new PluginFilePreviews(registry, "test.forms").Register(forms);
        using var studio = new UiDesignerStudio(previews: registry);

        await studio.Open(studio.Solution(("MainWindow.axaml", Form)));
        await Settled(studio);

        var card = studio.Card("MainWindow.axaml");

        Assert.Null(card.Preview);
        Assert.Contains(card.Path.Value, forms.Asked);

        forms.Image = Png(384, 192);
        registry.Invalidate(card.Path.Value);

        await Shown(studio, card);

        Assert.Same(card.Preview, Picture(studio.Container(card)));
    }

    /// <summary>
    /// Форма, ушедшая с холста, отпускает снимок вместе с контейнером: растр не живёт дольше того, что его
    /// рисует.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_taken_off_the_board_lets_its_snapshot_go()
    {
        var registry = new StudioFilePreviews(new StudioLog(), new PluginGuard());

        using var registration = new PluginFilePreviews(registry, "test.forms").Register(new Forms { Image = Png(384, 192) });
        using var studio = new UiDesignerStudio(previews: registry);

        await studio.Open(studio.Solution(("A.axaml", Form), ("B.axaml", Form)));

        var a = studio.Card("A.axaml");

        await Shown(studio, a);

        studio.Model.Remove([a]);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(a.Preview);
        Assert.NotNull(studio.Card("B.axaml").Preview);
    }

    /// <summary>
    /// Форма, ушедшая с виду, отпускает снимок — холст держит карточки всех форм, а растры только у видимых, —
    /// и берёт его снова, вернувшись.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_out_of_view_lets_its_snapshot_go_and_takes_it_again_in_view()
    {
        var registry = new StudioFilePreviews(new StudioLog(), new PluginGuard());

        using var registration = new PluginFilePreviews(registry, "test.forms").Register(new Forms { Image = Png(384, 192) });
        using var studio = new UiDesignerStudio(previews: registry);

        await studio.Open(studio.Solution(("A.axaml", Form), ("B.axaml", Form)));

        var a = studio.Card("A.axaml");
        var sheet = studio.View.Sheet;

        await Shown(studio, a);

        var shown = sheet.ViewportLocation;

        sheet.ViewportLocation = new Point(100_000, 100_000);
        Dispatcher.UIThread.RunJobs();
        studio.Panel.Sight!.Update();

        Assert.Null(a.Preview);
        Assert.Null(Picture(studio.Container(a)));
        Assert.NotNull(studio.View.Sheet.ContainerFromItem(a));

        sheet.ViewportLocation = shown;
        Dispatcher.UIThread.RunJobs();
        studio.Panel.Sight.Update();

        await Shown(studio, a);

        Assert.Same(a.Preview, Picture(studio.Container(a)));
    }

    /// <summary>
    /// Снимок, пришедший после того, как форму убрали с холста, на неё не встаёт: держать его некому, и
    /// растр, поставленный форме без контейнера, не освободил бы никто.
    /// </summary>
    [AvaloniaFact]
    public async Task A_snapshot_arriving_after_the_form_left_is_not_put_on_it()
    {
        var registry = new StudioFilePreviews(new StudioLog(), new PluginGuard());
        var forms = new Forms { Image = Png(384, 192), Hold = new TaskCompletionSource() };

        using var registration = new PluginFilePreviews(registry, "test.forms").Register(forms);
        using var studio = new UiDesignerStudio(previews: registry);

        await studio.Open(studio.Solution(("A.axaml", Form), ("B.axaml", Form)));

        var a = studio.Card("A.axaml");

        Assert.Contains(a.Path.Value, forms.Asked);

        studio.Model.Remove([a]);
        Dispatcher.UIThread.RunJobs();
        forms.Hold.SetResult();

        await Shown(studio, studio.Card("B.axaml"));

        Assert.Null(a.Preview);
    }

    /// <summary>Ждёт, пока снимок встанет на карточку.</summary>
    private static async Task Shown(UiDesignerStudio studio, FormCard card)
    {
        await XamlStudio.UntilAsync(
            () =>
            {
                Dispatcher.UIThread.RunJobs();

                return card.Preview is not null;
            },
            $"снимок {card.Name} не встал на форму");

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Ждёт загрузки снимков: спрошенный поставщик ответил.</summary>
    private static async Task Settled(UiDesignerStudio studio)
    {
        Dispatcher.UIThread.RunJobs();
        await studio.Panel.Snapshots!.Loading.WaitAsync(TimeSpan.FromSeconds(30));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Снимок, который форма показывает на своём месте; null — его нет.</summary>
    private static IImageBrushSource? Picture(UiDesignerFormItem item)
    {
        Dispatcher.UIThread.RunJobs();

        return (item.Background as ImageBrush)?.Source;
    }

    private static double Length(UiDesignerStudio studio, string key) =>
        studio.View.TryFindResource(key, studio.View.ActualThemeVariant, out var value) && value is double length
            ? length
            : throw new InvalidOperationException($"у темы нет длины {key}");

    /// <summary>Картинка в PNG — залитый прямоугольник заданного размера.</summary>
    private static byte[] Png(int width, int height)
    {
        var surface = new Border { Width = width, Height = height, Background = Brushes.OrangeRed };

        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));

        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using var stream = new MemoryStream();

        bitmap.Render(surface);
        bitmap.Save(stream, new PngBitmapEncoderOptions());

        return stream.ToArray();
    }

    /// <summary>Поставщик форм теста: картинка, которую ему велели, и что у него спросили.</summary>
    private sealed class Forms : IFilePreviewProvider
    {
        public IReadOnlyList<string> Extensions { get; } = [".axaml"];

        /// <summary>Картинка; пусто — снимка нет.</summary>
        public byte[]? Image { get; set; }

        /// <summary>Снимок старше файла.</summary>
        public bool Stale { get; init; }

        /// <summary>О каких файлах спросили.</summary>
        public List<string> Asked { get; } = [];

        /// <summary>Держит ответы, пока его не отпустят; пусто — отвечает сразу.</summary>
        public TaskCompletionSource? Hold { get; init; }

        public async Task<FilePreview?> GetPreviewAsync(string filePath, int pixels, CancellationToken cancellationToken)
        {
            Asked.Add(filePath);

            if (Hold is { } hold)
                await hold.Task;

            return Image is { } image ? new FilePreview(image) { IsStale = Stale } : null;
        }
    }
}
