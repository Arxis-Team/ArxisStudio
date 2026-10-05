using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ArxisStudio.Modules.UiDesigner.Panels;

// Тяга на доску: заготовки форм в точке курсора, ответ цели и постановка.
// Часть BoardPanel; общее описание типа — в BoardPanel.cs.
public sealed partial class BoardPanel
{
    private IReadOnlyList<CanonicalPath> _previewed = [];

    /// <summary>
    /// Над доской несут: формы решения — заготовки в точке курсора и ответ «ссылка», прочее — отказ и
    /// почему.
    /// </summary>
    /// <remarks>
    /// Отвечает доска на каждое движение заново — так велит договор цели: ответ, не данный сейчас, —
    /// отказ. Разбор дешёвый — пути сверяются со снимком, который доска уже держит.
    /// </remarks>
    private void OnDragOver(object? sender, StudioDragEventArgs e)
    {
        e.Handled = true;

        if (_view is not { } view)
            return;

        // Решение закрылось посреди тяги — ставить некуда, и заготовке на доске не место.
        if (_model is not { IsReady: true } model)
        {
            Unland(view);
            return;
        }

        var files = e.Data.Files;
        var landings = model.Landings(files);
        var effect = EffectFor(e.AllowedEffects);

        if (landings.Count == 0 || effect == DragDropEffects.None)
        {
            e.Hint = landings.Count == 0 ? Refusal(files) : null;
            Unland(view);

            return;
        }

        e.Effect = effect;
        e.Hint = HintFor(landings);
        Preview(view, landings, SpotsAt(view.Sheet, e.GetPosition(view.Sheet), landings));
    }

    private void OnDragLeave(object? sender, StudioDragEventArgs e)
    {
        if (_view is { } view)
            Unland(view);
    }

    /// <summary>
    /// Отпустили над доской: формы встают в точку отпускания одной записью истории, выбранными, а
    /// клавиатура переходит к холсту — следующее нажатие Ctrl+Z отменит именно это.
    /// </summary>
    private void OnDrop(object? sender, StudioDragEventArgs e)
    {
        e.Handled = true;

        if (_view is not { } view || _model is not { IsReady: true } model || _history is not { } history)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        var landings = model.Landings(e.Data.Files);
        var effect = EffectFor(e.AllowedEffects);

        if (landings.Count == 0 || effect == DragDropEffects.None)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        var sheet = view.Sheet;
        var spots = SpotsAt(sheet, e.GetPosition(sheet), landings);
        var (cards, change) = model.Land([.. landings.Select((landing, index) => (landing.Path, spots[index]))]);

        if (change is not null)
            history.Push(change);

        sheet.Selection.Clear();

        foreach (var card in cards)
            sheet.Selection.Select(model.Cards.IndexOf(card));

        _controls?.Back();
        e.Effect = effect;
    }

    /// <summary>Ссылка, а не копия: доска показывает файл, а не забирает его. Переноса доска не просит.</summary>
    private static DragDropEffects EffectFor(DragDropEffects allowed) =>
        (allowed & DragDropEffects.Link) != 0 ? DragDropEffects.Link
        : (allowed & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy
        : DragDropEffects.None;

    /// <summary>Что сделает отпускание: вернуть убранную, передвинуть стоящую или поставить несколько.</summary>
    private string HintFor(IReadOnlyList<Landing> landings) => landings switch
    {
        [{ OnBoard: true } one] => Say("board.drop.move", one.Card.Name),
        [var one] => Say("board.drop.return", one.Card.Name),
        _ => Say("board.drop.many", landings.Count),
    };

    /// <summary>
    /// Почему нельзя: среди несомого нет разметки — или есть, но не форма решения. Несут не файлы —
    /// сказать нечего: это не к доске.
    /// </summary>
    private string? Refusal(IReadOnlyList<string> files)
    {
        var markup = files.Where(file => file.EndsWith(FormFiles.Extension, StringComparison.OrdinalIgnoreCase)).ToList();

        return (files.Count, markup) switch
        {
            (0, _) => null,
            (_, []) => Context.Strings["board.drop.none"],
            (_, [var one]) => Say("board.drop.notForm.one", Path.GetFileName(one)),
            _ => Context.Strings["board.drop.notForm.many"],
        };
    }

    /// <summary>
    /// Места под курсором: первая форма встаёт серединой под него — сама форма, заголовок окна над ней, —
    /// следующие — рядами от неё.
    /// </summary>
    /// <remarks>
    /// Серединой, а не углом: подсказка у курсора лежит справа снизу от него и закрывала бы форму, вставшую
    /// углом. Места — в целых точках, как у тяги по холсту.
    /// </remarks>
    private IReadOnlyList<Spot> SpotsAt(Control sheet, Point point, IReadOnlyList<Landing> landings)
    {
        var world = ((Surface.SurfaceView)sheet).GetWorldPosition(point);
        var metrics = Metrics();
        var boxes = landings.Select(landing => metrics.BoxOf(landing.Card)).ToList();
        var first = boxes[0];
        var origin = new Spot(
            Math.Round(world.X - first.Width / 2),
            Math.Round(world.Y - first.Height / 2 - first.Above));

        return BoardLayout.Rows(boxes, origin, metrics.Gap);
    }

    /// <summary>
    /// Ставит заготовки форм в места, где они встанут, — в масштабе холста; состояния прячутся.
    /// </summary>
    /// <remarks>
    /// Заготовка — прямоугольник размера формы языком цели перетаскивания: заливка сообщения и кольцо
    /// акцента. Строятся заготовки, когда сменился состав несомого, а на движение мыши только переезжают;
    /// состав сверяется путями. Кольцо масштабом не толстеет и не тает: его толщина у темы делится на
    /// масштаб холста.
    /// </remarks>
    private void Preview(BoardView view, IReadOnlyList<Landing> landings, IReadOnlyList<Spot> spots)
    {
        var sheet = view.Sheet;
        var preview = view.Preview;
        var zoom = sheet.ViewportZoom;
        var corner = sheet.ViewportLocation;
        var metrics = Metrics();

        if (!_previewed.SequenceEqual(landings.Select(landing => landing.Path)))
        {
            preview.Children.Clear();

            foreach (var landing in landings)
            {
                var box = metrics.BoxOf(landing.Card);

                preview.Children.Add(new Border
                {
                    Classes = { "landing" },
                    Width = box.Width,
                    Height = box.Height,
                    RenderTransformOrigin = RelativePoint.TopLeft,
                });
            }

            _previewed = [.. landings.Select(landing => landing.Path)];
        }

        var ring = view.TryFindResource("AxDropTargetThickness", view.ActualThemeVariant, out var value) && value is Thickness thickness
            ? new Thickness(thickness.Left / zoom, thickness.Top / zoom, thickness.Right / zoom, thickness.Bottom / zoom)
            : default;

        for (var index = 0; index < preview.Children.Count && index < spots.Count; index++)
        {
            var ghost = (Border)preview.Children[index];

            Avalonia.Controls.Canvas.SetLeft(ghost, (spots[index].X - corner.X) * zoom);
            Avalonia.Controls.Canvas.SetTop(ghost, (spots[index].Y - corner.Y) * zoom);
            ghost.RenderTransform = new ScaleTransform(zoom, zoom);
            ghost.BorderThickness = ring;
        }

        view.States.IsVisible = false;
    }

    /// <summary>Убирает заготовки и возвращает состояния доски.</summary>
    private void Unland(BoardView view)
    {
        view.Preview.Children.Clear();
        view.States.IsVisible = true;
        _previewed = [];
    }
}
