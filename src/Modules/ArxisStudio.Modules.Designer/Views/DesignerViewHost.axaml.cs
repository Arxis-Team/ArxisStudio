using ArxisStudio.Shell.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Highlighting;

namespace ArxisStudio.Modules.Designer;

/// <summary>
/// Контрол вкладки дизайнера. Отделён от <see cref="DesignerDocumentView"/>,
/// потому что представление — контракт SDK, а контрол — Avalonia со своей
/// разметкой.
/// </summary>
internal sealed partial class DesignerViewHost : UserControl
{
    private readonly DesignDocument _document;

    // Правка текста доходит до документа не с каждым нажатием клавиши: пока
    // человек набирает, разметка почти всегда сломана.
    private readonly DispatcherTimer _xamlIdle = new() { Interval = TimeSpan.FromMilliseconds(700) };

    // Текст в редакторе и текст документа обновляют друг друга, и каждое
    // обновление поднимает событие другого. Флаг гасит это эхо.
    private bool _fillingEditor;

    /// <summary>Создаёт контрол для открытого документа.</summary>
    /// <param name="document">Открытый документ дизайнера.</param>
    public DesignerViewHost(DesignDocument document)
    {
        _document = document;

        InitializeComponent();
        ViewSwitch.SelectedIndex = 0;

        // Приём перетаскивания — присоединённые события, и в разметке их
        // атрибутом не назначить.
        DragDrop.AddDragOverHandler(Canvas, OnDragOver);
        DragDrop.AddDropHandler(Canvas, OnDrop);

        XamlEditor.TextChanged += OnXamlTextChanged;
        XamlEditor.LostFocus += OnXamlLostFocus;
        _xamlIdle.Tick += async (_, _) => await ApplyXamlAsync();

        AttachedToVisualTree += (_, _) =>
        {
            ApplyXamlColors();
            ShowDocument();
        };
        ActualThemeVariantChanged += (_, _) => ApplyXamlColors();
    }

    private static DesignerState State => DesignerState.Instance;

    /// <summary>Кладёт содержимое документа на канву.</summary>
    public void ShowDocument()
    {
        Form.Content = _document.Surface;
        Form.Width = double.IsNaN(_document.Surface.Width) ? 1280 : _document.Surface.Width;
        Form.Height = double.IsNaN(_document.Surface.Height) ? 800 : _document.Surface.Height;

        EmptyHint.IsVisible = _document.IsEmpty;
        FormSize.Text = $"{Form.Width:0} × {Form.Height:0}";

        ShowXamlIfVisible();
    }

    /// <summary>Показывает выделение узла на канве.</summary>
    /// <param name="node">Узел, чей контрол выделяется.</param>
    public void ShowSelection(HierarchyNode? node)
    {
        if (node?.Control is { } control)
            Canvas.SelectDesignTarget(control);
    }

    /// <summary>Перечитывает текст, если вкладка XAML открыта.</summary>
    public void ShowXamlIfVisible()
    {
        if (XamlHost.IsVisible)
            ShowXaml();
    }

    private void OnViewChanged(object? sender, SelectionChangedEventArgs e) => ApplyView();

    private void ApplyView()
    {
        var mode = ViewSwitch.SelectedIndex;
        var design = mode != 1;
        var xaml = mode != 0;

        EditorSplit.ColumnDefinitions[0].Width = new GridLength(design ? 1 : 0, design ? GridUnitType.Star : GridUnitType.Pixel);
        EditorSplit.ColumnDefinitions[1].Width = new GridLength(design && xaml ? 4 : 0, GridUnitType.Pixel);
        EditorSplit.ColumnDefinitions[2].Width = new GridLength(xaml ? 1 : 0, xaml ? GridUnitType.Star : GridUnitType.Pixel);

        XamlSplitter.IsVisible = design && xaml;
        XamlHost.IsVisible = xaml;

        if (xaml)
            ShowXaml();
    }

    private void OnZoomReset(object? sender, RoutedEventArgs e) => Canvas.ViewportZoom = 1;

    private void OnCanvasSelectionChanged(object? sender, DesignSelectionChangedEventArgs e)
    {
        if (e.NewPrimary is { } target)
        {
            var path = _document.FindPath(target.Target);

            State.Select(path.Count > 0 ? path[^1] : null, this);
            State.Status($"{Localizer.Instance["status.selected"]}: {target.DisplayName}");
        }
        else
        {
            State.Select(null, this);
            State.Status(_document.FilePath);
        }
    }

    /// <summary>
    /// Жест на канве закончился: переносим новую геометрию в разметку.
    /// </summary>
    /// <remarks>
    /// Канва уже подвинула объекты — событие приходит после, а не вместо. Наше
    /// дело записать то же самое в документ, иначе перетаскивание жило бы до
    /// первой пересборки.
    /// </remarks>
    private async void OnEditCompleted(object? sender, DesignEditCompletedEventArgs e)
    {
        foreach (var change in e.Changes.OfType<DesignGeometryChange>())
        {
            if (_document.FindNode(change.Target) is not { } node)
                continue;

            var values = GeometryWriter.Describe(node, change.OldBounds, change.NewBounds);

            if (values.Count == 0)
                continue;

            var error = await _document.SetAttributesAsync(node, values, $"{node.DisplayName}: {e.Kind}");

            State.Status(error ?? $"{node.DisplayName}: {string.Join(", ", values.Select(value => $"{value.Name}={value.Text}"))}");
        }
    }

    private async void OnDeleteRequested(object? sender, DesignEditorDeleteRequestedEventArgs e)
    {
        // Флаг читают сразу после возврата из обработчика, то есть до первого
        // ожидания: поставить его позже — значит не поставить вовсе.
        e.Handled = true;

        foreach (var target in e.Targets)
        {
            if (_document.FindNode(target.Target) is not { } node)
                continue;

            var error = await _document.RemoveAsync(node);

            State.Status(error ?? $"{Localizer.Instance["structure.deleted"]}: {node.DisplayName}");
        }
    }

    private async void OnReorderRequested(object? sender, DesignEditorReorderRequestedEventArgs e)
    {
        if (_document.FindNode(e.Target) is not { } node)
            return;

        e.Handled = true;

        var error = await _document.MoveAsync(node, e.NewIndex);

        State.Status(error ?? $"{Localizer.Instance["structure.moved"]}: {node.DisplayName}");
    }

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DesignerState.ToolboxFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(DesignerState.ToolboxFormat) is not { } item)
            return;

        var point = e.GetPosition(Form);
        var target = FindDropTarget(point);

        if (target is null)
        {
            State.Status(Localizer.Instance["toolbox.nowhere"]);
            return;
        }

        // На Canvas контрол ложится туда, где его отпустили; в остальных
        // раскладках место ему отводит родитель, и координаты только мешали бы.
        var placement = "";

        if (target.Control is Canvas canvas && PointIn(canvas, point) is { } local)
            placement = $"Canvas.Left=\"{Math.Round(local.X)}\" Canvas.Top=\"{Math.Round(local.Y)}\"";

        await InsertAsync(item, target, placement);
    }

    /// <summary>Вставляет контрол палитры в элемент документа.</summary>
    /// <param name="item">Контрол палитры.</param>
    /// <param name="parent">Куда вставлять.</param>
    /// <param name="placement">Атрибуты положения; пусто, если родитель кладёт сам.</param>
    public async Task InsertAsync(ToolboxItem item, HierarchyNode? parent, string placement)
    {
        if (parent is null)
            return;

        if (!CanHold(parent))
        {
            State.Status(Localizer.Instance["toolbox.nowhere"]);
            return;
        }

        if (ToolboxCatalog.Markup(item, parent.Element, placement) is not { } markup)
        {
            State.Status($"{Localizer.Instance["toolbox.nonamespace"]}: {item.TypeName}");
            return;
        }

        var path = parent.Path;
        var error = await _document.InsertAsync(parent, -1, markup, $"Вставить {item.TypeName}");

        State.Status(error
            ?? $"{Localizer.Instance["toolbox.inserted"]}: {item.TypeName} → {parent.DisplayName}");

        // Вставка перестраивает дерево, поэтому прежние узлы больше не те:
        // родителя приходится искать заново, зато вставленный контрол сразу
        // виден и выделен — иначе после вставки не за что взяться.
        if (error is null && _document.FindByPath(path)?.Children.LastOrDefault() is { } inserted)
        {
            State.Select(inserted, origin: null);
            ShowSelection(inserted);
        }
    }

    /// <summary>
    /// Находит элемент документа под точкой канвы.
    /// </summary>
    /// <remarks>
    /// Обычная проверка попадания тут не работает: в режиме показа готовой формы
    /// её содержимое в ней не участвует — иначе документ ловил бы щелчки вместо
    /// редактора. Поэтому попадание считается по прямоугольникам, как это делает
    /// и сама канва, и побеждает самый глубокий элемент, который вообще может
    /// кого-то в себе держать.
    /// </remarks>
    private HierarchyNode? FindDropTarget(Point point)
    {
        HierarchyNode? found = null;

        Walk(_document.Nodes);
        return found;

        void Walk(IEnumerable<HierarchyNode> nodes)
        {
            foreach (var node in nodes)
            {
                // Корень документа — окно, а окно частью чужого дерева не
                // бывает: на канве лежит его содержимое. Точку к нему не
                // пересчитать, но идти вглубь это не мешает.
                if (node.Control is { IsVisible: true } control && PointIn(control, point) is { } local)
                {
                    if (!new Rect(control.Bounds.Size).Contains(local))
                        continue;

                    if (CanHold(node))
                        found = node;
                }

                Walk(node.Children);
            }
        }
    }

    /// <summary>
    /// Пересчитывает точку формы в координаты контрола документа.
    /// </summary>
    /// <remarks>
    /// Готовый пересчёт координат тут неприменим: содержимое документа
    /// принадлежит своему окну, а не нашему, и <c>TranslatePoint</c> через эту
    /// границу возвращает пустоту. Зато внутри самого документа прямоугольник
    /// каждого контрола задан относительно его визуального родителя, и цепочку
    /// до поверхности можно сложить самим.
    /// </remarks>
    private Point? PointIn(Control control, Point point)
    {
        var offset = new Point();

        for (Visual? visual = control; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, _document.Surface))
                return point - offset;

            offset += visual.Bounds.Position;
        }

        return null;
    }

    /// <summary>
    /// Может ли элемент принять в себя ещё один контрол.
    /// </summary>
    /// <remarks>
    /// Панель держит сколько угодно детей, а рамка и контрол с содержимым —
    /// ровно одного, и то если он ещё не занят: ни вложенным элементом, ни
    /// значением, записанным атрибутом.
    /// </remarks>
    private static bool CanHold(HierarchyNode node) => node.Control switch
    {
        Panel => true,
        Decorator => IsFree(node, "Child"),
        ContentControl => IsFree(node, "Content"),
        _ => false,
    };

    private static bool IsFree(HierarchyNode node, string member) =>
        !node.Element.ContentElements.Any() &&
        node.Element.GetAttribute(member) is null;

    /// <summary>Перечитывает текст редактора из документа.</summary>
    /// <remarks>
    /// Позиция каретки восстанавливается: правка из инспектора не должна
    /// уводить взгляд с того места, где человек читал разметку.
    /// </remarks>
    private void ShowXaml()
    {
        var text = _document.Text;

        if (string.Equals(XamlEditor.Text, text, StringComparison.Ordinal))
            return;

        var caret = XamlEditor.CaretOffset;

        _fillingEditor = true;
        try
        {
            XamlEditor.Text = text;
            XamlEditor.CaretOffset = Math.Min(caret, text.Length);
        }
        finally
        {
            _fillingEditor = false;
        }
    }

    private void OnXamlTextChanged(object? sender, EventArgs e)
    {
        if (_fillingEditor)
            return;

        _xamlIdle.Stop();
        _xamlIdle.Start();
    }

    private async void OnXamlLostFocus(object? sender, RoutedEventArgs e) => await ApplyXamlAsync();

    private async Task ApplyXamlAsync()
    {
        _xamlIdle.Stop();

        if (_fillingEditor)
            return;

        var error = await _document.SetTextAsync(XamlEditor.Text, "Правка разметки");

        State.Status(error ?? Localizer.Instance["xaml.applied"]);
    }

    /// <summary>
    /// Красит разметку в цвета темы.
    /// </summary>
    /// <remarks>
    /// Готовая подсветка XML приходит со своими цветами, рассчитанными на белый
    /// фон, и рядом с тёмной темой студии выглядит чужой. Правила разбора при
    /// этом верные, поэтому меняются только цвета — и заново при каждой смене
    /// темы.
    /// </remarks>
    private void ApplyXamlColors()
    {
        if (HighlightingManager.Instance.GetDefinition("XML") is not { } definition)
            return;

        Paint(definition, "Comment", "AxFg3Brush");
        Paint(definition, "XmlTag", "AxAccBrush");
        Paint(definition, "AttributeName", "AxPurBrush");
        Paint(definition, "AttributeValue", "AxGrnBrush");
        Paint(definition, "XmlDeclaration", "AxFg3Brush");
        Paint(definition, "DocType", "AxFg3Brush");
        Paint(definition, "CData", "AxYelBrush");
        Paint(definition, "Entity", "AxYelBrush");

        XamlEditor.SyntaxHighlighting = definition;

        void Paint(IHighlightingDefinition target, string name, string resource)
        {
            if (target.GetNamedColor(name) is { } color &&
                this.TryFindResource(resource, ActualThemeVariant, out var value) &&
                value is ISolidColorBrush brush)
            {
                color.Foreground = new SimpleHighlightingBrush(brush.Color);
            }
        }
    }
}
