using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Снимки форм на карточках доски: место под снимок постоянное, снимок ставится видимой карточке и
/// уходит вместе с её контейнером, старый — с отметкой, объявленный поставщиком — встаёт сразу.
/// </summary>
/// <remarks>
/// Снимки отдаёт поставщик теста через службу превью студии — той же дорогой, какой их отдаёт дизайнер:
/// доска знает только службу. Как дизайнер снимает формы, проверяет <see cref="FormSnapshotTests"/>.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class UiDesignerBoardPreviewTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui" Width="400" Height="200" />
        """;

    /// <summary>
    /// Снимок формы встаёт в место под него, а без снимка место стоит пустой подложкой той же высоты:
    /// карточка не прыгает, когда снимок придёт.
    /// </summary>
    [AvaloniaFact]
    public async Task A_card_shows_its_form_snapshot_in_the_place_kept_for_it()
    {
        var registry = new StudioFilePreviews(new StudioLog(), new PluginGuard());
        var forms = new Forms { Image = Png(384, 192) };

        using var registration = new PluginFilePreviews(registry, "test.forms").Register(forms);
        using var studio = new UiDesignerStudio(previews: registry);

        await studio.Open(studio.Solution(("MainWindow.axaml", Form)));
        await Shown(studio, studio.Card("MainWindow.axaml"));

        var card = studio.Card("MainWindow.axaml");
        var item = Container(studio, card);

        Assert.Equal(new PixelSize(384, 192), Assert.IsAssignableFrom<Bitmap>(card.Preview).PixelSize);
        Assert.True(Picture(item).IsEffectivelyVisible, "снимка на карточке нет");
        Assert.Equal(Length(studio, "AxFormCardPreviewHeight"), Well(item).Bounds.Height);
        Assert.False(Stale(item).IsVisible, "свежий снимок отмечен устаревшим");
    }

    /// <summary>
    /// Карточка с обычным текстом стоит ровно на наименьшей высоте темы: снимок и три строки в неё
    /// влезают, и шаг раскладки — высота с зазором — ложится в клетку сетки холста.
    /// </summary>
    [AvaloniaFact]
    public async Task A_card_with_its_snapshot_place_stands_at_the_theme_minimum()
    {
        var registry = new StudioFilePreviews(new StudioLog(), new PluginGuard());

        using var registration = new PluginFilePreviews(registry, "test.forms").Register(new Forms());
        using var studio = new UiDesignerStudio(previews: registry);

        await studio.Open(studio.Solution(("MainWindow.axaml", Form)));

        var minimum = Length(studio, "AxFormCardMinHeight");

        Assert.Equal(minimum, Container(studio, studio.Card("MainWindow.axaml")).Bounds.Height);
        Assert.Equal(0, (minimum + Length(studio, "AxFormCardGap")) % 20);
    }

    /// <summary>Снимок старше файла стоит на карточке с отметкой, и диктор называет её словами.</summary>
    [AvaloniaFact]
    public async Task A_stale_snapshot_shows_with_its_mark()
    {
        var registry = new StudioFilePreviews(new StudioLog(), new PluginGuard());

        using var registration = new PluginFilePreviews(registry, "test.forms").Register(new Forms { Image = Png(384, 192), Stale = true });
        using var studio = new UiDesignerStudio(previews: registry);

        await studio.Open(studio.Solution(("MainWindow.axaml", Form)));
        await Shown(studio, studio.Card("MainWindow.axaml"));

        var item = Container(studio, studio.Card("MainWindow.axaml"));

        Assert.True(Picture(item).IsEffectivelyVisible, "устаревший снимок спрятан");
        Assert.True(Stale(item).IsEffectivelyVisible, "отметки устаревшего снимка нет");
        Assert.Equal(studio.Strings["board.preview.stale"], AutomationProperties.GetName(Stale(item)));
    }

    /// <summary>
    /// Снимок, о котором поставщик объявил, встаёт на видимую карточку сразу: форму сняли в фоне, а файл
    /// при этом не менялся.
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

        Assert.True(Picture(Container(studio, card)).IsEffectivelyVisible, "объявленный снимок не встал");
    }

    /// <summary>
    /// Карточка, ушедшая с холста, отпускает снимок вместе с контейнером: растр не живёт дольше того, что
    /// его рисует.
    /// </summary>
    [AvaloniaFact]
    public async Task A_card_taken_off_the_board_lets_its_snapshot_go()
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
    /// Снимок, пришедший после того, как карточку убрали с холста, на неё не встаёт: держать его некому, и
    /// растр, поставленный карточке без контейнера, не освободил бы никто.
    /// </summary>
    [AvaloniaFact]
    public async Task A_snapshot_arriving_after_the_card_left_is_not_put_on_it()
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
            $"снимок {card.Name} не встал на карточку");

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Ждёт загрузки снимков: спрошенный поставщик ответил.</summary>
    private static async Task Settled(UiDesignerStudio studio)
    {
        Dispatcher.UIThread.RunJobs();
        await studio.Panel.Snapshots!.Loading.WaitAsync(TimeSpan.FromSeconds(30));
        Dispatcher.UIThread.RunJobs();
    }

    private static AxCard Container(UiDesignerStudio studio, FormCard card)
    {
        Dispatcher.UIThread.RunJobs();

        var container = studio.View.Sheet.ContainerFromItem(card) ?? throw new InvalidOperationException($"у {card.Name} нет контейнера");

        return container.GetVisualDescendants().OfType<AxCard>().Single();
    }

    private static Border Well(Visual card) =>
        card.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("preview"));

    private static Image Picture(Visual card) => card.GetVisualDescendants().OfType<Image>().Single();

    private static Border Stale(Visual card) =>
        card.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("stale"));

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
