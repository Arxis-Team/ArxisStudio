using System.Globalization;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Panels;

/// <summary>
/// Доска форм решения: все формы на одном холсте дизайнера — живые, и правят их здесь так же, как во
/// вкладке.
/// </summary>
/// <remarks>
/// <para>
/// <b>Правят как во вкладке.</b> Выбор, жесты, правки строения, меню, Esc к родителю — у холста форм
/// (<see cref="FormCanvas"/>), того же, что у вкладки; доска ставит на него все формы решения, каждую в
/// её размер и с именем над ней (<see cref="BoardForms"/>), — живыми те, что на виду (<see cref="BoardSight"/>),
/// и снимком прочие и те, что ещё встают.
/// </para>
/// <para>
/// <b>Своё у доски</b> — то, что знает она одна: где стоят формы и какие убраны. Форму берут целиком за
/// имя над ней и тянут по холсту, как фрейм в Figma, — её место уходит в файл доски; Delete убирает её с
/// доски — файл формы остаётся, — и вернуть её можно отменой и из меню холста. Enter и двойной щелчок по
/// снимку открывают форму в редакторе документов студии. F показывает выбранное целиком, а без выбора —
/// всю доску. «Упорядочить» расставляет формы рядами в порядке решения.
/// </para>
/// <para>
/// <b>Форма, открытая откуда угодно, — на доске</b>, когда дизайнер в режиме доски: редактор документов
/// показывает её здесь (<see cref="RevealAsync"/>), выбранной целиком и в кадре, как Unity — сцену, а не
/// открывает вкладкой.
/// </para>
/// <para>
/// <b>История одна.</b> Ctrl+Z на доске отменяет последнее, что на ней сделано, — место формы или правку
/// её текста (<see cref="BoardHistory"/>).
/// </para>
/// <para>
/// <b>XAML — формы, с которой работают</b>: где выбрано, а без выбора — где выбирали последней. Под холстом,
/// как у вкладки, только просмотр, с отметкой выбранного; каретка в нём выбирает на холсте. Вид — дизайн,
/// XAML или оба — тот же, что у вкладок (<see cref="FormViewModes"/>).
/// </para>
/// <para>
/// Режим дизайнера выбирают здесь же, парой переключателей в полосе: все формы на одной доске или каждая
/// в своей вкладке. Режим — настройка, а не состояние доски: он решает, кто открывает форму. Форма,
/// открытая вкладкой, на доске стоит снимком: показ у документа один, и вкладка его забирает.
/// </para>
/// <para>
/// <b>Тяга из окна проекта.</b> Доска — цель перетаскивания студии (<see cref="StudioDragDrop"/>): на неё
/// несут файлы форм, как ассеты на сцену в Unity. Пока несут, в точке курсора стоит заготовка формы её
/// размера, а у курсора сказано, что будет: вернуть убранную, передвинуть стоящую или почему нельзя.
/// Отпущенные встают выбранными, клавиатура переходит к холсту, и Ctrl+Z отменяет постановку целиком.
/// Дорога без мыши — меню пустого холста «Вернуть на доску» (WCAG 2.5.7).
/// </para>
/// </remarks>
[ToolWindow(UiDesignerModule.PanelId)]
public sealed partial class BoardPanel : ToolWindow, IFormCanvasHost
{
    private BoardView? _view;
    private BoardModel? _model;
    private BoardMenu? _menu;
    private BoardHistory? _history;
    private SheetControls? _controls;
    private BoardPreviews? _snapshots;
    private FormCanvas? _canvas;
    private FormViewModes? _modes;
    private BoardSight? _sight;
    private BoardForms? _forms;
    private IStudioXamlDesign? _design;

    /// <summary>Модель доски — тестам, чтобы ждать постройку, а не время.</summary>
    internal BoardModel? Model => _model;

    /// <summary>Разметка доски — тестам.</summary>
    internal BoardView? View => _view;

    /// <summary>История доски — тестам.</summary>
    internal BoardHistory? History => _history;

    /// <summary>Меню — тестам: попап — отдельное окно, которого у безголового прогона нет.</summary>
    internal BoardMenu? Menu => _menu;

    /// <summary>Снимки форм — тестам: дождаться загрузки, а не спать.</summary>
    internal BoardPreviews? Snapshots => _snapshots;

    /// <summary>Холст форм — тестам: выбор, правки строения и меню.</summary>
    internal FormCanvas? Canvas => _canvas;

    /// <summary>Живые формы — тестам: дождаться, пока встанут.</summary>
    internal BoardForms? Forms => _forms;

    /// <summary>Какие формы на виду — тестам: пересчитать сразу, а не ждать прохода диспетчера.</summary>
    internal BoardSight? Sight => _sight;

    /// <summary>Вид доски — тестам.</summary>
    internal FormViewModes? Modes => _modes;

    /// <inheritdoc/>
    /// <remarks>
    /// Клавиатура доски — у холста: им работают, и его клавиши — стрелки, F, Enter и Delete; в виде одного
    /// XAML холста не видно, и каретку берёт текст.
    /// </remarks>
    public override Control? FocusTarget => _modes?.Caret ?? _view?.Sheet;

    /// <inheritdoc/>
    protected override Control Build()
    {
        var view = new BoardView();
        var sheet = view.Sheet;
        var options = Context.GetService<UiDesignerOptions>() ?? UiDesignerOptions.Default;

        _view = view;
        _design = Context.XamlDesign();
        _model = new BoardModel(Context, Metrics);
        _history = new BoardHistory();
        _menu = new BoardMenu(
            Context.Strings,
            new BoardActions(Open, Frame, Arrange, Remove, Return, () => _model?.Removed ?? [], Where));
        _controls = new SheetControls(Context, sheet, view.Fit, view.Actual, view.GridToggle, Everything);
        _canvas = new FormCanvas(Context, sheet, view.Code, this, options);
        _modes = new FormViewModes(Context, view.Mode, view.Body, view.Stage, view.Split, view.CodePane, sheet, view.Code);
        _modes.Changed += OnModeApplied;
        _sight = new BoardSight(sheet, PlaceOf);
        _forms = new BoardForms(Context, sheet, _sight, _canvas, _history, options);

        view.DataContext = _model;

        if (Context.GetService<IStudioFilePreviews>() is { } previews)
            _snapshots = new BoardPreviews(_sight, previews);

        Keys(sheet);
        Wire(view, sheet, _model, _history);
        Moded();
        ShowState();
        Boards.Of(Context).Built(this);

        return view;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        Boards.Of(Context).Released(this);

        if (_view is { } view)
        {
            view.Sheet.RemoveHandler(InputElement.PointerPressedEvent, OnSheetPressed);
            view.Sheet.EditCompleted -= OnEditCompleted;
            view.ReturnAll.Click -= OnReturnAll;
            view.ArrangeAll.Click -= OnArrange;
            view.BoardMode.Click -= OnBoardMode;
            view.TabsMode.Click -= OnTabsMode;
            StudioDragDrop.RemoveDragOverHandler(view.Stage, OnDragOver);
            StudioDragDrop.RemoveDragLeaveHandler(view.Stage, OnDragLeave);
            StudioDragDrop.RemoveDropHandler(view.Stage, OnDrop);
            Unland(view);
            Unwire(view);
        }

        if (_model is not null)
        {
            _model.Replaced -= OnReplaced;
            _model.PresenceChanged -= OnPresence;
        }

        if (_history is not null)
            _history.Changed -= OnHistoryChanged;

        Context.Settings.Changed -= OnSettingsChanged;

        if (_modes is not null)
        {
            _modes.Changed -= OnModeApplied;
            _modes.Dispose();
            _modes = null;
        }

        // Живые формы — раньше снимков и холста: они отдают корни карточкам, пока карточки и холст живы, и
        // сохраняют несохранённое, прежде чем отпустить документы.
        _forms?.Dispose();
        _forms = null;
        _snapshots?.Dispose();
        _snapshots = null;
        _sight?.Dispose();
        _sight = null;
        _canvas?.Dispose();
        _canvas = null;
        _controls?.Dispose();
        _controls = null;
        _model?.Dispose();
        _model = null;
        _history = null;
        _menu = null;
        _view = null;
    }

    /// <summary>
    /// Показывает форму на доске: убранную возвращает, выбирает целиком и ставит в кадр.
    /// </summary>
    /// <param name="path">Файл формы.</param>
    /// <returns>Форма показана; false — такой формы на доске нет и вернуть её нечем.</returns>
    /// <remarks>
    /// Доска могла только что построиться — её показали ради этой формы, — и формы на ней встают после
    /// чтения решения: показ ждёт его. Возврат убранной — запись истории, как из меню: Ctrl+Z уберёт её снова.
    /// </remarks>
    internal async Task<bool> RevealAsync(CanonicalPath path)
    {
        if (_model is not { } model)
            return false;

        await model.Settled;

        if (_model != model || _view is not { } view)
            return false;

        if (!model.Cards.Any(card => card.Path == path) && model.Removed.Any(form => form.File.Path == path))
            Return([path]);

        if (model.Cards.FirstOrDefault(card => card.Path == path) is not { } card)
            return false;

        // Вернувшейся форме контейнер даёт раскладка, и выбрать ядро может только разложенный — с рамкой:
        // неразложенный оно не выберет, а пустой выбор холст форм потом и перенесёт на доску.
        view.Sheet.UpdateLayout();

        if (view.Sheet.ContainerFromItem(card) is not UiDesignerFormItem item)
            return false;

        // Посреди жеста ядро выбор не отдаст — форма всё равно показана: она на доске и в кадре.
        view.Sheet.SelectTarget(item);
        Frame([card]);

        return true;
    }

    /// <summary>Формы, выбранные на холсте, — и целиком, и те, внутри которых выбрано, — в порядке выбора.</summary>
    internal IReadOnlyList<FormCard> Selected() =>
        _view?.Sheet.SelectedItems?.OfType<FormCard>().ToList() ?? [];

    /// <summary>
    /// Формы, выбранные целиком: контейнер выбран сам, а не элемент в нём, — или выбранная форма за краем
    /// окна, без контейнера.
    /// </summary>
    internal IReadOnlyList<FormCard> Whole()
    {
        if (_view?.Sheet is not { } sheet)
            return [];

        return
        [
            .. Selected().Where(card =>
                sheet.ContainerFromItem(card) is not { } container
                || sheet.SelectedTargets.Any(target => ReferenceEquals(target.Target, container))),
        ];
    }

    /// <summary>
    /// Показывает формы целиком — с заголовками окон; пусто — всю доску.
    /// </summary>
    /// <param name="cards">Что показать.</param>
    internal void Frame(IReadOnlyList<FormCard> cards)
    {
        if (cards.Count == 0)
            _controls?.FrameAll();
        else
            _controls?.Frame(Union(cards));
    }

    /// <inheritdoc/>
    void IFormCanvasHost.Step(bool back)
    {
        if (back)
            _history?.Undo();
        else
            _history?.Redo();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Убирается то, что выбрано целиком; форма, в которой выбран элемент, остаётся — Delete удалил элемент.
    /// Выбранное без контейнера — за краем окна — берётся данными: в просьбе ядра его контейнера нет.
    /// </remarks>
    void IFormCanvasHost.Remove(SurfaceDeleteRequestedEventArgs request)
    {
        if (_view?.Sheet is not { } sheet)
            return;

        var cards = request.Items
            .OfType<FormCard>()
            .Where(card => sheet.ContainerFromItem(card) is not { } container
                           || request.Targets.Any(target => ReferenceEquals(target.Target, container)))
            .ToList();

        if (cards.Count > 0)
            Remove(cards);
    }

    /// <inheritdoc/>
    void IFormCanvasHost.FrameAll() => Frame([]);

    /// <inheritdoc/>
    void IFormCanvasHost.LeftWindow() => _forms?.SaveAll();

    /// <inheritdoc/>
    /// <remarks>
    /// Над пустым холстом — меню доски: вписать, упорядочить, вернуть убранное. Над живой формой — правки её
    /// строения и, за чертой, то, что доска делает с формой целиком. Над снимком и над несколькими формами —
    /// только это.
    /// </remarks>
    IReadOnlyList<Control> IFormCanvasHost.MenuItems(IReadOnlyList<Control> form, SurfaceContextRequest? request)
    {
        if (_menu is not { } menu)
            return form;

        var cards = CardsFor(request);

        if (cards.Count == 0)
            return menu.Items([]);

        if (form.Count == 0 || cards.Count > 1 || _forms?.IsLive(cards[0]) != true || _canvas?.Active?.Session.Path != cards[0].Path)
            return menu.Items(cards);

        return [.. form, new ArxisStudio.Controls.AxSeparator(), .. menu.Items(cards)];
    }

    /// <summary>О каких формах меню: под указателем, выбранные или ни о каких — над пустым холстом.</summary>
    private IReadOnlyList<FormCard> CardsFor(SurfaceContextRequest? request) => request?.Scope switch
    {
        null or SurfaceContextScope.Selection => Selected(),
        SurfaceContextScope.Container or SurfaceContextScope.NestedTarget
            when request.Target?.Container.DataContext is FormCard card => [card],
        _ => [],
    };

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
    /// Убирает формы с доски одной записью истории.
    /// </summary>
    /// <remarks>
    /// Без вопроса «вы уверены?»: файл на месте, а уборка отменяется, — вопрос перед обратимым действием
    /// только учит отвечать «да» не читая. Сделанное подтверждает строка состояния (<see cref="OnPresence"/>).
    /// </remarks>
    private void Remove(IReadOnlyList<FormCard> cards)
    {
        if (_model?.Remove(cards) is not { } change || _history is not { } history)
            return;

        history.Push(change);
        _controls?.Back();
    }

    /// <summary>
    /// Строка состояния о перемене состава: что ушло — и как вернуть, что вернулось.
    /// </summary>
    /// <remarks>
    /// Говорит о каждой перемене, откуда бы она ни пришла, — иначе после отмены уборки или «Вернуть все»
    /// строка так и звала бы вернуть то, что уже на доске.
    /// </remarks>
    private void OnPresence(object? sender, PresenceEventArgs e)
    {
        var (one, many) = e.Removed
            ? ("board.removed.one", "board.removed.many")
            : ("board.returned.one", "board.returned.many");

        var said = e.Cards.Count == 1
            ? string.Format(CultureInfo.CurrentCulture, Context.Strings[one], e.Cards[0].Name)
            : string.Format(CultureInfo.CurrentCulture, Context.Strings[many], e.Cards.Count);

        Context.GetService<IStudioStatus>()?.Show(said);
    }

    private string Where(FoundForm form) => _model?.Where(form) ?? form.File.Project;

    /// <summary>Возвращает убранные формы одной записью истории.</summary>
    private void Return(IReadOnlyList<CanonicalPath> paths)
    {
        if (_model?.Return(paths) is not { } change || _history is not { } history)
            return;

        history.Push(change);
        _controls?.Back();
    }

    /// <summary>
    /// Клавиши доски поверх клавиш холста форм: Enter открывает формы, выбранные целиком, F показывает
    /// выбранное.
    /// </summary>
    private void Keys(SurfaceView sheet)
    {
        sheet.KeyCommands.Add(new SurfaceKeyCommand("ui-designer.open", BoardMenu.OpenKey, _ =>
        {
            var cards = Whole();

            Open(cards);

            return cards.Count > 0;
        }));

        sheet.KeyCommands.Add(new SurfaceKeyCommand(LiveFormDocument.FrameCommand, BoardMenu.FrameKey, _ =>
        {
            Frame(Selected());

            return true;
        }));
    }

    private void Wire(BoardView view, SurfaceView sheet, BoardModel model, BoardHistory history)
    {
        sheet.AddHandler(InputElement.PointerPressedEvent, OnSheetPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        sheet.EditCompleted += OnEditCompleted;
        view.ReturnAll.Click += OnReturnAll;
        view.ArrangeAll.Click += OnArrange;
        view.BoardMode.Click += OnBoardMode;
        view.TabsMode.Click += OnTabsMode;
        StudioDragDrop.AddDragOverHandler(view.Stage, OnDragOver);
        StudioDragDrop.AddDragLeaveHandler(view.Stage, OnDragLeave);
        StudioDragDrop.AddDropHandler(view.Stage, OnDrop);
        model.Replaced += OnReplaced;
        model.PresenceChanged += OnPresence;
        history.Changed += OnHistoryChanged;
        Context.Settings.Changed += OnSettingsChanged;
        WireState(view);
    }

    /// <summary>
    /// Холст снова виден: формы, сменившиеся под видом «XAML», снимаются теперь — спрятанные не снять.
    /// </summary>
    private void OnModeApplied(object? sender, EventArgs e)
    {
        if (_modes?.Mode != FormViewMode.Xaml)
            _canvas?.QueueSnapshots();
    }

    /// <summary>История сменилась — тягой, отменой, раскладкой: места, может быть, тоже — пора записать.</summary>
    private void OnHistoryChanged(object? sender, EventArgs e) => _model?.Moved();

    /// <summary>
    /// Двойной щелчок по снимку формы открывает её: живую правят тут же, а снимок стоит, пока форму держит
    /// вкладка, — ей и уходит открытие.
    /// </summary>
    /// <remarks>
    /// Щелчок ловится на пути вниз и по точке холста, а не событием двойного касания: первое нажатие
    /// выбирает форму, и второе приходится уже в рамку выбора над ней — касание платформа засчитывает
    /// только по тому же элементу.
    /// </remarks>
    private void OnSheetPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2
            || _view?.Sheet is not { } sheet
            || !e.GetCurrentPoint(sheet).Properties.IsLeftButtonPressed
            || CardAt(sheet, sheet.GetWorldPosition(e.GetPosition(sheet))) is not { } card
            || _forms?.IsLive(card) == true)
        {
            return;
        }

        Open([card]);
        e.Handled = true;
    }

    /// <summary>
    /// Жест холста сдвинул формы, выбранные целиком: их места — запись истории доски. Правки внутри форм
    /// пишет в их текст холст форм, а их шаги ложатся в историю через сессии.
    /// </summary>
    private void OnEditCompleted(object? sender, SurfaceEditCompletedEventArgs e)
    {
        if (_view?.Sheet is not { } sheet || _history is not { } history)
            return;

        var moves = new List<BoardMove>();

        foreach (var change in e.Changes.OfType<GeometryChange>())
        {
            if (change.Target is UiDesignerFormItem item
                && sheet.IndexFromContainer(item) >= 0
                && CardOf(sheet, item) is { } card
                && change.OldBounds.Position != change.NewBounds.Position)
            {
                moves.Add(new BoardMove(card, change.OldBounds.Position, change.NewBounds.Position));
            }
        }

        foreach (var change in e.ItemChanges)
        {
            if (change.Item is FormCard card && change.OldLocation != change.NewLocation)
                moves.Add(new BoardMove(card, change.OldLocation, change.NewLocation));
        }

        if (moves.Count > 0)
            history.Push(new BoardMoves(moves));
    }

    private void OnArrange(object? sender, RoutedEventArgs e)
    {
        Arrange();
        _controls?.Back();
    }

    private void OnReturnAll(object? sender, RoutedEventArgs e)
    {
        if (_model is { } model)
            Return([.. model.Removed.Select(form => form.File.Path)]);
    }

    private string Say(string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, Context.Strings[key], values);

    private void OnBoardMode(object? sender, RoutedEventArgs e) => Choose(tabs: false);

    private void OnTabsMode(object? sender, RoutedEventArgs e) => Choose(tabs: true);

    /// <summary>
    /// Выбирает режим дизайнера.
    /// </summary>
    /// <remarks>
    /// Переключатели — пара, а не два выключателя: щелчок по включённому его не гасит. Кнопка уже
    /// перевернула себя к этому времени, поэтому вид ставится из настройки, а не из кнопки.
    /// </remarks>
    private void Choose(bool tabs)
    {
        if (ShowsTabs() != tabs)
            Context.Settings.Set(UiDesignerModule.TabsKey, tabs);

        Moded();
        _controls?.Back();
    }

    private void OnSettingsChanged(object? sender, string key)
    {
        if (key != UiDesignerModule.TabsKey || _view is not { } view)
            return;

        // Настройка меняется и из окна настроек — не в потоке интерфейса, если её записал не он.
        Dispatcher.UIThread.Post(() =>
        {
            if (_view == view)
                Moded();
        });
    }

    /// <summary>Переключатели режима — по настройке.</summary>
    private void Moded()
    {
        if (_view is not { } view)
            return;

        var tabs = ShowsTabs();

        view.BoardMode.IsChecked = !tabs;
        view.TabsMode.IsChecked = tabs;
    }

    private bool ShowsTabs() => Context.Settings.Get<bool?>(UiDesignerModule.TabsKey) ?? false;

    /// <summary>
    /// Доска сменила решение: отмена прежнего ничего не значит, прежние формы отпускаются, а новое надо
    /// показать целиком.
    /// </summary>
    private void OnReplaced(object? sender, EventArgs e)
    {
        _history?.Clear();
        _forms?.Forget();

        // Кадр — после раскладки: размер холста и форм известен только ей.
        Dispatcher.UIThread.Post(() => Frame([]), DispatcherPriority.Loaded);
    }

    /// <summary>Чем мерить формы для раскладки: размер и заголовок окна — у стоящих на холсте, у прочих — по разметке.</summary>
    private BoardMetrics Metrics() => new(Length("AxFormBoardGap"), BoxOf);

    /// <summary>
    /// Форма для раскладки: размер — у её карточки, если она на холсте и измерена, иначе объявленный, а
    /// нет его — рамка темы; над окном — его заголовок.
    /// </summary>
    private Box BoxOf(FormCard card)
    {
        var item = _view?.Sheet.ContainerFromItem(card) as UiDesignerFormItem;
        var size = item is { Bounds.Size: { Width: > 0, Height: > 0 } measured }
            ? measured
            : new Size(card.Root.Width ?? Length("AxFormFrameWidth"), card.Root.Height ?? Length("AxFormFrameHeight"));
        var titled = item is { Root: not null } ? item.IsTopLevel : card.Kind == FormKind.Window;

        return new Box(size.Width, size.Height, titled ? Length("UiDesigner.Form.TitleBar.Height") : 0);
    }

    /// <summary>Вся доска — с заголовками окон; пустая — null.</summary>
    private Rect? Everything() =>
        _model is { Cards.Count: > 0 } model ? Union(model.Cards) : null;

    /// <summary>Объединение мест форм — вместе с заголовками окон над ними.</summary>
    private Rect Union(IReadOnlyCollection<FormCard> cards) =>
        cards.Select(PlaceOf).Aggregate((all, next) => all.Union(next));

    /// <summary>Место формы на холсте — вместе с заголовком окна над ней.</summary>
    private Rect PlaceOf(FormCard card)
    {
        var box = BoxOf(card);

        return new Rect(card.Location.X, card.Location.Y - box.Above, box.Width, box.Height + box.Above);
    }

    private double Length(string key) => _view is { } view ? SheetControls.LengthOf(view, key) : 0;

    private static FormCard? CardOf(SurfaceView sheet, Control container) =>
        container.DataContext as FormCard ?? sheet.ItemFromContainer(container) as FormCard;

    /// <summary>Верхняя форма на виду под точкой холста: место — у формы, размер — у её карточки.</summary>
    private static FormCard? CardAt(SurfaceView sheet, Point world) =>
        sheet.GetRealizedContainers()
            .OfType<UiDesignerFormItem>()
            .Select(item => (Item: item, Card: CardOf(sheet, item)))
            .LastOrDefault(form => form.Card is { } card && new Rect(card.Location, form.Item.Bounds.Size).Contains(world))
            .Card;
}
