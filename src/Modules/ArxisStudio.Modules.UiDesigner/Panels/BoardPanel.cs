using System.Globalization;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.Sdk;
using ArxisStudio.Surface;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Panels;

/// <summary>
/// Доска форм решения: карточки <c>.axaml</c> на холсте Surface.
/// </summary>
/// <remarks>
/// Холст — ядро Surface как есть: панорама средней кнопкой, зум колесом к курсору, рамка выбора левой
/// кнопкой по пустому месту, Shift — добавить к выбору, тяга с прилипанием к сетке, стрелки — сдвиг на
/// шаг, Shift со стрелкой — на крупный, Ctrl+Z и Ctrl+Y — отмена и повтор (<see cref="SurfaceHistory"/>).
/// Удаление и изменение размера у ядра доска снимает: файлы она не удаляет, а размер карточке задаёт тема.
/// <para>
/// Своё у доски — то, что знает она одна. Enter и двойной щелчок открывают форму в редакторе документов
/// студии. F показывает выбранное целиком, а без выбора — всю доску, как F в Unity и Unreal. Кадр не
/// крупнее ста процентов: одна карточка во весь холст ничего не показывает. «Упорядочить» раскладывает
/// доску сеткой в порядке решения и ложится в ту же историю, что тяга.
/// </para>
/// <para>
/// Места пишутся после каждой единицы правки — история сообщает о ней и о каждой отмене, — так что файл
/// доски всегда совпадает с тем, что на экране.
/// </para>
/// </remarks>
[ToolWindow(UiDesignerModule.PanelId)]
public sealed class BoardPanel : ToolWindow
{
    /// <summary>Наибольший масштаб кадра.</summary>
    private const double FrameZoom = 1;

    private BoardView? _view;
    private BoardModel? _model;
    private BoardMenu? _menu;
    private SurfaceHistory? _history;

    /// <summary>Модель доски — тестам, чтобы ждать постройку, а не время.</summary>
    internal BoardModel? Model => _model;

    /// <summary>Разметка доски — тестам.</summary>
    internal BoardView? View => _view;

    /// <summary>История правок холста — тестам.</summary>
    internal SurfaceHistory? History => _history;

    /// <summary>Меню — тестам: попап — отдельное окно, которого у безголового прогона нет.</summary>
    internal BoardMenu? Menu => _menu;

    /// <inheritdoc/>
    /// <remarks>Клавиатура доски — у холста: им работают, и его клавиши — стрелки, F и Enter.</remarks>
    public override Control? FocusTarget => _view?.Sheet;

    /// <inheritdoc/>
    protected override Control Build()
    {
        var view = new BoardView();
        var sheet = view.Sheet;

        _view = view;
        _model = new BoardModel(Context, Pitch);
        _menu = new BoardMenu(Context.Strings, new BoardActions(Open, Frame, Arrange));
        _history = new SurfaceHistory(sheet);

        view.DataContext = _model;
        sheet.EstimatedItemSize = new Size(Length("AxFormCardWidth"), Length("AxFormCardMinHeight"));
        sheet.ShowGrid = ShowsGrid();
        view.GridToggle.IsChecked = sheet.ShowGrid;

        Keys(sheet);
        Wire(view, sheet, _model, _history);
        BoardLooks.Apply(sheet);
        Zoomed();

        return view;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        if (_view is { } view)
        {
            var sheet = view.Sheet;

            sheet.DoubleTapped -= OnDoubleTapped;
            sheet.ContextMenuRequesting -= OnContextMenuRequesting;
            sheet.PropertyChanged -= OnSheetPropertyChanged;
            sheet.ActualThemeVariantChanged -= OnThemeChanged;
            view.Fit.Click -= OnFit;
            view.Actual.Click -= OnActual;
            view.ArrangeAll.Click -= OnArrange;
            view.GridToggle.IsCheckedChanged -= OnGridToggled;
        }

        if (_model is not null)
            _model.Replaced -= OnReplaced;

        Context.Settings.Changed -= OnSettingsChanged;

        _history?.Dispose();
        _history = null;
        _model?.Dispose();
        _model = null;
        _menu = null;
        _view = null;
    }

    /// <summary>Выбранные карточки в порядке выбора.</summary>
    internal IReadOnlyList<FormCard> Selected() =>
        _view?.Sheet.SelectedItems?.OfType<FormCard>().ToList() ?? [];

    /// <summary>
    /// Показывает карточки целиком; пусто — всю доску.
    /// </summary>
    /// <param name="cards">Что показать.</param>
    internal void Frame(IReadOnlyList<FormCard> cards)
    {
        if (_view?.Sheet is not { } sheet || _model is not { } model || model.Cards.Count == 0)
            return;

        var shown = cards.Count > 0 ? cards : model.Cards;
        var bounds = shown.Select(card => BoundsOf(sheet, card)).Aggregate((all, next) => all.Union(next));

        sheet.FitToView(bounds);

        if (sheet.ViewportZoom > FrameZoom)
        {
            sheet.ZoomAt(FrameZoom, Middle(sheet));
            sheet.CenterOn(bounds);
        }
    }

    private void Open(IReadOnlyList<FormCard> cards)
    {
        if (_model is { } model && cards.Count > 0)
            _ = model.OpenAsync(cards);
    }

    private void Arrange()
    {
        if (_model?.Arrange() is not { } change || _history is not { } history)
            return;

        history.Push(change);
        Frame([]);
    }

    /// <summary>
    /// Клавиши холста: свои у доски, и минус те, что ей не к месту.
    /// </summary>
    private void Keys(SurfaceView sheet)
    {
        sheet.KeyCommands.Remove(SurfaceKeyCommands.Delete);
        sheet.KeyCommands.Remove(SurfaceKeyCommands.Resize);

        sheet.KeyCommands.Add(new SurfaceKeyCommand("ui-designer.open", BoardMenu.OpenKey, _ =>
        {
            var cards = Selected();

            Open(cards);

            return cards.Count > 0;
        }));

        sheet.KeyCommands.Add(new SurfaceKeyCommand("ui-designer.frame", BoardMenu.FrameKey, _ =>
        {
            Frame(Selected());

            return true;
        }));
    }

    private void Wire(BoardView view, SurfaceView sheet, BoardModel model, SurfaceHistory history)
    {
        sheet.DoubleTapped += OnDoubleTapped;
        sheet.ContextMenuRequesting += OnContextMenuRequesting;
        sheet.PropertyChanged += OnSheetPropertyChanged;
        sheet.ActualThemeVariantChanged += OnThemeChanged;
        view.Fit.Click += OnFit;
        view.Actual.Click += OnActual;
        view.ArrangeAll.Click += OnArrange;
        view.GridToggle.IsCheckedChanged += OnGridToggled;
        model.Replaced += OnReplaced;
        history.Changed += (_, _) => _model?.Moved();
        Context.Settings.Changed += OnSettingsChanged;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (CardAt(e.Source) is { } card)
        {
            Open([card]);
            e.Handled = true;
        }
    }

    private void OnContextMenuRequesting(object? sender, SurfaceContextRequestingEventArgs e)
    {
        if (_view?.Sheet is not { } sheet || _menu is not { } menu)
            return;

        var request = e.Request;
        IReadOnlyList<FormCard> cards = request.Scope switch
        {
            SurfaceContextScope.Selection => Selected(),
            SurfaceContextScope.Container or SurfaceContextScope.NestedTarget
                when request.Target?.Container.DataContext is FormCard card => [card],
            _ => [],
        };

        e.Handled = true;
        menu.Show(sheet, cards, atPointer: request.Source == SurfaceContextSource.Pointer);
    }

    private void OnSheetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SurfaceView.ViewportZoomProperty)
            Zoomed();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_view?.Sheet is { } sheet)
            BoardLooks.Apply(sheet);
    }

    private void OnFit(object? sender, RoutedEventArgs e)
    {
        Frame([]);
        Back();
    }

    private void OnActual(object? sender, RoutedEventArgs e)
    {
        if (_view?.Sheet is { } sheet)
            sheet.ZoomAt(FrameZoom, Middle(sheet));

        Back();
    }

    private void OnArrange(object? sender, RoutedEventArgs e)
    {
        Arrange();
        Back();
    }

    /// <summary>
    /// Кнопка полосы отдаёт клавиатуру холсту: раскладку отменяют сразу <c>Ctrl+Z</c>, а с кареткой на
    /// кнопке сочетание до истории холста не дошло бы. Полоса Unity держит так же.
    /// </summary>
    private void Back() => _view?.Sheet.Focus();

    private void OnGridToggled(object? sender, RoutedEventArgs e)
    {
        if (_view?.GridToggle.IsChecked is { } shown && shown != ShowsGrid())
            Context.Settings.Set(UiDesignerModule.GridKey, shown);
    }

    private void OnSettingsChanged(object? sender, string key)
    {
        if (key != UiDesignerModule.GridKey || _view is not { } view)
            return;

        // Настройка меняется и из окна настроек — не в потоке интерфейса, если её записал не он.
        Dispatcher.UIThread.Post(() =>
        {
            if (_view != view)
                return;

            view.Sheet.ShowGrid = ShowsGrid();
            view.GridToggle.IsChecked = view.Sheet.ShowGrid;
        });
    }

    /// <summary>
    /// Доска сменила решение: отмена прежнего ничего не значит, а новое надо показать целиком.
    /// </summary>
    private void OnReplaced(object? sender, EventArgs e)
    {
        _history?.Clear();

        // Кадр — после раскладки: размер холста и карточек известен только ей.
        Dispatcher.UIThread.Post(() => Frame([]), DispatcherPriority.Loaded);
    }

    private void Zoomed()
    {
        if (_view is { } view)
        {
            view.Actual.Content = string.Format(
                CultureInfo.CurrentCulture, Context.Strings["board.zoom"], Math.Round(view.Sheet.ViewportZoom * 100));
        }
    }

    private bool ShowsGrid() => Context.Settings.Get<bool?>(UiDesignerModule.GridKey) ?? true;

    /// <summary>
    /// Шаг раскладки: карточка с зазором. Высота — по самой высокой видимой карточке: крупный текст
    /// растит их все одинаково, и ряд, отмеренный по наименьшей высоте, налез бы на следующий.
    /// </summary>
    private Pitch Pitch()
    {
        var gap = Length("AxFormCardGap");
        var height = Length("AxFormCardMinHeight");

        if (_view?.Sheet is { } sheet)
        {
            foreach (var container in sheet.GetRealizedContainers())
                height = Math.Max(height, container.Bounds.Height);
        }

        return new Pitch(Length("AxFormCardWidth") + gap, height + gap);
    }

    /// <summary>Длина из темы; ключ, которого нет, проверяют тесты ключей, а не доска.</summary>
    private double Length(string key) =>
        _view is { } view && view.TryFindResource(key, view.ActualThemeVariant, out var value) && value is double length
            ? length
            : 0;

    /// <summary>Место карточки на холсте: размер — у контейнера, если он есть, иначе у темы.</summary>
    private Rect BoundsOf(SurfaceView sheet, FormCard card)
    {
        var size = sheet.ContainerFromItem(card) is { Bounds.Size: { Width: > 0 } measured }
            ? measured
            : new Size(Length("AxFormCardWidth"), Length("AxFormCardMinHeight"));

        return new Rect(card.Location, size);
    }

    private static Point Middle(SurfaceView sheet) => new(sheet.Bounds.Width / 2, sheet.Bounds.Height / 2);

    private static FormCard? CardAt(object? source) =>
        (source as Visual)?.FindAncestorOfType<SurfaceItem>(includeSelf: true)?.DataContext as FormCard;
}
