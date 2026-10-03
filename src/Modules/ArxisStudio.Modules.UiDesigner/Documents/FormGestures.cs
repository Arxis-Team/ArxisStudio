using System.Globalization;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Sdk;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Xaml;
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
/// находится путём уже внутри правки, в тексте, каким его оставили правки перед ней: правка идёт
/// очередью документа, а элемент прежнего разбора редактор отвергнет.
/// </para>
/// <para>
/// <b>Удаление и перестановку делает текст.</b> Ядро дерево не правит: пока на просьбу никто не ответил,
/// ничего не происходит. Ответ здесь — правка документа, а живое дерево перестраивает уже она.
/// </para>
/// </remarks>
internal sealed class FormGestures : IDisposable
{
    private readonly UiDesignerView _sheet;
    private readonly UiDesignerFormItem _form;
    private readonly IXamlDocumentHandle _document;
    private readonly Func<IXamlDesignView?> _view;
    private readonly IStudioStrings _strings;
    private readonly Action<IReadOnlyList<XamlElementPath>> _select;
    private readonly Action<string> _refused;

    /// <summary>Слушает жесты холста.</summary>
    /// <param name="sheet">Холст.</param>
    /// <param name="form">Карточка формы: она и есть корень документа.</param>
    /// <param name="document">Документ формы.</param>
    /// <param name="view">Показ документа сейчас; null — показа нет.</param>
    /// <param name="strings">Словарь модуля: имена шагов истории.</param>
    /// <param name="select">Что выбрать, когда правка, сдвинувшая пути, показана.</param>
    /// <param name="refused">Куда сказать, что жест не записан.</param>
    public FormGestures(
        UiDesignerView sheet,
        UiDesignerFormItem form,
        IXamlDocumentHandle document,
        Func<IXamlDesignView?> view,
        IStudioStrings strings,
        Action<IReadOnlyList<XamlElementPath>> select,
        Action<string> refused)
    {
        _sheet = sheet;
        _form = form;
        _document = document;
        _view = view;
        _strings = strings;
        _select = select;
        _refused = refused;

        sheet.EditCompleted += OnEditCompleted;
        sheet.DeleteRequested += OnDeleteRequested;
        sheet.ReorderRequested += OnReorderRequested;
        sheet.UndoRequested += OnUndoRequested;
        sheet.RedoRequested += OnRedoRequested;
    }

    /// <summary>Отменяет или возвращает шаг истории документа.</summary>
    /// <param name="back">Отменить, а не вернуть.</param>
    public async Task StepAsync(bool back)
    {
        try
        {
            _ = back ? await _document.UndoAsync() : await _document.RedoAsync();
        }
        catch (ObjectDisposedException)
        {
            // Документ закрыт: отменять нечего.
        }
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

    private void OnEditCompleted(object? sender, SurfaceEditCompletedEventArgs e)
    {
        if (e.Kind is not (SurfaceEditKind.Move or SurfaceEditKind.Resize))
            return;

        var writes = new List<Write>();

        foreach (var change in e.Changes.OfType<GeometryChange>())
        {
            // Карточка — сама форма: тянут её ручки — меняют размер корня. Где карточка стоит на холсте,
            // документ не знает: положение у формы есть только в том, что её показывает.
            if (ReferenceEquals(change.Target, _form))
            {
                if (e.Kind == SurfaceEditKind.Resize)
                    Size(writes, XamlElementPath.Root, change);

                continue;
            }

            if (_view()?.PathOf(change.Target) is not { } path)
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

        writes.RemoveAll(write => write.Value.Length == 0);

        if (writes.Count == 0)
            return;

        var label = string.Format(
            CultureInfo.CurrentCulture,
            _strings[e.Kind == SurfaceEditKind.Move ? "form.edit.position" : "form.edit.size"],
            NameOf(writes[0].Path));

        _ = EditAsync(label, editor =>
        {
            foreach (var write in writes)
            {
                if (write.Path.Resolve(editor.Document) is not { } element)
                    continue;

                if (write.Plain)
                    editor.SetAttribute(element, XamlQualifiedName.Parse(write.Name), write.Value);
                else
                    WriteSize(editor, element, write.Name, write.Value);
            }
        }, select: null);
    }

    private void OnDeleteRequested(object? sender, SurfaceDeleteRequestedEventArgs e)
    {
        // Корень удалить нечем: без него нет документа. Остальное уходит одной правкой.
        var paths = e.Targets
            .Where(target => !ReferenceEquals(target.Target, _form))
            .Select(target => _view()?.PathOf(target.Target))
            .OfType<XamlElementPath>()
            .Where(path => !path.Equals(XamlElementPath.Root))
            .Distinct()
            .ToList();

        // Ответ на просьбу и есть удаление: без него ядро не сделало бы ничего, а клавиша ушла бы дальше.
        e.Handled = true;

        if (paths.Count == 0)
            return;

        var label = paths.Count == 1
            ? string.Format(CultureInfo.CurrentCulture, _strings["form.edit.delete"], NameOf(paths[0]))
            : string.Format(CultureInfo.CurrentCulture, _strings["form.edit.deleteMany"], paths.Count);

        // Удалённое выбрать нельзя: выбор переходит к тому, в чём оно стояло, — его путь удаление не двигает.
        var after = paths.Select(path => path.Parent ?? XamlElementPath.Root).Take(1).ToList();

        _ = EditAsync(label, editor =>
        {
            // Все элементы — из одного текста и до удаления: пути разрешены против него. Вложенное в
            // удаляемое уходит вместе с ним и отдельной правкой не пишется — правки перекрылись бы.
            var elements = paths.Select(path => path.Resolve(editor.Document)).OfType<XamlElement>().ToList();

            foreach (var element in elements.Where(element => !elements.Any(other => IsInside(element, other))))
                editor.RemoveElement(element);
        }, after);
    }

    private void OnReorderRequested(object? sender, UiDesignerReorderRequestedEventArgs e)
    {
        if (_view() is not { } view || view.PathOf(e.Target) is not { Steps.Length: > 0 } path)
            return;

        var anchor = e.Anchor is { } before ? view.PathOf(before) : null;

        // Ядро не начнёт перестановку в потоке, если на неё никто не отвечает: ответ здесь и есть жест.
        e.Handled = true;

        var label = string.Format(CultureInfo.CurrentCulture, _strings["form.edit.move"], NameOf(path));
        var moved = new List<XamlElementPath>(1);

        _ = EditAsync(label, editor =>
        {
            if (path.Resolve(editor.Document) is not { Parent: XamlElement parent } element)
                return;

            // Якорь, а не индекс ядра: ядро считает детей панели, документ — элементы содержимого, и у сетки
            // с определениями строк они расходятся. Сосед, перед которым встаёт контрол, переживает разницу.
            var siblings = parent.ContentElements.ToList();
            var from = siblings.IndexOf(element);
            var to = anchor?.Resolve(editor.Document) is { } at ? siblings.IndexOf(at) : siblings.Count;

            if (from < 0 || to < 0 || to == from || to == from + 1)
                return;

            editor.MoveElement(element, parent, to);

            // Перенос — удаление и вставка против прежнего текста: стоявший выше места встаёт на одну позицию
            // раньше названной.
            moved.Add(Moved(path, to > from ? to - 1 : to));
        }, moved);
    }

    private void OnUndoRequested(object? sender, SurfaceHistoryRequestedEventArgs e)
    {
        e.Handled = true;
        _ = StepAsync(back: true);
    }

    private void OnRedoRequested(object? sender, SurfaceHistoryRequestedEventArgs e)
    {
        e.Handled = true;
        _ = StepAsync(back: false);
    }

    /// <summary>Правит документ; правка показана — выбирает, что сказано.</summary>
    /// <param name="label">Имя шага истории.</param>
    /// <param name="edit">Правка.</param>
    /// <param name="select">Что выбрать после неё; null — выбор остаётся.</param>
    private async Task EditAsync(string label, Action<XamlDocumentEditor> edit, IReadOnlyList<XamlElementPath>? select)
    {
        try
        {
            var outcome = await _document.EditAsync(label, edit);

            if (outcome.TextChanged && select is { Count: > 0 })
                _select(select);
        }
        catch (ObjectDisposedException)
        {
            // Документ закрыт, пока шёл жест: писать некуда.
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            // Редактор отказал: правка, которой синтаксис не допускает, или перекрытые правки. В тексте не
            // изменилось ничего, и человеку говорится, почему жест не записан.
            _refused(e.Message);
        }
    }

    /// <summary>Как назвать элемент в имени шага: тип, а с именем — тип и имя.</summary>
    private string NameOf(XamlElementPath path) =>
        path.Resolve(_document.Syntax) is { } element
            ? element.Identity is { Length: > 0 } name ? $"{element.Name.LocalName} «{name}»" : element.Name.LocalName
            : path.ToString();

    /// <summary>
    /// Ширина и высота — той дорогой, какой размер читает дизайнер, и только сменившиеся: тянули правый
    /// край — высота остаётся за раскладкой, а не застывает числом.
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

    /// <summary>
    /// Размер пишется туда, откуда его берёт дизайнер.
    /// </summary>
    /// <remarks>
    /// Шаблоны пишут окна с <c>d:DesignWidth</c> и <c>d:DesignHeight</c> и без <c>Width</c>, а в режиме
    /// дизайна побеждают именно они: размер, записанный в <c>Width</c>, лёг бы в файл, а живое окно
    /// вернулось бы к размеру дизайна с первым же обновлением. Поэтому элемент, назвавший размер дизайна,
    /// получает его; обычный атрибут — тоже, если он уже написан: двух разных чисел об одном в файле не
    /// остаётся.
    /// </remarks>
    private static void WriteSize(XamlDocumentEditor editor, XamlElement element, string name, string value)
    {
        var design = element.DesignTimeAttributes.FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, "Design" + name, StringComparison.Ordinal));

        if (design is not null)
            editor.SetAttribute(element, design.Name, value);

        if (design is null || element.GetAttribute(name) is not null)
            editor.SetAttribute(element, XamlQualifiedName.Parse(name), value);
    }

    /// <summary>Путь соседа: тот же родитель, другое место среди его содержимого.</summary>
    private static XamlElementPath Moved(XamlElementPath path, int index)
    {
        var last = path.Steps[^1];
        var parent = path.Parent is { Steps.IsEmpty: false } above ? above.ToString() : string.Empty;

        return XamlElementPath.Parse(last.MemberName is null
            ? string.Create(CultureInfo.InvariantCulture, $"{parent}/{index}")
            : string.Create(CultureInfo.InvariantCulture, $"{parent}/{last.MemberName}:{index}"));
    }

    private static string Whole(double value) =>
        double.IsFinite(value) ? Math.Round(value).ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Лежит ли элемент внутри другого.</summary>
    private static bool IsInside(XamlElement element, XamlElement other)
    {
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, other))
                return true;
        }

        return false;
    }

    /// <summary>Запись одного атрибута: где, что и как — плоско или дорогой размера.</summary>
    private sealed record Write(XamlElementPath Path, string Name, string Value, bool Plain);
}
