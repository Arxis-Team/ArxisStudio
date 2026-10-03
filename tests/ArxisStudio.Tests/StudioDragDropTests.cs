using ArxisStudio.Dragging;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Перетаскивание между панелями: договор SDK и то, как его держит студия, — без окна проекта и доски.
/// </summary>
/// <remarks>
/// Стенд — окно с источником, целью внутри цели и местом без цели. Источник ведёт тягу сеансом, как
/// окно проекта, или отдаёт её студии целиком — <c>DragAsync</c>, как сделает почти любой плагин.
/// </remarks>
public class StudioDragDropTests
{
    private static readonly StudioDragVisual Visual = new("Main.axaml");

    /// <summary>
    /// Цель — ближайший элемент, объявивший себя целью: курсор пришёл — DragEnter и сразу DragOver, и её
    /// ответ — курсор и подсказка у курсора. Внешняя цель отвеченного события не слышит.
    /// </summary>
    [AvaloniaFact]
    public void The_nearest_target_hears_enter_then_over_and_its_answer_is_shown()
    {
        using var bench = new Bench();

        var outer = 0;

        bench.Answer(bench.Inner, DragDropEffects.Link, "сюда");
        StudioDragDrop.AddDragOverHandler(bench.Outer, (_, _) => outer++);

        var before = bench.Source.Cursor;
        var session = bench.Begin();

        Assert.Equal(DragDropEffects.Link, session.Over(bench.At(bench.Inner), KeyModifiers.None));
        Assert.Equal(["Inner:DragEnter", "Inner:DragOver"], bench.Heard);
        Assert.Equal(0, outer);
        Assert.Equal("сюда", session.Hint);
        Assert.Equal("сюда", bench.Drags.Ghost!.Hint);
        Assert.True(bench.Drags.Ghost.Answer.IsVisible, "ответ цели у курсора не виден");
        Assert.NotSame(before, bench.Source.Cursor);
        Assert.True(session.IsActive);
    }

    /// <summary>Курсор ушёл с цели — она слышит DragLeave, а ответ — отказ без подсказки.</summary>
    [AvaloniaFact]
    public void Moving_off_a_target_says_leave_and_refuses()
    {
        using var bench = new Bench();

        bench.Answer(bench.Inner, DragDropEffects.Copy, "копия");

        var session = bench.Begin();

        session.Over(bench.At(bench.Inner), KeyModifiers.None);
        bench.Heard.Clear();

        Assert.Equal(DragDropEffects.None, session.Over(bench.At(bench.Plain), KeyModifiers.None));
        Assert.Equal(["Inner:DragLeave"], bench.Heard);
        Assert.Null(session.Hint);
        Assert.Null(bench.Drags.Ghost!.Hint);
        Assert.False(bench.Drags.Ghost.Answer.IsVisible, "у курсора осталась пустая строка ответа");
    }

    /// <summary>
    /// Переход между детьми одной цели её не дёргает — цель та же; с внутренней цели на внешнюю:
    /// внутренняя прощается, внешняя здоровается.
    /// </summary>
    [AvaloniaFact]
    public void Children_of_a_target_do_not_jerk_it_and_the_outer_target_takes_over()
    {
        using var bench = new Bench();

        bench.Answer(bench.Inner, DragDropEffects.Link, null);
        bench.Answer(bench.Outer, DragDropEffects.Copy, null);

        var session = bench.Begin();

        session.Over(bench.At(bench.Inner, 7), KeyModifiers.None);
        bench.Heard.Clear();

        Assert.Equal(DragDropEffects.Link, session.Over(bench.At(bench.Inner), KeyModifiers.None));
        Assert.Equal(["Inner:DragOver"], bench.Heard);

        bench.Heard.Clear();

        Assert.Equal(DragDropEffects.Copy, session.Over(bench.At(bench.Outer, 10), KeyModifiers.None));
        Assert.Equal(["Inner:DragLeave", "Outer:DragEnter", "Outer:DragOver"], bench.Heard);
    }

    /// <summary>
    /// Отпустили над целью — Drop, ответ на него и за ним DragLeave: отметку цель убирает в одном месте.
    /// Тяга кончилась — подсказки у курсора нет, курсор прежний.
    /// </summary>
    [AvaloniaFact]
    public void A_drop_answers_and_is_followed_by_leave()
    {
        using var bench = new Bench();

        bench.Answer(bench.Inner, DragDropEffects.Link, null);

        var before = bench.Source.Cursor;
        var session = bench.Begin();

        session.Over(bench.At(bench.Inner), KeyModifiers.None);

        var ghost = bench.Drags.Ghost!;

        Assert.NotNull(ghost.Parent);

        bench.Heard.Clear();

        Assert.Equal(DragDropEffects.Link, session.Drop());
        Assert.Equal(["Inner:Drop", "Inner:DragLeave"], bench.Heard);
        Assert.False(session.IsActive);
        Assert.Null(bench.Drags.Ghost);
        Assert.Null(ghost.Parent);
        Assert.Same(before, bench.Source.Cursor);
        Assert.Equal(DragDropEffects.None, session.Over(bench.At(bench.Inner), KeyModifiers.None));
    }

    /// <summary>Drop начинается с последнего ответа DragOver: цель, решившая там, в Drop может не решать.</summary>
    [AvaloniaFact]
    public void A_drop_starts_from_the_last_answer()
    {
        using var bench = new Bench();
        DragDropEffects? started = null;

        bench.Answer(bench.Inner, DragDropEffects.Copy, "копия");
        StudioDragDrop.AddDropHandler(bench.Inner, (_, e) => started = e.Effect);

        var session = bench.Begin();

        session.Over(bench.At(bench.Inner), KeyModifiers.None);

        Assert.Equal(DragDropEffects.Copy, session.Drop());
        Assert.Equal(DragDropEffects.Copy, started);
    }

    /// <summary>
    /// Эффект, которого источник не разрешил, и составной — отказ, и в журнале строка — одна на цель за
    /// тягу, а не на каждое движение.
    /// </summary>
    [AvaloniaFact]
    public void An_effect_the_source_did_not_allow_is_a_refusal_said_once()
    {
        using var bench = new Bench();

        bench.Answer(bench.Inner, DragDropEffects.Move, null);

        var session = bench.Begin(DragDropEffects.Copy | DragDropEffects.Link);

        Assert.Equal(DragDropEffects.None, session.Over(bench.At(bench.Inner), KeyModifiers.None));
        Assert.Equal(DragDropEffects.None, session.Over(bench.At(bench.Inner) + new Vector(2, 0), KeyModifiers.None));

        var warning = Assert.Single(bench.Log.Records, record => record.Level == StudioLogLevel.Warning);

        Assert.Contains("Move", warning.Message, StringComparison.Ordinal);

        bench.Answer(bench.Outer, DragDropEffects.Copy | DragDropEffects.Link, null);

        Assert.Equal(DragDropEffects.None, session.Over(bench.At(bench.Outer, 10), KeyModifiers.None));
    }

    /// <summary>
    /// Упавший обработчик цели студию не роняет: цель выпадает из тяги, её больше не спрашивают, сбой
    /// уходит виновнику один раз, а отпускание над ней — отказ.
    /// </summary>
    [AvaloniaFact]
    public void A_failing_target_drops_out_of_the_carry()
    {
        var blamed = new List<Exception>();

        using var bench = new Bench(error =>
        {
            blamed.Add(error);
            return true;
        });

        var asked = 0;

        StudioDragDrop.AddDragOverHandler(bench.Inner, (_, _) =>
        {
            asked++;
            throw new InvalidOperationException("цель сломана");
        });

        var session = bench.Begin();

        Assert.Equal(DragDropEffects.None, session.Over(bench.At(bench.Inner), KeyModifiers.None));
        Assert.Equal(["Inner:DragEnter", "Inner:DragOver", "Inner:DragLeave"], bench.Heard);
        Assert.Equal(DragDropEffects.None, session.Over(bench.At(bench.Inner) + new Vector(1, 0), KeyModifiers.None));
        Assert.Equal(DragDropEffects.None, session.Drop());
        Assert.Equal(1, asked);
        Assert.Equal("цель сломана", Assert.Single(blamed).Message);
        Assert.DoesNotContain(bench.Log.Records, record => record.Level == StudioLogLevel.Error);
    }

    /// <summary>Падение, которое приписать некому, говорит в журнал студии.</summary>
    [AvaloniaFact]
    public void A_failure_nobody_owns_goes_to_the_log()
    {
        using var bench = new Bench();

        StudioDragDrop.AddDragOverHandler(bench.Inner, (_, _) => throw new InvalidOperationException("цель сломана"));

        bench.Begin().Over(bench.At(bench.Inner), KeyModifiers.None);

        Assert.Contains("цель сломана", Assert.Single(bench.Log.Records, record => record.Level == StudioLogLevel.Error).Message, StringComparison.Ordinal);
    }

    /// <summary>Курсор цель получает в своих координатах, а клавиши — те, что зажаты.</summary>
    [AvaloniaFact]
    public void The_target_reads_the_cursor_in_its_own_coordinates_and_the_keys()
    {
        using var bench = new Bench();
        Point? seen = null;
        KeyModifiers keys = default;

        StudioDragDrop.AddDragOverHandler(bench.Inner, (_, e) =>
        {
            seen = e.GetPosition(bench.Inner);
            keys = e.KeyModifiers;
        });

        bench.Begin().Over(bench.At(bench.Inner, 7), KeyModifiers.Control);

        Assert.Equal(new Point(7, 7), seen);
        Assert.Equal(KeyModifiers.Control, keys);
    }

    /// <summary>
    /// Над своим отвечает источник: прежняя чужая цель прощается, ответ и подсказка — источника, а
    /// отпускание сеансу нечего отдавать.
    /// </summary>
    [AvaloniaFact]
    public void Over_its_own_the_source_answers_itself()
    {
        using var bench = new Bench();

        bench.Answer(bench.Inner, DragDropEffects.Link, "чужое");

        var session = bench.Begin();

        session.Over(bench.At(bench.Inner), KeyModifiers.None);
        bench.Heard.Clear();

        session.OverOwn(bench.At(bench.Source), DragDropEffects.Move, "своё");

        Assert.Equal(["Inner:DragLeave"], bench.Heard);
        Assert.Equal(DragDropEffects.Move, session.Effect);
        Assert.Equal("своё", bench.Drags.Ghost!.Hint);
        Assert.Equal(DragDropEffects.None, session.Drop());
        Assert.DoesNotContain("Inner:Drop", bench.Heard);
    }

    /// <summary>
    /// Новая тяга бросает прежнюю: её цель слышит DragLeave, сеанс закрыт, у курсора одна подсказка.
    /// </summary>
    [AvaloniaFact]
    public void A_new_carry_drops_the_previous_one()
    {
        using var bench = new Bench();

        bench.Answer(bench.Inner, DragDropEffects.Link, null);

        var first = bench.Begin();

        first.Over(bench.At(bench.Inner), KeyModifiers.None);

        var ghost = bench.Drags.Ghost!;

        bench.Heard.Clear();

        var second = bench.Begin();

        Assert.Equal(["Inner:DragLeave"], bench.Heard);
        Assert.False(first.IsActive);
        Assert.True(second.IsActive);
        Assert.Null(ghost.Parent);
        Assert.Equal(DragDropEffects.None, first.Over(bench.At(bench.Inner), KeyModifiers.None));
    }

    /// <summary>
    /// У курсора — что несут и ответ цели; подсказка лежит в слое оверлеев окна, а сеанс, закрытый
    /// без отпускания, прощается с целью.
    /// </summary>
    [AvaloniaFact]
    public void The_ghost_shows_what_is_carried_and_a_dropped_carry_says_leave()
    {
        using var bench = new Bench();

        bench.Answer(bench.Inner, DragDropEffects.Link, "сюда");

        var session = bench.Begin();

        session.Over(bench.At(bench.Inner), KeyModifiers.None);

        var ghost = bench.Drags.Ghost!;

        Assert.Equal("Main.axaml", ghost.Label);
        Assert.Same(OverlayLayer.GetOverlayLayer(bench.Window), ghost.Parent);
        Assert.True(ghost.IsVisible);

        bench.Heard.Clear();
        session.Dispose();

        Assert.Equal(["Inner:DragLeave"], bench.Heard);
        Assert.Null(ghost.Parent);
    }

    /// <summary>
    /// Тягу, отданную студии, она ведёт сама — от движения, на котором жест стал тягой, до отпускания:
    /// ответ цели приходит задачей, захват отпущен.
    /// </summary>
    [AvaloniaFact]
    public async Task A_carry_the_studio_leads_runs_to_the_release()
    {
        using var bench = new Bench();

        bench.Answer(bench.Inner, DragDropEffects.Link, null);

        var carry = bench.LeadFromSource();

        bench.Window.MouseMove(bench.InWindow(bench.Inner), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Inner:DragEnter", "Inner:DragOver"], bench.Heard);

        bench.Window.MouseUp(bench.InWindow(bench.Inner), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(DragDropEffects.Link, await carry().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(["Inner:DragEnter", "Inner:DragOver", "Inner:DragOver", "Inner:Drop", "Inner:DragLeave"], bench.Heard);
        Assert.Null(bench.Drags.Current);
        Assert.Null(bench.Source.GetValue(InputElement.CursorProperty));
    }

    /// <summary>Esc бросает тягу, которую ведёт студия: цель прощается, ответ — отказ.</summary>
    [AvaloniaFact]
    public async Task Escape_drops_a_carry_the_studio_leads()
    {
        using var bench = new Bench();

        bench.Answer(bench.Inner, DragDropEffects.Link, null);

        var carry = bench.LeadFromSource();

        bench.Window.MouseMove(bench.InWindow(bench.Inner), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        bench.Heard.Clear();

        bench.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(DragDropEffects.None, await carry().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(["Inner:DragLeave"], bench.Heard);
        Assert.Null(bench.Drags.Current);

        IInputElement? captured = bench.Source;

        bench.Window.PointerMoved += (_, e) => captured = e.Pointer.Captured;
        bench.Window.MouseMove(bench.InWindow(bench.Plain), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(captured);

        bench.Window.MouseUp(bench.InWindow(bench.Inner), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("Inner:Drop", bench.Heard);
    }

    /// <summary>
    /// Ctrl, нажатый на месте, спрашивает цель заново — не дожидаясь движения мыши.
    /// </summary>
    [AvaloniaFact]
    public async Task A_modifier_pressed_in_place_asks_the_target_again()
    {
        using var bench = new Bench();

        StudioDragDrop.AddDragOverHandler(bench.Inner, (_, e) =>
            e.Effect = (e.KeyModifiers & KeyModifiers.Control) != 0 ? DragDropEffects.Copy : DragDropEffects.Link);

        var carry = bench.LeadFromSource();

        bench.Window.MouseMove(bench.InWindow(bench.Inner), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(DragDropEffects.Link, bench.Drags.Current!.Effect);

        bench.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(DragDropEffects.Copy, bench.Drags.Current!.Effect);

        bench.Window.MouseUp(bench.InWindow(bench.Inner), MouseButton.Left, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(DragDropEffects.Copy, await carry().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// Окно поверх окна: несут над тем, что выше, — его цель слышит и под курсором его элемент, даже если
    /// источник держит указатель в окне ниже.
    /// </summary>
    [AvaloniaFact]
    public void The_upper_window_wins()
    {
        var windows = new List<TopLevel>();

        using var bench = new Bench(windows: () => windows);

        var upper = new Border { Background = Brushes.Transparent };
        var front = new Window { Width = Bench.Width, Height = Bench.Height, Content = upper, Position = bench.Window.Position };

        StudioDragDrop.SetAllowDrop(upper, true);
        StudioDragDrop.AddDragOverHandler(upper, (_, e) => e.Effect = DragDropEffects.Copy);
        bench.Answer(bench.Inner, DragDropEffects.Link, null);
        front.Show();
        windows.Add(front);
        windows.Add(bench.Window);
        Bench.Frame();

        try
        {
            var session = bench.Begin();

            Assert.Same(upper, session.ElementAt(bench.At(bench.Inner)));
            Assert.Equal(DragDropEffects.Copy, session.Over(bench.At(bench.Inner), KeyModifiers.None));
            Assert.DoesNotContain("Inner:DragOver", bench.Heard);
            Assert.Same(OverlayLayer.GetOverlayLayer(front), bench.Drags.Ghost!.Parent);

            session.Dispose();
        }
        finally
        {
            front.Close();
        }
    }

    /// <summary>Пути несут абсолютными: относительный значил бы у цели не тот файл.</summary>
    [Fact]
    public void Files_are_carried_by_absolute_paths()
    {
        var path = Path.Combine(Path.GetTempPath(), "Main.axaml");
        var data = StudioDragData.FromFiles([path]);

        Assert.Equal([path], data.Files);
        Assert.Contains("arxis.files", data.Formats);
        Assert.Throws<ArgumentException>(() => StudioDragData.FromFiles(["Views/Main.axaml"]));
        Assert.Throws<ArgumentException>(() => StudioDragData.FromFiles([""]));
    }

    /// <summary>
    /// Данные неизменяемы: значение кладётся в новые данные, а формат отдаёт значение только своего типа.
    /// </summary>
    [Fact]
    public void Data_is_immutable_and_typed()
    {
        var count = new StudioDataFormat<int>("tests.count");
        var name = new StudioDataFormat<string>("tests.count");
        var empty = new StudioDragData();
        var full = empty.With(count, 3);

        Assert.False(empty.Contains(count));
        Assert.Empty(empty.Files);
        Assert.True(full.TryGet(count, out var value));
        Assert.Equal(3, value);
        Assert.False(full.TryGet(name, out _));
        Assert.Equal(5, full.With(count, 5).TryGet(count, out var replaced) ? replaced : 0);
        Assert.Equal(3, full.TryGet(count, out var kept) ? kept : 0);
        Assert.Throws<ArgumentException>(() => new StudioDataFormat<int>(" "));
    }

    /// <summary>
    /// Окно с источником слева, целью с целью внутри справа и местом без цели внизу.
    /// </summary>
    private sealed class Bench : IDisposable
    {
        public const double Width = 600;
        public const double Height = 400;

        public Bench(Func<Exception, bool>? blame = null, Func<IEnumerable<TopLevel>>? windows = null)
        {
            Source = Plate();
            Outer = Plate();
            Inner = Plate();
            Plain = Plate();
            Speck = Plate();
            Speck.Margin = new Thickness(25);
            Inner.Margin = new Thickness(100);
            Inner.Child = Speck;
            Outer.Child = Inner;

            var canvas = new Canvas();

            Place(canvas, Source, 0, 0, 100, 100);
            Place(canvas, Outer, 200, 0, 300, 300);
            Place(canvas, Plain, 0, 200, 100, 100);

            StudioDragDrop.SetAllowDrop(Outer, true);
            StudioDragDrop.SetAllowDrop(Inner, true);
            Listen(Outer, "Outer");
            Listen(Inner, "Inner");

            Window = new Window { Width = Width, Height = Height, Content = canvas, Position = new PixelPoint(0, 0) };
            Drags = new StudioDrags(windows ?? (() => [Window]), Log, blame);
            Window.Show();
            Frame();
        }

        /// <summary>
        /// Даёт окну кадр: попадание идёт по сцене отрисовки, а новому окну её кладёт только такт — без
        /// него курсор над целью пришёлся бы на пустое место.
        /// </summary>
        public static void Frame()
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        public Window Window { get; }

        public Border Source { get; }

        public Border Outer { get; }

        public Border Inner { get; }

        public Border Plain { get; }

        /// <summary>Ребёнок внутренней цели, сам не цель: курсор над ним — над ней.</summary>
        public Border Speck { get; }

        public StudioLog Log { get; } = new();

        public StudioDrags Drags { get; }

        public List<string> Heard { get; } = [];

        /// <summary>Открывает сеанс от источника.</summary>
        public IStudioDragSession Begin(DragDropEffects allowed = DragDropEffects.Copy | DragDropEffects.Link) =>
            Drags.Begin(Source, StudioDragData.FromFiles([Path.Combine(Path.GetTempPath(), "Main.axaml")]), allowed, Visual);

        /// <summary>Цель отвечает всегда одно и то же и отмечает событие обработанным.</summary>
        public void Answer(Border target, DragDropEffects effect, string? hint) =>
            StudioDragDrop.AddDragOverHandler(target, (_, e) =>
            {
                e.Effect = effect;
                e.Hint = hint;
                e.Handled = true;
            });

        /// <summary>Точка элемента в координатах источника: середина или отступ от угла.</summary>
        public Point At(Visual target, double? inset = null) =>
            target.TranslatePoint(Inside(target, inset), Source)!.Value;

        /// <summary>Точка элемента в координатах окна.</summary>
        public Point InWindow(Visual target) => target.TranslatePoint(Inside(target, null), Window)!.Value;

        /// <summary>
        /// Нажимает на источник и ведёт мышь дальше порога: обработчик источника отдаёт тягу студии.
        /// </summary>
        /// <returns>Как достать ответ тяги, когда она кончится.</returns>
        public Func<Task<DragDropEffects>> LeadFromSource()
        {
            Task<DragDropEffects>? carry = null;

            Source.PointerMoved += (_, e) =>
            {
                if (carry is null && e.GetCurrentPoint(Source).Properties.IsLeftButtonPressed)
                    carry = Drags.DragAsync(Source, e, StudioDragData.FromFiles([Path.Combine(Path.GetTempPath(), "Main.axaml")]), DragDropEffects.Copy | DragDropEffects.Link, Visual);
            };

            var at = InWindow(Source);

            Window.MouseMove(at);
            Window.MouseDown(at, MouseButton.Left);
            Window.MouseMove(at + new Vector(StudioDragDrop.Threshold * 2, 0), RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(carry);

            return () => carry!;
        }

        public void Dispose()
        {
            Drags.Current?.Dispose();
            Window.Close();
        }

        private static Point Inside(Visual target, double? inset) =>
            inset is { } offset ? new Point(offset, offset) : new Point(target.Bounds.Width / 2, target.Bounds.Height / 2);

        private static Border Plate() => new() { Background = Brushes.Transparent };

        private static void Place(Canvas canvas, Control control, double left, double top, double width, double height)
        {
            control.Width = width;
            control.Height = height;
            Canvas.SetLeft(control, left);
            Canvas.SetTop(control, top);
            canvas.Children.Add(control);
        }

        private void Listen(Border target, string name)
        {
            StudioDragDrop.AddDragEnterHandler(target, (_, e) => Hear(target, name, "DragEnter", e));
            StudioDragDrop.AddDragOverHandler(target, (_, e) => Hear(target, name, "DragOver", e));
            StudioDragDrop.AddDragLeaveHandler(target, (_, e) => Hear(target, name, "DragLeave", e));
            StudioDragDrop.AddDropHandler(target, (_, e) => Hear(target, name, "Drop", e));
        }

        /// <summary>Слышит только свои события: всплывшее от вложенной цели — её, а не этой.</summary>
        private void Hear(Border target, string name, string what, StudioDragEventArgs e)
        {
            if (ReferenceEquals(e.Source, target))
                Heard.Add($"{name}:{what}");
        }
    }
}
