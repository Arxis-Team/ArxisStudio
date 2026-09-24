using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Modules.Project;
using ArxisStudio.Modules.Project.Browse;
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
/// Превью картинок на плитках окна проекта, как в Project window у Unity.
/// </summary>
/// <remarks>
/// Картинки пишутся настоящими PNG — рисованием в память, без файлов в репозитории: рисование в
/// тестах настоящее, и декодер отличает картинку от пустышки. Файлы из обычной фикстуры решения
/// лежат пустыми, и <c>avalonia-logo.ico</c> среди них — картинка только по имени; это заодно
/// проверяет, что плитка такого файла остаётся силуэтом, а не падает.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectWindowPreviewTests
{
    /// <summary>
    /// Плитка картинки показывает саму картинку, а файл, который не читается, остаётся силуэтом.
    /// </summary>
    [AvaloniaFact]
    public async Task An_image_tile_shows_its_picture_in_place_of_the_silhouette()
    {
        using var studio = await Pictures(new ProjectWindowStudio(twoColumns: true), ("logo.png", 48, 48));

        var logo = TileItem(studio, "logo.png");

        Assert.True(Preview(logo).IsEffectivelyVisible, "картинки на плитке нет");
        Assert.IsAssignableFrom<Bitmap>(Preview(logo).Source);
        Assert.False(Silhouette(logo).IsVisible, "силуэт остался под картинкой");

        var empty = TileItem(studio, "avalonia-logo.ico");

        Assert.True(Silhouette(empty).IsEffectivelyVisible, "плитка непрочитанного файла потеряла силуэт");
        Assert.False(Preview(empty).IsVisible, "у непрочитанного файла место картинки");
    }

    /// <summary>
    /// Выключатель в настройках возвращает силуэты сразу и отдаёт память; включённый — картинки
    /// обратно.
    /// </summary>
    [AvaloniaFact]
    public async Task Turning_the_previews_off_brings_the_silhouettes_back()
    {
        using var studio = await Pictures(new ProjectWindowStudio(twoColumns: true), ("logo.png", 48, 48));

        studio.Settings.Set(ProjectSettings.PreviewsKey, false);
        Dispatcher.UIThread.RunJobs();

        var logo = TileItem(studio, "logo.png");

        Assert.True(Silhouette(logo).IsEffectivelyVisible, "выключенные превью оставили картинку");
        Assert.False(Preview(logo).IsVisible, "выключенные превью оставили место картинки");
        Assert.Equal(0, Service(studio).Kept);

        studio.Settings.Set(ProjectSettings.PreviewsKey, true);
        await Settled(studio);

        Assert.True(Preview(TileItem(studio, "logo.png")).IsEffectivelyVisible, "включённые превью не вернулись");
    }

    /// <summary>
    /// Декодируются только плитки на экране и в запасе вокруг него, а прокрутка дочитывает остальное.
    /// </summary>
    /// <remarks>
    /// Плитки не виртуализированы, и контейнер есть у каждой: без отбора по видимости папка с сотней
    /// картинок декодировалась бы целиком, пока человек смотрит на первый экран.
    /// </remarks>
    [AvaloniaFact]
    public async Task Only_tiles_in_view_are_decoded()
    {
        var names = Enumerable.Range(1, 60).Select(number => ($"shot{number:D2}.png", 8, 8)).ToArray();

        using var studio = await Pictures(new ProjectWindowStudio(twoColumns: true, height: 260), names);

        var decoded = Service(studio).Decoded;

        Assert.InRange(decoded, 1, names.Length - 1);

        var last = studio.Model.Browser.Items.Single(tile => tile.Name == names[^1].Item1);

        studio.Panel.Pane!.Shown.ScrollIntoView(last);
        await Settled(studio);

        Assert.NotNull(last.Preview);
        Assert.True(Service(studio).Decoded > decoded, "прокрутка не дочитала показавшиеся плитки");
    }

    /// <summary>
    /// Крупная картинка декодируется в крупнейшую ступень по длинной стороне, а мелкая не растягивается.
    /// </summary>
    /// <remarks>
    /// Иконка 16×16, растянутая до плитки, вышла бы мыльным квадратом: декодер Avalonia сам по себе
    /// растягивает до запрошенной ширины. Высокий кадр уменьшается по высоте — по ширине он вышел бы
    /// выше плитки в разы.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_picture_is_decoded_to_the_ladder_and_a_small_one_is_not_stretched()
    {
        using var studio = await Pictures(
            new ProjectWindowStudio(twoColumns: true),
            ("wide.png", 600, 300),
            ("tall.png", 200, 800),
            ("icon.png", 16, 16));

        var largest = (int)Math.Ceiling(Assert.IsType<double>(studio.Resource("AxTileGlyphSizeLarge")) * studio.Window.RenderScaling);

        Assert.Equal(new PixelSize(largest, largest / 2), Bitmap(studio, "wide.png").PixelSize);
        Assert.Equal(largest, Bitmap(studio, "tall.png").PixelSize.Height);
        Assert.Equal(new PixelSize(16, 16), Bitmap(studio, "icon.png").PixelSize);
    }

    /// <summary>
    /// Перезаписанная картинка обновляется, когда окно студии снова в фокусе.
    /// </summary>
    /// <remarks>
    /// Перезапись содержимого не даёт событий ни одному наблюдателю студии: плитка того же файла
    /// переживает снимок, и сверка с диском идёт по возвращению окна — так перечитывает ассеты Unity.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_rewritten_picture_is_picked_up_when_the_window_comes_back()
    {
        using var studio = await Pictures(new ProjectWindowStudio(twoColumns: true), ("logo.png", 24, 24));

        var logo = studio.Model.Browser.Items.Single(tile => tile.Name == "logo.png");

        Assert.Equal(new PixelSize(24, 24), Assert.IsAssignableFrom<Bitmap>(logo.Preview).PixelSize);

        File.WriteAllBytes(logo.Node.Path.Value, Png(40, 40));
        File.SetLastWriteTimeUtc(logo.Node.Path.Value, DateTime.UtcNow.AddMinutes(1));

        studio.Window.Activate();
        await Settled(studio);

        Assert.Equal(new PixelSize(40, 40), Assert.IsAssignableFrom<Bitmap>(logo.Preview).PixelSize);
    }

    /// <summary>
    /// Вернувшись в папку, окно не декодирует её заново: превью ушедших плиток ждут в кэше.
    /// </summary>
    [AvaloniaFact]
    public async Task Coming_back_to_a_folder_does_not_decode_it_again()
    {
        using var studio = await Pictures(new ProjectWindowStudio(twoColumns: true), ("logo.png", 48, 48), ("mark.png", 32, 32));

        var decoded = Service(studio).Decoded;

        studio.Select("App");
        await Settled(studio);
        studio.Select("Assets");
        await Settled(studio);

        Assert.NotNull(studio.Model.Browser.Items.Single(tile => tile.Name == "logo.png").Preview);
        Assert.Equal(decoded, Service(studio).Decoded);
    }

    /// <summary>Вырезанная картинка показывает приглушённый силуэт, вставленная — снова картинку.</summary>
    [AvaloniaFact]
    public async Task A_cut_picture_shows_its_muted_silhouette()
    {
        using var studio = await Pictures(new ProjectWindowStudio(twoColumns: true), ("logo.png", 48, 48));

        var tile = studio.Model.Browser.Items.Single(item => item.Name == "logo.png");

        tile.IsCut = true;
        Dispatcher.UIThread.RunJobs();

        var logo = TileItem(studio, "logo.png");

        Assert.True(Silhouette(logo).IsEffectivelyVisible, "у вырезанной картинки нет силуэта");
        Assert.Same(studio.Resource("AxTextDisabledBrush"), Silhouette(logo).Foreground);
        Assert.False(Preview(logo).IsVisible, "вырезанная картинка не приглушена");

        tile.IsCut = false;
        Dispatcher.UIThread.RunJobs();

        Assert.True(Preview(logo).IsEffectivelyVisible, "вставленная картинка не вернулась");
    }

    /// <summary>
    /// SVG — картинка, но декодера векторной графики нет: его плитка остаётся силуэтом, и файл
    /// декодеру даже не отдаётся.
    /// </summary>
    /// <remarks>
    /// Силуэт вышел бы и так: растровый декодер SVG не прочтёт. Но читать файл ради заведомого отказа —
    /// лишний проход по диску на каждую плитку, поэтому проверяется и число прочитанного: в папке две
    /// растровые картинки по имени — <c>logo.png</c> и пустой <c>avalonia-logo.ico</c> из фикстуры.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_scalable_picture_keeps_its_silhouette()
    {
        using var studio = await Pictures(new ProjectWindowStudio(twoColumns: true), ("logo.png", 48, 48));

        var shape = studio.Model.Browser.Items.Single(tile => tile.Name == "shape.svg");

        Assert.Null(shape.Preview);
        Assert.True(Silhouette(TileItem(studio, "shape.svg")).IsEffectivelyVisible, "у SVG нет силуэта");
        Assert.Equal(2, Service(studio).Decoded);
    }

    /// <summary>
    /// Открывает решение, у которого в <c>Assets</c> лежат картинки, и ставит колонку в эту папку.
    /// </summary>
    /// <remarks>
    /// Рядом с картинками всегда лежит <c>shape.svg</c>: SVG картинкой названо, а прочесть его нечем.
    /// </remarks>
    private static async Task<ProjectWindowStudio> Pictures(
        ProjectWindowStudio studio, params (string Name, int Width, int Height)[] pictures)
    {
        var more = pictures.Select(picture => $"Assets/{picture.Name}").Append("Assets/shape.svg").ToList();

        await studio.Open(studio.Solution(more: more));

        var assets = studio.Row("Assets").Node.Path;

        foreach (var (name, width, height) in pictures)
            File.WriteAllBytes(assets.Combine(name).Value, Png(width, height));

        File.WriteAllText(assets.Combine("shape.svg").Value, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");

        studio.Select("Assets");
        await Settled(studio);

        return studio;
    }

    /// <summary>Ждёт проходы превью и то, что они поставили на плитки.</summary>
    /// <remarks>
    /// Проход декодирует в фоне, а растр ставит в потоке интерфейса; поставленный растр меняет
    /// раскладку, и она может попросить ещё проход — поэтому ждать приходится до тишины.
    /// </remarks>
    private static async Task Settled(ProjectWindowStudio studio)
    {
        var previews = studio.Panel.Pane!.Previews;

        for (var round = 0; round < 4; round++)
        {
            Dispatcher.UIThread.RunJobs();
            await previews.Settled.WaitAsync(TimeSpan.FromSeconds(30));
            Dispatcher.UIThread.RunJobs();
        }
    }

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

    private static Previews Service(ProjectWindowStudio studio) => studio.Panel.Pane!.Previews.Service;

    /// <summary>Растр, который стоит на плитке.</summary>
    private static Bitmap Bitmap(ProjectWindowStudio studio, string name) =>
        Assert.IsAssignableFrom<Bitmap>(studio.Model.Browser.Items.Single(tile => tile.Name == name).Preview);

    /// <summary>Контейнер плитки в показанном списке — прокрутив до неё.</summary>
    private static AxListBoxItem TileItem(ProjectWindowStudio studio, string name)
    {
        var list = studio.Panel.Pane!.Shown;
        var tile = studio.Model.Browser.Items.Single(item => item.Name == name);

        list.ScrollIntoView(tile);
        Dispatcher.UIThread.RunJobs();

        return Assert.IsType<AxListBoxItem>(list.ContainerFromItem(tile));
    }

    /// <summary>Силуэт плитки.</summary>
    private static AxIcon Silhouette(AxListBoxItem item) =>
        item.GetVisualDescendants().OfType<AxIcon>().Single(icon => icon.Classes.Contains("tile"));

    /// <summary>Место картинки на плитке.</summary>
    private static Image Preview(AxListBoxItem item) =>
        item.GetVisualDescendants().OfType<Image>().Single(image => image.Classes.Contains("preview"));
}
