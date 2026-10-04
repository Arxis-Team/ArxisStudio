using System.Globalization;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Sdk;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;
using Avalonia.Controls;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>
/// Жесты холста — правками текста формы: сдвиг и размер, удаление, перестановка, отмена и возврат.
/// </summary>
/// <remarks>
/// <para>
/// <b>По концу жеста, а не по шагу.</b> Тяга даёт сотни промежуточных положений, и фактом о форме
/// становится только то, на котором отпустили: правка на каждый кадр перестраивала бы живое дерево под
/// мышью. Ядро сообщает конец жеста (<see cref="SurfaceView.EditCompleted"/>) — одна правка, один шаг
/// истории документа.
/// </para>
/// <para>
/// <b>Числа — сейчас, элементы — в правке.</b> Числа снимаются при событии: размер — из того, что ядро
/// записало в изменение, положение в <c>Canvas</c> — с контрола, куда его поставил шов ядра. Элемент
/// находится путём уже внутри правки (<see cref="FormEdits"/>).
/// </para>
/// <para>
/// <b>Удаление и перестановку делает текст.</b> Ядро дерево не правит: пока на просьбу никто не ответил,
/// ничего не происходит. Ответ здесь — правка документа, а живое дерево перестраивает уже она.
/// </para>
/// </remarks>
internal sealed class FormGestures : IDisposable
{
    private readonly UiDesignerView _sheet;
    private readonly FormCanvas _canvas;
    private readonly IStudioStrings _strings;

    /// <summary>Слушает жесты холста.</summary>
    /// <param name="sheet">Холст.</param>
    /// <param name="canvas">Формы на холсте: жест правит ту, в которой сделан.</param>
    /// <param name="strings">Словарь модуля: имена шагов истории.</param>
    public FormGestures(UiDesignerView sheet, FormCanvas canvas, IStudioStrings strings)
    {
        _sheet = sheet;
        _canvas = canvas;
        _strings = strings;

        sheet.EditCompleted += OnEditCompleted;
        sheet.DeleteRequested += OnDeleteRequested;
        sheet.ReorderRequested += OnReorderRequested;
        sheet.UndoRequested += OnUndoRequested;
        sheet.RedoRequested += OnRedoRequested;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _sheet.EditCompleted -= OnEditCompleted;
        _sheet.DeleteRequested -= OnDeleteRequested;
        _sheet.ReorderRequested -= OnReorderRequested;
        _sheet.UndoRequested -= OnUndoRequested;
        _sheet.RedoRequested -= OnRedoRequested;
    }

    /// <remarks>
    /// Жест, задевший несколько форм, — по правке на каждую: у каждой своя история, и Ctrl+Z во вкладке формы
    /// отменяет только её.
    /// </remarks>
    private void OnEditCompleted(object? sender, SurfaceEditCompletedEventArgs e)
    {
        if (e.Kind is not (SurfaceEditKind.Move or SurfaceEditKind.Resize))
            return;

        var forms = new List<(FormSlot Slot, List<Write> Writes)>();

        foreach (var change in e.Changes.OfType<GeometryChange>())
        {
            if (_canvas.SlotOf(change.Target) is not { } slot)
                continue;

            var writes = forms.Find(form => ReferenceEquals(form.Slot, slot)).Writes;

            if (writes is null)
            {
                writes = [];
                forms.Add((slot, writes));
            }

            // Карточка — сама форма: тянут её ручки — меняют размер корня. Где карточка стоит на холсте,
            // документ не знает: положение у формы есть только в том, что её показывает.
            if (ReferenceEquals(change.Target, slot.Item))
            {
                if (e.Kind == SurfaceEditKind.Resize)
                    Size(writes, XamlElementPath.Root, change);

                continue;
            }

            if (slot.Shown?.PathOf(change.Target) is not { } path)
                continue;

            if (e.Kind == SurfaceEditKind.Resize)
                Size(writes, path, change);

            // Положение пишется туда, где его держит родитель. Сетка и панели потока держат его сами, и ядро
            // такой сдвиг отсекает на шве; перестановку в потоке оно присылает своей просьбой. В Canvas
            // положение меняет и размер, взятый за левый или верхний край.
            if (change.Target.Parent is Canvas)
            {
                if (Changed(change.OldBounds.X, change.NewBounds.X))
                    writes.Add(new Write(path, "Canvas.Left", Whole(Canvas.GetLeft(change.Target)), Plain: true));

                if (Changed(change.OldBounds.Y, change.NewBounds.Y))
                    writes.Add(new Write(path, "Canvas.Top", Whole(Canvas.GetTop(change.Target)), Plain: true));
            }
        }

        foreach (var (slot, writes) in forms)
        {
            writes.RemoveAll(write => write.Value.Length == 0);

            if (writes.Count == 0 || slot.Session.Edits is not { } edits)
                continue;

            var label = string.Format(
                CultureInfo.CurrentCulture,
                _strings[e.Kind == SurfaceEditKind.Move ? "form.edit.position" : "form.edit.size"],
                edits.NameOf(writes[0].Path));

            _ = edits.EditAsync(label, editor =>
            {
                foreach (var write in writes)
                {
                    if (write.Path.Resolve(editor.Document) is not { } element)
                        continue;

                    if (write.Plain)
                        editor.SetAttribute(element, XamlQualifiedName.Parse(write.Name), write.Value);
                    else
                        FormEdits.Size(editor, element, write.Name, write.Value);
                }
            }, select: null);
        }
    }

    /// <remarks>
    /// Корень удалить нечем: без него нет документа, — форму, выбранную целиком, отдают хозяину холста, а
    /// остальное уходит одной правкой на форму.
    /// </remarks>
    private void OnDeleteRequested(object? sender, SurfaceDeleteRequestedEventArgs e)
    {
        var whole = new List<FormSlot>();
        var parts = new List<(FormSlot Slot, List<XamlElementPath> Paths)>();

        foreach (var target in e.Targets)
        {
            if (_canvas.SlotOf(target.Target) is not { } slot)
                continue;

            if (ReferenceEquals(target.Target, slot.Item))
            {
                if (!whole.Contains(slot))
                    whole.Add(slot);

                continue;
            }

            if (slot.Shown?.PathOf(target.Target) is not { } path)
                continue;

            var paths = parts.Find(part => ReferenceEquals(part.Slot, slot)).Paths;

            if (paths is null)
            {
                paths = [];
                parts.Add((slot, paths));
            }

            paths.Add(path);
        }

        // Ответ на просьбу и есть удаление: без него ядро не сделало бы ничего, а клавиша ушла бы дальше.
        e.Handled = true;

        foreach (var (slot, paths) in parts)
        {
            if (slot.Session.Edits is { } edits)
                _ = edits.DeleteAsync(paths);
        }

        if (whole.Count > 0)
            _canvas.Host.Remove(whole);
    }

    private void OnReorderRequested(object? sender, UiDesignerReorderRequestedEventArgs e)
    {
        if (_canvas.SlotOf(e.Target) is not { Shown: { } view, Session.Edits: { } edits }
            || view.PathOf(e.Target) is not { Steps.Length: > 0 } path)
        {
            return;
        }

        // Ядро не начнёт перестановку в потоке, если на неё никто не отвечает: ответ здесь и есть жест.
        e.Handled = true;

        _ = edits.MoveAsync(path, e.Anchor is { } before ? view.PathOf(before) : null);
    }

    private void OnUndoRequested(object? sender, SurfaceHistoryRequestedEventArgs e)
    {
        e.Handled = true;
        _canvas.Host.Step(back: true);
    }

    private void OnRedoRequested(object? sender, SurfaceHistoryRequestedEventArgs e)
    {
        e.Handled = true;
        _canvas.Host.Step(back: false);
    }

    /// <summary>
    /// Ширина и высота — только сменившиеся: тянули правый край — высота остаётся за раскладкой, а не
    /// застывает числом.
    /// </summary>
    private static void Size(List<Write> writes, XamlElementPath path, GeometryChange change)
    {
        if (Changed(change.OldBounds.Width, change.NewBounds.Width))
            writes.Add(new Write(path, "Width", Whole(change.NewBounds.Width), Plain: false));

        if (Changed(change.OldBounds.Height, change.NewBounds.Height))
            writes.Add(new Write(path, "Height", Whole(change.NewBounds.Height), Plain: false));
    }

    /// <summary>Сдвинулось ли число так, что это видно в файле целыми.</summary>
    private static bool Changed(double before, double after) => Math.Abs(before - after) >= 0.5;

    private static string Whole(double value) =>
        double.IsFinite(value) ? Math.Round(value).ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Запись одного атрибута: где, что и как — плоско или дорогой размера.</summary>
    private sealed record Write(XamlElementPath Path, string Name, string Value, bool Plain);
}
