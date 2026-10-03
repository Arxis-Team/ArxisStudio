using System.Globalization;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Surface;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Panels;

/// <summary>
/// Доска форм решения: карточки <c>.axaml</c> на холсте Surface.
/// </summary>
/// <remarks>
/// Холст — ядро Surface как есть: панорама средней кнопкой, зум колесом к курсору, рамка выбора левой
/// кнопкой по пустому месту, Shift — добавить к выбору, тяга по целым точкам, стрелки — сдвиг на шаг,
/// Shift со стрелкой — на крупный, Ctrl+Z и Ctrl+Y — отмена и повтор (<see cref="SurfaceHistory"/>).
/// Изменение размера у ядра доска снимает: размер карточке задаёт тема.
/// <para>
/// Delete и Backspace — запрос ядра на удаление выбранного — доска выполняет уборкой с доски: карточки
/// уходят из коллекции холста, файлы форм остаются на месте. Так же убирает пункт меню карточки, и так же
/// уборка отменяется — Ctrl+Z, — а вернуть убранное можно и из меню пустого холста.
/// </para>
/// <para>
/// Своё у доски — то, что знает она одна. Enter и двойной щелчок открывают форму в редакторе документов
/// студии. F показывает выбранное целиком, а без выбора — всю доску, как F в Unity и Unreal.
/// «Упорядочить» раскладывает доску сеткой в порядке решения и ложится в ту же историю, что тяга.
/// </para>
/// <para>
/// Режим дизайнера выбирают здесь же, парой переключателей в полосе: все формы на одной доске или каждая
/// в своей вкладке. Режим — настройка, а не состояние доски: он решает, кто открывает форму, — в режиме
/// вкладок её берёт редактор документов модуля, и форма встаёт своей вкладкой с холстом на одну неё. Доска
/// при этом остаётся обзором, с которого формы открывают, а открытые вкладки смена режима не закрывает.
/// </para>
/// <para>
/// <b>Тяга из окна проекта.</b> Доска — цель перетаскивания студии (<see cref="StudioDragDrop"/>): на неё
/// несут файлы форм и контролов, как ассеты на сцену в Unity. Пока несут, в точке курсора стоит заготовка
/// той самой карточки — в масштабе холста, языком цели перетаскивания, — а у курсора сказано, что будет:
/// вернуть убранную, передвинуть стоящую или почему нельзя. Отпущенная форма встаёт серединой под
/// курсор, несколько — сеткой от неё; встают выбранными, клавиатура переходит к холсту, и Ctrl+Z
/// отменяет постановку целиком. Ответ — ссылка, а не копия: карточка показывает файл, а не забирает
/// его. Дорога без мыши — меню пустого холста «Вернуть на доску» (WCAG 2.5.7).
/// </para>
/// <para>
/// Места пишутся после каждой единицы правки — история сообщает о ней и о каждой отмене, — так что файл
/// доски всегда совпадает с тем, что на экране.
/// </para>
/// </remarks>
[ToolWindow(UiDesignerModule.PanelId)]
public sealed class BoardPanel : ToolWindow
{
    private BoardView? _view;
    private BoardModel? _model;
    private BoardMenu? _menu;
    private SurfaceHistory? _history;
    private SheetControls? _controls;
    private IReadOnlyList<CanonicalPath> _previewed = [];

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
        _menu = new BoardMenu(
            Context.Strings,
            new BoardActions(Open, Frame, Arrange, Remove, Return, () => _model?.Removed ?? [], Where));
        _history = new SurfaceHistory(sheet);
        _controls = new SheetControls(Context, sheet, view.Fit, view.Actual, view.GridToggle, Everything);

        view.DataContext = _model;
        sheet.EstimatedItemSize = new Size(Length("AxFormCardWidth"), Length("AxFormCardMinHeight"));

        Keys(sheet);
        Wire(view, sheet, _model, _history);
        Moded();

        return view;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        if (_view is { } view)
        {
            view.Sheet.DoubleTapped -= OnDoubleTapped;
            view.Sheet.ContextMenuRequesting -= OnContextMenuRequesting;
            view.Sheet.DeleteRequested -= OnDeleteRequested;
            view.ReturnAll.Click -= OnReturnAll;
            view.ArrangeAll.Click -= OnArrange;
            view.BoardMode.Click -= OnBoardMode;
            view.TabsMode.Click -= OnTabsMode;
            StudioDragDrop.RemoveDragOverHandler(view.Stage, OnDragOver);
            StudioDragDrop.RemoveDragLeaveHandler(view.Stage, OnDragLeave);
            StudioDragDrop.RemoveDropHandler(view.Stage, OnDrop);
            Unland(view);
        }

        if (_model is not null)
        {
            _model.Replaced -= OnReplaced;
            _model.PresenceChanged -= OnPresence;
        }

        Context.Settings.Changed -= OnSettingsChanged;

        _controls?.Dispose();
        _controls = null;
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
        if (cards.Count == 0)
            _controls?.FrameAll();
        else if (_view?.Sheet is { } sheet)
            _controls?.Frame(Union(sheet, cards));
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
    /// Убирает карточки с доски одной записью истории.
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
    /// Клавиши холста: свои у доски, и минус те, что ей не к месту.
    /// </summary>
    private void Keys(SurfaceView sheet)
    {
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
        sheet.DeleteRequested += OnDeleteRequested;
        view.ReturnAll.Click += OnReturnAll;
        view.ArrangeAll.Click += OnArrange;
        view.BoardMode.Click += OnBoardMode;
        view.TabsMode.Click += OnTabsMode;
        StudioDragDrop.AddDragOverHandler(view.Stage, OnDragOver);
        StudioDragDrop.AddDragLeaveHandler(view.Stage, OnDragLeave);
        StudioDragDrop.AddDropHandler(view.Stage, OnDrop);
        model.Replaced += OnReplaced;
        model.PresenceChanged += OnPresence;
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

    private void OnArrange(object? sender, RoutedEventArgs e)
    {
        Arrange();
        _controls?.Back();
    }

    /// <summary>
    /// Delete и Backspace: ядро просит удалить выбранное, доска убирает его с доски.
    /// </summary>
    /// <remarks>
    /// Выбранное берётся данными, а не контейнерами: выбор у холста бывает и за краем окна, где
    /// контейнера нет (ADR 0010 ядра).
    /// </remarks>
    private void OnDeleteRequested(object? sender, SurfaceDeleteRequestedEventArgs e)
    {
        var cards = e.Items.OfType<FormCard>().ToList();

        if (cards.Count == 0)
            return;

        Remove(cards);
        e.Handled = true;
    }

    private void OnReturnAll(object? sender, RoutedEventArgs e)
    {
        if (_model is { } model)
            Return([.. model.Removed.Select(form => form.File.Path)]);
    }

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
        Preview(view, landings, SpotsAt(view.Sheet, e.GetPosition(view.Sheet), landings.Count));
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
        var spots = SpotsAt(sheet, e.GetPosition(sheet), landings.Count);
        var (cards, change) = model.Land([.. landings.Select((landing, index) => (landing.Path, spots[index]))]);

        if (change is not null)
            history.Push(change);

        sheet.Selection.Clear();

        foreach (var card in cards)
            sheet.Selection.Select(model.Cards.IndexOf(card));

        _controls?.Back();
        e.Effect = effect;
    }

    /// <summary>Ссылка, а не копия: карточка показывает файл, а не забирает его. Переноса доска не просит.</summary>
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
    /// Места под курсором: первая карточка встаёт серединой под него, следующие — сеткой от неё.
    /// </summary>
    /// <remarks>
    /// Серединой, а не углом: подсказка у курсора лежит справа снизу от него и закрывала бы имя
    /// карточки, вставшей углом. Места — в целых точках, как у тяги по холсту.
    /// </remarks>
    private IReadOnlyList<Spot> SpotsAt(SurfaceView sheet, Point point, int count)
    {
        var world = sheet.GetWorldPosition(point);
        var origin = new Spot(
            Math.Round(world.X - Length("AxFormCardWidth") / 2),
            Math.Round(world.Y - Length("AxFormCardMinHeight") / 2));

        return BoardLayout.Grid(count, origin, Pitch());
    }

    /// <summary>
    /// Ставит заготовки карточек в места, где они встанут, — в масштабе холста; состояния прячутся.
    /// </summary>
    /// <remarks>
    /// Заготовки строятся, когда сменился состав несомого, а на движение мыши только переезжают; состав
    /// сверяется путями, а не карточками: у убранной формы, которой доска ещё не показывала, карточка для
    /// заготовки каждый раз новая. Кольцо масштабом не толстеет и не тает: его толщина у темы делится на
    /// масштаб холста.
    /// </remarks>
    private void Preview(BoardView view, IReadOnlyList<Landing> landings, IReadOnlyList<Spot> spots)
    {
        var sheet = view.Sheet;
        var preview = view.Preview;
        var zoom = sheet.ViewportZoom;
        var corner = sheet.ViewportLocation;

        if (!_previewed.SequenceEqual(landings.Select(landing => landing.Path))
            && view.TryFindResource("FormCardTemplate", out var found) && found is IDataTemplate template)
        {
            preview.Children.Clear();

            foreach (var landing in landings)
            {
                var host = new Panel { Classes = { "landing" }, RenderTransformOrigin = RelativePoint.TopLeft };

                host.Children.Add(new ContentPresenter { Content = landing.Card, ContentTemplate = template });
                host.Children.Add(new Border { Classes = { "landing-ring" } });
                preview.Children.Add(host);
            }

            _previewed = [.. landings.Select(landing => landing.Path)];
        }

        var ring = view.TryFindResource("AxDropTargetThickness", view.ActualThemeVariant, out var value) && value is Thickness thickness
            ? new Thickness(thickness.Left / zoom, thickness.Top / zoom, thickness.Right / zoom, thickness.Bottom / zoom)
            : default;

        for (var index = 0; index < preview.Children.Count && index < spots.Count; index++)
        {
            var host = (Panel)preview.Children[index];

            Canvas.SetLeft(host, (spots[index].X - corner.X) * zoom);
            Canvas.SetTop(host, (spots[index].Y - corner.Y) * zoom);
            host.RenderTransform = new ScaleTransform(zoom, zoom);
            ((Border)host.Children[1]).BorderThickness = ring;
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
    /// Доска сменила решение: отмена прежнего ничего не значит, а новое надо показать целиком.
    /// </summary>
    private void OnReplaced(object? sender, EventArgs e)
    {
        _history?.Clear();

        // Кадр — после раскладки: размер холста и карточек известен только ей.
        Dispatcher.UIThread.Post(() => Frame([]), DispatcherPriority.Loaded);
    }

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

    /// <summary>
    /// Вся доска; пустая — null.
    /// </summary>
    /// <remarks>
    /// Протяжённость холст держит сам — по месту каждой карточки и её размеру, измеренному или
    /// оценочному, — и отвечает за неё одной записью. Своё объединение по контейнерам — запасной путь до
    /// первой раскладки, когда протяжённости ещё нет.
    /// </remarks>
    private Rect? Everything()
    {
        if (_view?.Sheet is not { } sheet || _model is not { Cards.Count: > 0 } model)
            return null;

        return sheet.ItemsExtent is { Width: > 0, Height: > 0 } extent ? extent : Union(sheet, model.Cards);
    }

    private Rect Union(SurfaceView sheet, IReadOnlyCollection<FormCard> cards) =>
        cards.Select(card => BoundsOf(sheet, card)).Aggregate((all, next) => all.Union(next));

    private double Length(string key) => _view is { } view ? SheetControls.LengthOf(view, key) : 0;

    /// <summary>Место карточки на холсте: размер — у контейнера, если он есть, иначе у темы.</summary>
    private Rect BoundsOf(SurfaceView sheet, FormCard card)
    {
        var size = sheet.ContainerFromItem(card) is { Bounds.Size: { Width: > 0 } measured }
            ? measured
            : new Size(Length("AxFormCardWidth"), Length("AxFormCardMinHeight"));

        return new Rect(card.Location, size);
    }

    private static FormCard? CardAt(object? source) =>
        (source as Visual)?.FindAncestorOfType<SurfaceItem>(includeSelf: true)?.DataContext as FormCard;
}
