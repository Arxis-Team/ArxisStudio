using System.Runtime.InteropServices;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Snapshots;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Xunit;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace ArxisStudio.Tests;

/// <summary>
/// Снимки форм: живая вкладка снимает форму такой, какой она лежит на диске, — на фоне окна её
/// приложения, — а превью отдаёт снимок плиткам с отметкой, если форма менялась после него.
/// </summary>
/// <remarks>
/// Форма — две половины разного цвета: по краям картинки видно, что снята вся область формы и ровно
/// она, без рамки карточки и без сдвига на её место в карточке. Картинку тест читает так же, как её
/// прочтёт плитка, — превью студии, — а не хранилищем мимо них.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class FormSnapshotTests
{
    /// <summary>Форма 200×100: левая половина красная, правая синяя.</summary>
    private const string Halves = """
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     Width="200" Height="100">
          <Canvas>
            <Rectangle x:Name="Left" Canvas.Left="0" Canvas.Top="0" Width="100" Height="100" Fill="#FFFF0000" />
            <Rectangle x:Name="Right" Canvas.Left="100" Canvas.Top="0" Width="100" Height="100" Fill="#FF0000FF" />
          </Canvas>
        </UserControl>
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Открытая форма оставляет снимок: длинная сторона — размер снимка, стороны — как у формы, края —
    /// края формы. Превью говорит о нём показывающим, и плитка его найдёт.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_shown_live_leaves_its_snapshot_for_the_tiles()
    {
        var snapshots = TempFolder.Create("snapshots");

        try
        {
            await using var studio = new LiveFormStudio(snapshots: snapshots);
            var path = studio.Xaml.PathOf("Halves.axaml").Value;
            var heard = new List<string>();

            studio.Xaml.Previews.Subscribe("test", (_, e) => heard.Add(e.FilePath));

            Assert.True(studio.Xaml.Previews.CanPreview(path), "дизайнер не поставил поставщика снимков");

            var document = await studio.OpenAsync("Halves.axaml", Halves);
            var preview = await SnapshotAsync(studio, document, path);
            var picture = Picture.Of(preview);

            Assert.False(preview.IsStale, "снимок только что открытой формы назван устаревшим");
            Assert.Equal(new PixelSize(FormSnapshots.Pixels, FormSnapshots.Pixels / 2), picture.Size);
            Assert.True(picture.IsRed(0, picture.Height / 2), $"левый край не красный: {picture.At(0, picture.Height / 2)}");
            Assert.True(picture.IsRed(picture.Width / 4, 0), $"верхний край не красный: {picture.At(picture.Width / 4, 0)}");
            Assert.True(picture.IsBlue(picture.Width - 2, picture.Height / 2), $"правый край не синий: {picture.At(picture.Width - 2, picture.Height / 2)}");
            Assert.True(picture.IsBlue(picture.Width * 3 / 4, picture.Height - 2), $"нижний край не синий: {picture.At(picture.Width * 3 / 4, picture.Height - 2)}");
            Assert.Contains(path, heard);
        }
        finally
        {
            TempFolder.Erase(snapshots);
        }
    }

    /// <summary>
    /// Не закрытое формой стоит на фоне, которым тема её приложения одевает окно, — и написанном
    /// кистью, и взятом из ресурсов, как у Fluent; без приложения снимок прозрачен.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("#FF336699", "")]
    [InlineData("{DynamicResource WindowBrush}", """<SolidColorBrush x:Key="WindowBrush" Color="#FF336699" />""")]
    public async Task The_snapshot_stands_on_the_window_background_of_its_application(string background, string resources)
    {
        var snapshots = TempFolder.Create("snapshots");

        try
        {
            await using var studio = new LiveFormStudio(snapshots: snapshots);
            var path = studio.Xaml.PathOf("Half.axaml").Value;

            studio.Xaml.Write("App.axaml", $$"""
                <Application xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Application.Resources>
                    {{resources}}
                    <ControlTheme x:Key="{x:Type Window}" TargetType="Window">
                      <Setter Property="Background" Value="{{background}}" />
                    </ControlTheme>
                  </Application.Resources>
                </Application>
                """);

            var document = await studio.OpenAsync("Half.axaml", Halves.Replace("""<Rectangle x:Name="Right" """, """<Rectangle x:Name="Right" IsVisible="False" """, StringComparison.Ordinal));

            await XamlStudio.UntilAsync(() => document.Form.ApplicationRoot is not null, "приложение формы не встало");

            var preview = await SnapshotAsync(studio, document, path, taken => Picture.Of(taken).At(FormSnapshots.Pixels * 3 / 4, 10).A > 0);
            var picture = Picture.Of(preview);

            Assert.Equal(Color.Parse("#FF336699"), picture.At(picture.Width * 3 / 4, picture.Height / 2));
            Assert.True(picture.IsRed(picture.Width / 4, picture.Height / 2), "форма легла под фон, а не на него");
        }
        finally
        {
            TempFolder.Erase(snapshots);
        }
    }

    /// <summary>
    /// Форма без приложения снимается без фона: тема окна, которую найдёт поиск, — тема самой студии, а
    /// не формы, и подложить её значило бы показать форму в чужих цветах.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_without_an_application_is_snapshotted_on_nothing()
    {
        var snapshots = TempFolder.Create("snapshots");

        try
        {
            await using var studio = new LiveFormStudio(snapshots: snapshots);
            var path = studio.Xaml.PathOf("Half.axaml").Value;
            var document = await studio.OpenAsync("Half.axaml", Halves.Replace("""<Rectangle x:Name="Right" """, """<Rectangle x:Name="Right" IsVisible="False" """, StringComparison.Ordinal));
            var picture = Picture.Of(await SnapshotAsync(studio, document, path));

            Assert.Equal(0, picture.At(picture.Width * 3 / 4, picture.Height / 2).A);
            Assert.True(picture.IsRed(picture.Width / 4, picture.Height / 2), "форма не снята");
        }
        finally
        {
            TempFolder.Erase(snapshots);
        }
    }

    /// <summary>
    /// Несохранённая правка не снимается: снимок отвечает файлу, а не вкладке. Сохранённая — снимается, и
    /// превью снова свежее.
    /// </summary>
    [AvaloniaFact]
    public async Task An_unsaved_edit_is_not_snapshotted_until_it_is_saved()
    {
        var snapshots = TempFolder.Create("snapshots");

        try
        {
            await using var studio = new LiveFormStudio(snapshots: snapshots);
            var path = studio.Xaml.PathOf("Halves.axaml").Value;
            var document = await studio.OpenAsync("Halves.axaml", Halves);

            await SnapshotAsync(studio, document, path);

            await document.Document!.EditAsync(
                "Цвет", editor => editor.SetAttribute(Named(editor.Document, "Left"), XamlQualifiedName.Unprefixed("Fill"), "#FF00FF00"), Token);
            LiveFormStudio.Frame();
            await document.Snapshotting;

            var unsaved = await studio.Xaml.Previews.GetAsync(path, 128, Token);

            Assert.True(document.IsModified);
            Assert.True(Picture.Of(unsaved!).IsRed(0, Picture.Of(unsaved!).Height / 2), "снята несохранённая правка");
            Assert.False(unsaved!.IsStale, "файл не менялся, а снимок назван устаревшим");

            Assert.True(await document.SaveAsync(), "форма не сохранилась");

            var saved = await SnapshotAsync(studio, document, path, taken => Picture.Of(taken).IsGreen(0, FormSnapshots.Pixels / 4));

            Assert.False(saved.IsStale, "снимок сохранённой формы назван устаревшим");
        }
        finally
        {
            TempFolder.Erase(snapshots);
        }
    }

    /// <summary>
    /// Форма, переписанная снаружи, пока вкладка открыта, снимается заново вкладкой; закрытая — сперва
    /// отдаёт прежний снимок с отметкой «старше файла», узнаваемое старое лучше значка, — а потом её
    /// переснимают в фоне.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_rewritten_outside_is_snapshotted_by_its_tab_or_else_in_the_background()
    {
        var snapshots = TempFolder.Create("snapshots");

        try
        {
            await using var studio = new LiveFormStudio(snapshots: snapshots);
            var path = studio.Xaml.PathOf("Halves.axaml").Value;
            var document = await studio.OpenAsync("Halves.axaml", Halves);

            await SnapshotAsync(studio, document, path);

            studio.Xaml.Write("Halves.axaml", Halves.Replace("#FFFF0000", "#FF00FF00", StringComparison.Ordinal));

            var taken = await SnapshotAsync(studio, document, path, preview => Picture.Of(preview).IsGreen(0, FormSnapshots.Pixels / 4));

            Assert.False(taken.IsStale, "снимок принятого с диска назван устаревшим");

            await document.DisposeAsync();
            studio.Xaml.Write("Halves.axaml", Halves);

            var stale = await studio.Xaml.Previews.GetAsync(path, 128, Token);

            Assert.True(stale!.IsStale, "форма менялась после снимка, а отметки нет");
            Assert.True(Picture.Of(stale).IsGreen(0, Picture.Of(stale).Height / 2), "устаревший снимок подменён");

            var captures = Captures(studio);

            await XamlStudio.UntilAsync(() => captures.Taken == 1, "закрытую форму не пересняли в фоне");

            var fresh = await studio.Xaml.Previews.GetAsync(path, 128, Token);

            Assert.False(fresh!.IsStale, "переснятая форма осталась с отметкой");
            Assert.True(Picture.Of(fresh).IsRed(0, Picture.Of(fresh).Height / 2), "переснят не нынешний файл");
        }
        finally
        {
            TempFolder.Erase(snapshots);
        }
    }

    /// <summary>
    /// Форма, которую не открывали, снимается в фоне, как только её превью спросили: без вкладки, тем же
    /// снимком, что у вкладки, — и документ после снимка отпущен.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_never_opened_is_snapshotted_in_the_background_once_its_preview_is_asked()
    {
        var snapshots = TempFolder.Create("snapshots");

        try
        {
            await using var studio = new LiveFormStudio(snapshots: snapshots);
            var path = studio.Xaml.Write("Halves.axaml", Halves).Value;
            var heard = new List<string>();

            studio.Xaml.Previews.Subscribe("test", (_, e) => heard.Add(e.FilePath));
            await studio.Xaml.OpenAsync();

            Assert.Null(await studio.Xaml.Previews.GetAsync(path, 128, Token));

            await XamlStudio.UntilAsync(() => heard.Contains(path), "форму не сняли в фоне");

            var preview = await studio.Xaml.Previews.GetAsync(path, 128, Token);
            var picture = Picture.Of(preview!);

            Assert.False(preview!.IsStale, "снимок только что снятой формы назван устаревшим");
            Assert.Equal(new PixelSize(FormSnapshots.Pixels, FormSnapshots.Pixels / 2), picture.Size);
            Assert.True(picture.IsRed(0, picture.Height / 2), $"левый край не красный: {picture.At(0, picture.Height / 2)}");
            Assert.True(picture.IsBlue(picture.Width - 2, picture.Height / 2), $"правый край не синий: {picture.At(picture.Width - 2, picture.Height / 2)}");
            Assert.Equal(1, Captures(studio).Taken);
            Assert.Equal(0, studio.Xaml.Session.Leases);
        }
        finally
        {
            TempFolder.Erase(snapshots);
        }
    }

    /// <summary>
    /// Форма, снятая в фоне, стоит на фоне окна своего приложения, как во вкладке: приложение показ берёт
    /// следом за корнем, и снимок его дожидается.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_snapshotted_in_the_background_wears_its_application()
    {
        var snapshots = TempFolder.Create("snapshots");

        try
        {
            await using var studio = new LiveFormStudio(snapshots: snapshots);

            studio.Xaml.Write("App.axaml", """
                <Application xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Application.Resources>
                    <ControlTheme x:Key="{x:Type Window}" TargetType="Window">
                      <Setter Property="Background" Value="#FF336699" />
                    </ControlTheme>
                  </Application.Resources>
                </Application>
                """);

            var path = studio.Xaml.Write(
                "Half.axaml",
                Halves.Replace("""<Rectangle x:Name="Right" """, """<Rectangle x:Name="Right" IsVisible="False" """, StringComparison.Ordinal)).Value;
            var captures = Captures(studio);

            await studio.Xaml.OpenAsync();

            Assert.Null(await studio.Xaml.Previews.GetAsync(path, 128, Token));
            await XamlStudio.UntilAsync(() => captures.Taken == 1, "форму с приложением не сняли в фоне");

            var picture = Picture.Of((await studio.Xaml.Previews.GetAsync(path, 128, Token))!);

            Assert.Equal(Color.Parse("#FF336699"), picture.At(picture.Width * 3 / 4, picture.Height / 2));
            Assert.True(picture.IsRed(picture.Width / 4, picture.Height / 2), "форма легла под фон, а не на него");
        }
        finally
        {
            TempFolder.Erase(snapshots);
        }
    }

    /// <summary>
    /// В фоне снимаются только формы решения: приложение, словарь стилей и файл вне решения не снимаются,
    /// и служба XAML ради них даже не поднимается.
    /// </summary>
    [AvaloniaFact]
    public async Task Only_forms_of_the_solution_are_snapshotted_in_the_background()
    {
        var snapshots = TempFolder.Create("snapshots");

        try
        {
            await using var studio = new LiveFormStudio(snapshots: snapshots);
            var application = studio.Xaml.Write("App.axaml", """<Application xmlns="https://github.com/avaloniaui" />""").Value;
            var styles = studio.Xaml.Write("Styles.axaml", """<Styles xmlns="https://github.com/avaloniaui" />""").Value;
            var outside = Path.Combine(studio.Xaml.Root, "Elsewhere.axaml");

            File.WriteAllText(outside, Halves);
            await studio.Xaml.OpenAsync();

            foreach (var file in new[] { application, styles, outside })
                Assert.Null(await studio.Xaml.Previews.GetAsync(file, 128, Token));

            var captures = Captures(studio);

            await XamlStudio.UntilAsync(() => captures.Pumping.IsCompleted, "очередь снимков не опустела");

            Assert.Equal(0, captures.Taken + captures.Failed);
            Assert.Equal(XamlDesignState.Idle, studio.Xaml.Design.State);
        }
        finally
        {
            TempFolder.Erase(snapshots);
        }
    }

    /// <summary>Настройка дизайнера выключает фоновые снимки: форму снимает только её вкладка.</summary>
    [AvaloniaFact]
    public async Task Background_snapshots_are_off_when_the_designer_is_told_so()
    {
        var snapshots = TempFolder.Create("snapshots");

        try
        {
            await using var studio = new LiveFormStudio(snapshots: snapshots);
            var path = studio.Xaml.Write("Halves.axaml", Halves).Value;

            studio.Context.Settings.Set(UiDesignerModule.PreviewsKey, false);
            await studio.Xaml.OpenAsync();

            Assert.Null(await studio.Xaml.Previews.GetAsync(path, 128, Token));

            var captures = Captures(studio);

            await XamlStudio.UntilAsync(() => captures.Pumping.IsCompleted, "очередь снимков не опустела");

            Assert.Equal(0, captures.Taken);
            Assert.Equal(XamlDesignState.Idle, studio.Xaml.Design.State);
        }
        finally
        {
            TempFolder.Erase(snapshots);
        }
    }

    /// <summary>
    /// Вкладка, открытая, пока форму снимают в фоне, забирает показ: снимок бросает своё, а вкладка
    /// встаёт живой — показ у документа один, и ждать его вкладке не приходится долго.
    /// </summary>
    [AvaloniaFact]
    public async Task A_tab_opened_while_its_form_is_snapshotted_takes_the_show()
    {
        var snapshots = TempFolder.Create("snapshots");
        var held = new TaskCompletionSource();
        var hold = new TaskCompletionSource();

        try
        {
            await using var studio = new LiveFormStudio(snapshots: snapshots, snapshotShown: async token =>
            {
                held.TrySetResult();
                await hold.Task.WaitAsync(token);
            });
            var path = studio.Xaml.Write("Halves.axaml", Halves).Value;

            await studio.Xaml.OpenAsync();
            Assert.Null(await studio.Xaml.Previews.GetAsync(path, 128, Token));

            await XamlStudio.UntilAsync(() => held.Task.IsCompleted, "фоновый снимок не взял показ");

            var (view, error) = await studio.Editor().OpenAsync(path);

            Assert.Null(error);

            var document = Assert.IsType<LiveFormDocument>(view);

            try
            {
                studio.Window.Content = document.Content;
                Dispatcher.UIThread.RunJobs();

                // Предел — чтобы снимок, не отпустивший показ, уронил тест, а не повесил его.
                await document.Opening.WaitAsync(TimeSpan.FromSeconds(30), Token);
                await XamlStudio.UntilAsync(() => document.Form.Root is not null, "вкладка не взяла показ у снимка");

                var captures = Captures(studio);

                Assert.Equal(0, captures.Taken + captures.Failed);
                Assert.False(captures.Capturing, "брошенный снимок держится");
            }
            finally
            {
                studio.Window.Content = null;
                await document.DisposeAsync();
            }
        }
        finally
        {
            TempFolder.Erase(snapshots);
        }
    }

    /// <summary>
    /// Вкладка, ушедшая с экрана, держит форму и от фоновой съёмки: та снимает формы, которых не держит никто, а
    /// вкладка, вернувшись, находит свою форму той же, а не построенной заново.
    /// </summary>
    [AvaloniaFact]
    public async Task A_tab_off_screen_keeps_its_form_from_the_background_snapshot()
    {
        var snapshots = TempFolder.Create("snapshots");

        try
        {
            await using var studio = new LiveFormStudio(snapshots: snapshots);
            var other = studio.Xaml.Write("Other.axaml", Halves).Value;
            var path = studio.Xaml.PathOf("Halves.axaml").Value;
            var document = await studio.OpenAsync("Halves.axaml", Halves);

            await SnapshotAsync(studio, document, path);

            var shown = document.Shown;

            studio.Window.Content = null;
            LiveFormStudio.Frame();

            // Файл переписан снаружи, пока вкладки нет на экране: её снимок старше файла, а снять форму вне окна
            // нельзя.
            studio.Xaml.Write("Halves.axaml", Halves.Replace("#FFFF0000", "#FF00FF00", StringComparison.Ordinal));

            await XamlStudio.UntilAsync(
                () => document.Document!.Syntax.SourceText.ToString().Contains("#FF00FF00", StringComparison.Ordinal),
                "вкладка не приняла текст с диска");

            var captures = Captures(studio);

            Assert.True((await studio.Xaml.Previews.GetAsync(path, 128, Token))!.IsStale, "снимок формы вкладки не устарел");
            Assert.Null(await studio.Xaml.Previews.GetAsync(other, 128, Token));

            await XamlStudio.UntilAsync(() => captures.Taken > 0, "свободную форму не сняли в фоне");
            await captures.Pumping;

            Assert.Equal(1, captures.Taken);
            Assert.True((await studio.Xaml.Previews.GetAsync(path, 128, Token))!.IsStale, "форму вкладки сняли в фоне");
            Assert.Same(shown, document.Shown);

            studio.Window.Content = document.Content;
            LiveFormStudio.Frame();

            Assert.Same(shown, document.Shown);
        }
        finally
        {
            TempFolder.Erase(snapshots);
        }
    }

    /// <summary>
    /// Форма, которая не встала, остаётся значком и не пробуется снова, пока её текст тот же; исправленная —
    /// снимается.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_that_does_not_stand_is_not_tried_again_until_its_text_changes()
    {
        var snapshots = TempFolder.Create("snapshots");

        try
        {
            await using var studio = new LiveFormStudio(snapshots: snapshots);
            var broken = Halves.Replace("<Canvas>", "<Canvas><Nowhere />", StringComparison.Ordinal);
            var path = studio.Xaml.Write("Halves.axaml", broken).Value;
            var captures = Captures(studio);

            await studio.Xaml.OpenAsync();

            Assert.Null(await studio.Xaml.Previews.GetAsync(path, 128, Token));
            await XamlStudio.UntilAsync(() => captures.Failed == 1 && captures.Pumping.IsCompleted, "сломанная форма не отказала");

            Assert.Null(await studio.Xaml.Previews.GetAsync(path, 128, Token));
            await XamlStudio.UntilAsync(() => captures.Pumping.IsCompleted, "очередь снимков не опустела");

            Assert.Equal(1, captures.Failed);
            Assert.Equal(0, captures.Taken);

            studio.Xaml.Write("Halves.axaml", Halves);

            Assert.Null(await studio.Xaml.Previews.GetAsync(path, 128, Token));
            await XamlStudio.UntilAsync(() => captures.Taken == 1, "исправленную форму не сняли");
        }
        finally
        {
            TempFolder.Erase(snapshots);
        }
    }

    /// <summary>
    /// Под видом «XAML» холст спрятан, и форма, сменившаяся там, не снимается — её раскладка прежняя, —
    /// а превью остаётся прежним с отметкой; холст вернулся — форма снимается.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_changed_under_the_xaml_view_is_snapshotted_once_the_canvas_is_back()
    {
        var snapshots = TempFolder.Create("snapshots");

        try
        {
            await using var studio = new LiveFormStudio(snapshots: snapshots);
            var path = studio.Xaml.PathOf("Halves.axaml").Value;
            var document = await studio.OpenAsync("Halves.axaml", Halves);
            var wide = Halves
                .Replace("""Width="100" Height="100" Fill="#FFFF0000" """, """Width="150" Height="100" Fill="#FF00FF00" """, StringComparison.Ordinal)
                .Replace("""<Rectangle x:Name="Right" """, """<Rectangle x:Name="Right" IsVisible="False" """, StringComparison.Ordinal);

            await SnapshotAsync(studio, document, path);

            document.View.Mode.SelectedIndex = (int)FormViewMode.Xaml;
            Dispatcher.UIThread.RunJobs();

            studio.Xaml.Write("Halves.axaml", wide);
            await XamlStudio.UntilAsync(() => document.Document!.Syntax.SourceText.ToString() == wide, "запись снаружи не принята");
            LiveFormStudio.Frame();
            await document.Snapshotting;

            var hidden = await studio.Xaml.Previews.GetAsync(path, 128, Token);

            Assert.True(hidden!.IsStale, "форма сменилась, а превью не отмечено");
            Assert.True(Picture.Of(hidden).IsRed(0, Picture.Of(hidden).Height / 2), "снят спрятанный холст");

            document.View.Mode.SelectedIndex = (int)FormViewMode.Design;

            var shown = await SnapshotAsync(studio, document, path, preview => !preview.IsStale);
            var picture = Picture.Of(shown);

            Assert.True(picture.IsGreen(picture.Width * 5 / 8, picture.Height / 2), "снята прежняя раскладка");
        }
        finally
        {
            TempFolder.Erase(snapshots);
        }
    }

    /// <summary>
    /// Снимается сама область, где бы она ни стояла в родителе: от края до края, без сдвига на её место.
    /// </summary>
    /// <remarks>
    /// У карточки тестовой темы рамки нет, и область формы стоит в её начале, — поэтому место здесь задано
    /// полем, а снимает тот же код, что у вкладки. Держит тест и поведение Avalonia: <c>Render</c> рисует
    /// элемент в начале холста, а не на его месте, — поправка «на место», написанная было в расчёте на
    /// обратное, этим тестом и нашлась.
    /// </remarks>
    [AvaloniaFact]
    public void A_picture_is_taken_of_the_face_wherever_it_stands()
    {
        var right = new Rectangle { Width = 100, Height = 100, Fill = Brushes.Blue };
        var face = new Border
        {
            Width = 200,
            Height = 100,
            Margin = new Thickness(7, 5, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Canvas { Children = { new Rectangle { Width = 100, Height = 100, Fill = Brushes.Red }, right } },
        };
        var window = new Window { Width = 300, Height = 200, Content = face };

        Canvas.SetLeft(right, 100);
        window.Show();
        LiveFormStudio.Frame();

        try
        {
            Assert.Equal(new Point(7, 5), face.Bounds.Position);

            var picture = Picture.Of(new FilePreview(FormPicture.Take(face, face, FormSnapshots.Pixels)!));

            Assert.Equal(new PixelSize(FormSnapshots.Pixels, FormSnapshots.Pixels / 2), picture.Size);
            Assert.True(picture.IsRed(0, picture.Height / 2), $"левый край не красный: {picture.At(0, picture.Height / 2)}");
            Assert.True(picture.IsRed(picture.Width / 4, 0), $"верхний край не красный: {picture.At(picture.Width / 4, 0)}");
            Assert.True(picture.IsBlue(picture.Width - 2, picture.Height / 2), $"правый край не синий: {picture.At(picture.Width - 2, picture.Height / 2)}");
            Assert.True(picture.IsBlue(picture.Width * 3 / 4, picture.Height - 2), $"нижний край не синий: {picture.At(picture.Width * 3 / 4, picture.Height - 2)}");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Свежесть сверяется с текстом формы и её приложения, а не со временем записи: вернувшийся текст
    /// снова свеж, а сменившееся или пропавшее приложение старит снимок.
    /// </summary>
    [Fact]
    public async Task A_snapshot_is_fresh_while_the_form_and_its_application_read_the_same()
    {
        var folder = TempFolder.Create("snapshots");

        try
        {
            var form = Path.Combine(folder, "Form.axaml");
            var application = Path.Combine(folder, "App.axaml");
            var store = new FormSnapshots(Path.Combine(folder, "store"));
            byte[] image = [1, 2, 3];

            File.WriteAllText(form, "<UserControl />");
            File.WriteAllText(application, "<Application />");

            await store.WriteAsync(
                new FormSnapshot(form, FormSnapshots.Hash("<UserControl />"), application, null, "Light", 10, 10), image, Token);

            var fresh = await store.ReadAsync(form, Token);

            Assert.Equal(image, fresh!.Image.ToArray());
            Assert.False(fresh.IsStale);
            Assert.NotNull(await store.ReadAsync(form.ToUpperInvariant(), Token));

            File.WriteAllText(form, "<UserControl Width=\"1\" />");
            Assert.True((await store.ReadAsync(form, Token))!.IsStale, "переписанная форма не устарила снимок");

            File.WriteAllText(form, "<UserControl />");
            Assert.False((await store.ReadAsync(form, Token))!.IsStale, "тот же текст назван новым");

            File.WriteAllText(application, "<Application RequestedThemeVariant=\"Dark\" />");
            Assert.True((await store.ReadAsync(form, Token))!.IsStale, "сменившееся приложение не устарило снимок");

            File.Delete(application);
            Assert.True((await store.ReadAsync(form, Token))!.IsStale, "пропавшее приложение не устарило снимок");
        }
        finally
        {
            TempFolder.Erase(folder);
        }
    }

    /// <summary>У формы без снимка и со сломанными сведениями превью нет — а не исключение.</summary>
    [Fact]
    public async Task A_form_without_a_readable_snapshot_has_no_preview()
    {
        var folder = TempFolder.Create("snapshots");

        try
        {
            var form = Path.Combine(folder, "Form.axaml");
            var store = new FormSnapshots(Path.Combine(folder, "store"));

            File.WriteAllText(form, "<UserControl />");

            Assert.Null(await store.ReadAsync(form, Token));

            await store.WriteAsync(new FormSnapshot(form, FormSnapshots.Hash("<UserControl />"), null, null, "Light", 10, 10), [1], Token);
            File.WriteAllText(Assert.Single(Directory.GetFiles(store.Folder, "*.json")), "{ сломано");

            Assert.Null(await store.ReadAsync(form, Token));
        }
        finally
        {
            TempFolder.Erase(folder);
        }
    }

    /// <summary>
    /// Снимки выключает <c>0</c> — швом модуля или переменной среды, как у процесса тестов, — и тогда
    /// поставщика у превью нет вовсе; папка, названная швом, сильнее переменной.
    /// </summary>
    [AvaloniaFact]
    public async Task Snapshots_are_off_when_told_so_and_the_previews_have_no_provider()
    {
        Assert.Null(FormSnapshots.For(new UiDesignerOptions { SnapshotsFolder = "0" }));
        Assert.Null(FormSnapshots.For((UiDesignerOptions?)null));
        Assert.Equal("here", FormSnapshots.For(new UiDesignerOptions { SnapshotsFolder = "here" })?.Folder);

        await using var studio = new LiveFormStudio();

        Assert.False(studio.Xaml.Previews.CanPreview(studio.Xaml.PathOf("MainWindow.axaml").Value), "выключенные снимки поставили поставщика");
    }

    /// <summary>
    /// Ждёт снимка, отвечающего условию, и отдаёт его превью, каким его увидит плитка.
    /// </summary>
    /// <remarks>
    /// Снимок ставится в очередь после раскладки, а пишется в фоне: ждать приходится кадр, запись и
    /// ответ поставщика — и так, пока снимок не тот, которого ждут.
    /// </remarks>
    private static async Task<FilePreview> SnapshotAsync(
        LiveFormStudio studio, LiveFormDocument document, string path, Func<FilePreview, bool>? until = null)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (true)
        {
            LiveFormStudio.Frame();
            await document.Snapshotting;

            if (await studio.Xaml.Previews.GetAsync(path, 128, Token) is { } preview && (until?.Invoke(preview) ?? true))
                return preview;

            Assert.True(DateTime.UtcNow < deadline, "снимка не дождались");
            await Task.Delay(10, Token);
        }
    }

    /// <summary>Фоновая съёмка дизайнера студии теста.</summary>
    private static FormCaptures Captures(LiveFormStudio studio) =>
        FormCaptures.Of(studio.Context) ?? throw new InvalidOperationException("у дизайнера нет фоновой съёмки");

    private static XamlElement Named(XamlDocument document, string name) =>
        document.Root!.DescendantElements().Single(element => element.Identity == name);

    /// <summary>Картинка превью точками — так, как её развернёт плитка.</summary>
    private sealed class Picture
    {
        private readonly int[] _bgra;

        private Picture(PixelSize size, int[] bgra)
        {
            Size = size;
            _bgra = bgra;
        }

        public PixelSize Size { get; }

        public int Width => Size.Width;

        public int Height => Size.Height;

        public static Picture Of(FilePreview preview)
        {
            using var stream = new MemoryStream(preview.Image.ToArray());
            using var bitmap = new Bitmap(stream);
            using var copy = new WriteableBitmap(bitmap.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using var frame = copy.Lock();

            bitmap.CopyPixels(frame);

            var pixels = new int[bitmap.PixelSize.Width * bitmap.PixelSize.Height];

            for (var y = 0; y < bitmap.PixelSize.Height; y++)
                Marshal.Copy(frame.Address + (y * frame.RowBytes), pixels, y * bitmap.PixelSize.Width, bitmap.PixelSize.Width);

            return new Picture(bitmap.PixelSize, pixels);
        }

        public Color At(int x, int y)
        {
            var bgra = _bgra[(y * Width) + x];

            return Color.FromArgb((byte)(bgra >> 24), (byte)(bgra >> 16), (byte)(bgra >> 8), (byte)bgra);
        }

        public bool IsRed(int x, int y) => At(x, y) is { A: > 200, R: > 200, G: < 60, B: < 60 };

        public bool IsGreen(int x, int y) => At(x, y) is { A: > 200, R: < 60, G: > 200, B: < 60 };

        public bool IsBlue(int x, int y) => At(x, y) is { A: > 200, R: < 60, G: < 60, B: > 200 };
    }
}
