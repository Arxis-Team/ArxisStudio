using ArxisStudio.Controls;
using ArxisStudio.Modules.Console.Feed;
using ArxisStudio.Modules.Console.Log;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.Console.Panels;

/// <summary>
/// Панель журнала: что студия и расширения сказали за этот сеанс.
/// </summary>
/// <remarks>
/// До этой панели журнал был виден только в стандартном выводе, то есть только тому, кто запустил
/// студию из терминала. О сбое расширения, об отключении его за три падения и о неудавшемся этапе
/// запуска обычный человек не узнавал никак.
/// <para>
/// Здесь — жизненный цикл и связывание: подписки, состояние отбора и передача работы тем, кто её
/// делает. Счётчики уровней показывает <see cref="LogToolbar"/>, меню источников —
/// <see cref="LogSourceMenu"/>, меню строки — <see cref="LogRowMenu"/>, вид записи текстом —
/// <see cref="LogText"/>.
/// </para>
/// <para>
/// <b>Панель никогда не пишет в журнал.</b> Студия пишет «панель встала в раскладку» ровно в тот
/// миг, когда панель подписывается на журнал, и панель, пишущая из своего же обработчика, кормила
/// бы себя. Склейка перестроений это пережила бы, но разбираться в такой петле не должен никто.
/// </para>
/// </remarks>
[ToolWindow(ConsoleModule.LogPanelId)]
public sealed class LogPanel : ToolWindow
{
    /// <summary>
    /// Насколько близко к низу список считается «у хвоста», в точках раскладки.
    /// </summary>
    /// <remarks>
    /// Не ноль: доля пикселя набегает от округления строки и масштаба экрана, и требовать точного
    /// совпадения значило бы не поймать возврат к хвосту на дробном масштабе.
    /// </remarks>
    private const double TailTolerance = 1;

    private readonly Rows<LogRow> _rows = [];

    private LogPanelView _view = null!;
    private ScrollViewer? _scroll;
    private LogToolbar _toolbar = null!;
    private LogSourcePicker _sources = null!;
    private LogRowMenu _menu = null!;
    private Refresh _refresh = null!;
    private IStudioLogFeed? _feed;

    private LogFilter _filter = LogFilter.Everything;
    private LogCounts _counts;
    private bool _collapse;
    private bool _autoscroll = true;
    private bool _details;
    private bool _stamps = true;

    // Чем закончилось прошлое перестроение: первая учтённая запись и сколько
    // их было. По ним и видно, дописали журнал или вытеснили из него старое.
    private StudioLogRecord? _head;
    private int _seen;

    /// <summary>Сколько раз список перестраивался — открыто ради теста.</summary>
    public int Rebuilds => _refresh?.Runs ?? 0;

    /// <summary>
    /// Пункты списка источников — те самые, что показывает меню; открыто ради теста.
    /// </summary>
    /// <remarks>
    /// Меню живёт в попапе, а попап — отдельное окно, которого у безголового прогона нет. Здесь
    /// же отдаётся ровно то, что меню показывает: те же пункты, с теми же обработчиками и тем же
    /// отбором за спиной.
    /// </remarks>
    internal IReadOnlyList<AxMenuItem> SourceItems() => LogSourceMenu.Items(_sources, Context.Strings);

    /// <summary>Пункты меню строки — те самые, что показывает меню; открыто ради теста.</summary>
    /// <param name="row">Строка, на которой стоят; <c>null</c> — щёлкнули мимо строк.</param>
    internal IReadOnlyList<AxMenuItem> RowItems(LogRow? row) => _menu.Items(row);

    /// <inheritdoc/>
    /// <remarks>
    /// Консоль открывают, чтобы читать записи, а первым, кто берёт каретку, в панели стоит отбор
    /// ошибок: F6 приводил на кнопку-переключатель, и стрелки по журналу не ходили.
    /// </remarks>
    public override Control? FocusTarget => _view.Records;

    /// <inheritdoc/>
    protected override Control Build()
    {
        _view = new LogPanelView();
        _toolbar = new LogToolbar(_view);
        _sources = new LogSourcePicker(Sources, () => _filter.Sources, PickSources);
        _menu = new LogRowMenu(Context.Strings, CopyRows, CopyMessage, Clear, _sources);
        _refresh = new Refresh(Rebuild);
        _feed = Context.GetService<IStudioLogFeed>();

        _view.Records.ItemsSource = _rows;
        _view.Records.SelectionChanged += OnSelected;

        _view.Errors.Click += OnLevelClick;
        _view.Warnings.Click += OnLevelClick;
        _view.Infos.Click += OnLevelClick;
        _view.Debugs.Click += OnLevelClick;

        _view.Collapse.Click += OnCollapseClick;
        _view.Autoscroll.Click += OnAutoscrollClick;
        _view.Details.Click += OnDetailsClick;
        _view.Sources.Click += OnSourcesClick;
        _view.Copy.Click += OnCopyClick;
        _view.Clear.Click += OnClearClick;
        _view.Query.PropertyChanged += OnQueryChanged;

        // Клавиши ловятся там, где они всплывают: Ctrl+C из подробностей не должен достаться
        // списку, и потому обработчик стоит на корне панели, а не туннелем.
        _view.Records.AddHandler(InputElement.ContextRequestedEvent, OnMenuAsked);
        _view.AddHandler(InputElement.KeyDownEvent, OnKeyDown);

        // Область прокрутки появляется вместе с шаблоном списка, и берётся она по имени части.
        _view.Records.TemplateApplied += OnRecordsTemplate;

        // Обработчики именованные: лямбду не отписать, а отписаться придётся —
        // студия зовёт Release прежде, чем отпустит панель.
        if (_feed is not null)
            _feed.Changed += OnFeedChanged;

        Context.Settings.Changed += OnSettingsChanged;

        _view.ActualThemeVariantChanged += OnThemeChanged;

        _toolbar.ReadTheme();
        Apply(ConsoleSettings.Read(Context.Settings));
        _toolbar.ShowLevels(_filter);
        _toolbar.ShowSources(_filter.Sources);

        _view.Collapse.IsChecked = _collapse;
        _view.Details.IsChecked = false;
        _view.DetailsPane.IsVisible = false;

        // Немедленно, а не отложенно: панель обязана показать то, что в журнале
        // уже есть, — иначе человек увидит пустоту и заполнение через кадр.
        _refresh.Now();

        ConsoleHub.Attach(Reveal);

        return _view;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        if (_feed is not null)
            _feed.Changed -= OnFeedChanged;

        Context.Settings.Changed -= OnSettingsChanged;

        _view.ActualThemeVariantChanged -= OnThemeChanged;
        _view.Records.SelectionChanged -= OnSelected;
        _view.Query.PropertyChanged -= OnQueryChanged;

        _view.Records.RemoveHandler(InputElement.ContextRequestedEvent, OnMenuAsked);
        _view.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _view.Records.TemplateApplied -= OnRecordsTemplate;

        if (_scroll is not null)
            _scroll.PropertyChanged -= OnScrollMoved;

        _scroll = null;

        _refresh.Stop();
        ConsoleHub.Detach();
    }

    /// <summary>Панель попросили показаться: выделять нечего, но хвост показать стоит.</summary>
    /// <remarks>
    /// Просьба приходит в потоке того, кто позвал команду, а команду сосед волен позвать и из
    /// фоновой работы. Список — контрол, и трогают его только из потока интерфейса; так же
    /// отвечает на просьбы и панель терминала.
    /// </remarks>
    private void Reveal()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Reveal);
            return;
        }

        if (_autoscroll)
            ScrollToTail();
    }

    /// <summary>
    /// Журнал изменился.
    /// </summary>
    /// <remarks>
    /// Здесь не делается ничего, кроме просьбы перестроить: событие приходит на каждую запись и на
    /// потоке того, кто писал, — а писать могут пачкой и из фоновой задачи.
    /// </remarks>
    private void OnFeedChanged(object? sender, EventArgs e) => _refresh.Ask();

    /// <summary>
    /// Настройку поменяли — снаружи или нашей же кнопкой.
    /// </summary>
    /// <remarks>
    /// Перестраивает только то, что меняет сами строки, — показ времени. Следование за хвостом
    /// решает, прокручивать ли список, и строк не трогает; перестроение ради него было бы не просто
    /// лишней работой, а потерей: список пересобирается новыми строками, и выделение вместе с
    /// открытыми подробностями пропадает у человека под руками.
    /// <para>
    /// Сравнение здесь обязательно, а не бережливость: настройки пишутся парой, поэтому щелчок по
    /// прокрутке будит и ключ времени — с прежним значением. Спрашивать надо не «какой ключ
    /// пришёл», а «изменилось ли то, из чего собраны строки».
    /// </para>
    /// </remarks>
    private void OnSettingsChanged(object? sender, string key)
    {
        if (!ConsoleSettings.Keys.Contains(key))
            return;

        // Настройку пишут и не из потока интерфейса — контракт настроек этого не запрещает, — а
        // переключатель автопрокрутки и строки списка живут в нём.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnSettingsChanged(sender, key));
            return;
        }

        var settings = ConsoleSettings.Read(Context.Settings);
        var rows = settings.Timestamps != _stamps;

        Apply(settings);

        if (!rows)
            return;

        Forget();
        _refresh.Ask();
    }

    private void Apply(ConsoleSettings settings)
    {
        _autoscroll = settings.Autoscroll;
        _stamps = settings.Timestamps;

        _view.Autoscroll.IsChecked = _autoscroll;
    }

    private void Rebuild()
    {
        var records = _feed?.Records ?? [];
        var repeats = Context.Strings["console.repeats"];

        if (Grew(records))
        {
            _counts = LogRows.Add(
                _counts,
                LogRows.Append(_rows, records, _seen, _filter, _collapse, _stamps, repeats));
        }
        else
        {
            var built = LogRows.Build(records, _filter, _collapse, _stamps, repeats);

            _rows.Reset(built.Rows);
            _counts = built.Counts;
        }

        _seen = records.Count;
        _head = records.Count > 0 ? records[0] : null;

        _toolbar.ShowCounts(_counts);
        ShowEmpty(records.Count);

        if (_autoscroll)
            ScrollToTail();
    }

    /// <summary>
    /// Журнал только дописали — старое на месте.
    /// </summary>
    /// <remarks>
    /// Сравнивается ссылка на первую запись: журнал вытесняет старое с начала, и стоило ему это
    /// сделать, как прежние строки перестают отвечать содержимому. Записи неизменяемы, поэтому
    /// ссылки достаточно.
    /// </remarks>
    private bool Grew(IReadOnlyList<StudioLogRecord> records) =>
        _head is not null &&
        records.Count > _seen &&
        _seen > 0 &&
        ReferenceEquals(records[0], _head);

    /// <summary>Забывает прошлое перестроение — следующее будет полным.</summary>
    private void Forget()
    {
        _head = null;
        _seen = 0;
    }

    /// <summary>Отбор изменился: строки надо собрать заново.</summary>
    private void Refilter()
    {
        Forget();
        _refresh.Ask();
    }

    /// <summary>
    /// Включает или выключает уровень, на кнопку которого нажали.
    /// </summary>
    /// <remarks>
    /// Включённость уровня хранит отбор, а кнопка её только показывает. Переключатель
    /// переворачивает себя сам на нажатии, и переворот отсюда вернул бы его назад, — поэтому
    /// переворачивается отбор, а кнопке состояние ставит <see cref="LogToolbar.ShowLevels"/>.
    /// </remarks>
    private void OnLevelClick(object? sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, _view.Errors))
            _filter = _filter with { Error = !_filter.Error };
        else if (ReferenceEquals(sender, _view.Warnings))
            _filter = _filter with { Warning = !_filter.Warning };
        else if (ReferenceEquals(sender, _view.Infos))
            _filter = _filter with { Info = !_filter.Info };
        else if (ReferenceEquals(sender, _view.Debugs))
            _filter = _filter with { Debug = !_filter.Debug };
        else
            return;

        _toolbar.ShowLevels(_filter);
        Refilter();
    }

    private void OnCollapseClick(object? sender, RoutedEventArgs e)
    {
        _collapse = !_collapse;
        _view.Collapse.IsChecked = _collapse;

        Refilter();
    }

    /// <summary>
    /// Прокручивать ли к последней записи.
    /// </summary>
    /// <remarks>
    /// Нажатие — выбор человека, и он же уходит в настройки: следование за хвостом переживает
    /// перезапуск. Прокрутка колесом тоже отпускает хвост (<see cref="OnScrollMoved"/>), но в
    /// настройки не пишется — иначе файл переписывался бы на каждое движение колеса.
    /// </remarks>
    private void OnAutoscrollClick(object? sender, RoutedEventArgs e)
    {
        _autoscroll = !_autoscroll;
        _view.Autoscroll.IsChecked = _autoscroll;

        new ConsoleSettings(_autoscroll, _stamps).Write(Context.Settings);

        if (_autoscroll)
            ScrollToTail();
    }

    /// <summary>
    /// Шаблон списка развернулся: у него появилась область прокрутки.
    /// </summary>
    /// <remarks>
    /// Берётся она по имени части — так же, как это делает всякий контрол со своим шаблоном.
    /// Маршрутизируемое событие <c>ScrollChanged</c> сюда не годится: до списка оно не доходит, и
    /// панель узнавала бы о прокрутке только на живом окне — проверено безголовым прогоном.
    /// </remarks>
    private void OnRecordsTemplate(object? sender, TemplateAppliedEventArgs e)
    {
        if (_scroll is not null)
            _scroll.PropertyChanged -= OnScrollMoved;

        _scroll = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");

        if (_scroll is not null)
            _scroll.PropertyChanged += OnScrollMoved;
    }

    /// <summary>
    /// Список прокрутили: у хвоста мы или ушли от него.
    /// </summary>
    /// <remarks>
    /// Так ведут себя консоли Rider и VS Code: человек, уехавший вверх читать давнюю ошибку, не
    /// хочет, чтобы его утащило вниз следующей же записью, — а вернувшись к низу, снова ждёт
    /// хвоста, и просить об этом кнопкой ему незачем.
    /// <para>
    /// Смотрим только на место: новая запись меняет высоту содержимого, а не место человека в нём,
    /// и на галочку не влияет. Собственная прокрутка панели к хвосту приводит сюда же — и
    /// оставляет всё как есть, потому что кончается ровно у низа.
    /// </para>
    /// </remarks>
    private void OnScrollMoved(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ScrollViewer.OffsetProperty || _scroll is null)
            return;

        var tail = _scroll.Offset.Y >= _scroll.Extent.Height - _scroll.Viewport.Height - TailTolerance;

        if (tail == _autoscroll)
            return;

        _autoscroll = tail;
        _view.Autoscroll.IsChecked = tail;
    }

    /// <summary>
    /// Показывает или прячет подробности.
    /// </summary>
    /// <remarks>
    /// Меняется доля строки, а не только видимость: скрытая строка ненулевой высоты оставила бы под
    /// списком пустую полосу, а показанная в фиксированные полтораста пикселей съела бы невысокую
    /// панель целиком — список пропал бы с глаз. Дальше долю правит человек: между строками стоит
    /// <c>AxSplitter</c>, и прячется он вместе с подробностями — граница, которой не с чем
    /// граничить, ничего не разделяет.
    /// </remarks>
    private void OnDetailsClick(object? sender, RoutedEventArgs e)
    {
        _details = !_details;

        _view.Details.IsChecked = _details;
        _view.DetailsPane.IsVisible = _details;
        _view.Handle.IsVisible = _details;

        _view.Body.RowDefinitions[2].Height = _details
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
    }

    private void OnClearClick(object? sender, RoutedEventArgs e) => Clear();

    private void OnCopyClick(object? sender, RoutedEventArgs e) => CopyRows();

    /// <summary>
    /// Клавиши панели.
    /// </summary>
    /// <remarks>
    /// Всплывающим событием, а не туннельным: <c>Ctrl+C</c> в подробностях копирует выделенный там
    /// текст, и перехват на пути вниз отобрал бы его у поля. Сюда доходит только то, что не взял
    /// никто.
    /// </remarks>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.C)
        {
            CopyRows();
            e.Handled = true;

            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F)
        {
            _view.Query.Focus();
            _view.Query.SelectAll();
            e.Handled = true;

            return;
        }

        if (e.Key == Key.Escape && ReferenceEquals(e.Source, _view.Query))
        {
            _view.Query.Clear();
            _view.Records.Focus();
            e.Handled = true;
        }
    }

    /// <summary>
    /// У списка попросили меню — мышью или клавишей меню.
    /// </summary>
    /// <remarks>
    /// Строка под курсором выделяется, если не была выделена: иначе пункт «скопировать» относился
    /// бы к записи, на которую человек не показывал. Выделенную группу щелчок правой кнопкой не
    /// рушит — так ведут себя списки Windows.
    /// <para>
    /// Откуда пришла просьба, говорит сам довод: у мыши есть место, у клавиши меню его нет.
    /// Мышью меню встаёт под указателем, клавишей — у строки, на которой стоят; привязанное к
    /// списку, оно уезжало бы к его углу, а список тут во всю ширину панели.
    /// </para>
    /// </remarks>
    private void OnMenuAsked(object? sender, ContextRequestedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var row = RowOf(e.Source as Visual) ?? _view.Records.SelectedItem as LogRow;

        if (row is not null && !_view.Records.SelectedItems!.Contains(row))
            _view.Records.SelectedItem = row;

        var pointer = e.TryGetPosition(null, out _);
        var anchor = pointer || row is null
            ? _view.Records
            : _view.Records.ContainerFromItem(row) as Control ?? _view.Records;

        _menu.ShowAt(anchor, row, pointer);
        e.Handled = true;
    }

    /// <summary>Строка, которой принадлежит элемент разметки; <c>null</c> — щёлкнули мимо строк.</summary>
    private static LogRow? RowOf(Visual? source)
    {
        for (var node = source; node is not null; node = node.GetVisualParent())
        {
            if (node is AxListBoxItem item)
                return item.DataContext as LogRow;
        }

        return null;
    }

    /// <summary>
    /// Меню источников: все, кто писал в этот журнал.
    /// </summary>
    /// <remarks>
    /// Кнопка — переключатель, и нажатие переворачивает её само: отбор при этом не меняется, меню
    /// только открывается, — поэтому состояние ставится заново, прежде чем меню встанет. Перевёрнутой
    /// кнопка не останется и на миг: ставится она в том же обработчике, до показа.
    /// </remarks>
    private void OnSourcesClick(object? sender, RoutedEventArgs e)
    {
        _toolbar.ShowSources(_filter.Sources);

        LogSourceMenu.ShowAt(_view.Sources, _sources, Context.Strings);
    }

    /// <summary>
    /// Отбор по источнику выбрали — в полосе или в меню строки.
    /// </summary>
    /// <remarks>
    /// Сравнение здесь не бережливость, а условие: меню остаётся открытым, и «все источники»,
    /// нажатые дважды, пришли бы сюда вторым тем же отбором. Перестроение на нём стёрло бы
    /// выделение и место прокрутки, ничего не изменив в списке.
    /// </remarks>
    private void PickSources(LogSources sources)
    {
        if (sources == _filter.Sources)
            return;

        _filter = _filter with { Sources = sources };

        _toolbar.ShowSources(sources);
        Refilter();
    }

    /// <summary>Кто писал в журнал к этому мигу — по одному имени, по алфавиту.</summary>
    /// <remarks>
    /// Список строится на каждый показ меню: источник появляется тогда, когда просыпается
    /// расширение, и держать его между показами значило бы показывать вчерашний состав.
    /// </remarks>
    private IReadOnlyList<string> Sources() =>
        [.. (_feed?.Records ?? [])
            .Select(record => record.Source)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(source => source, StringComparer.CurrentCulture)];

    private void OnSelected(object? sender, SelectionChangedEventArgs e) =>
        _view.DetailsText.Text = _view.Records.SelectedItem is LogRow row ? row.Record.Message : string.Empty;

    private void OnQueryChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.TextProperty)
            return;

        _filter = _filter with { Query = _view.Query.Text ?? string.Empty };

        Refilter();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => _toolbar.ReadTheme();

    private void Clear() => _feed?.Clear();

    /// <summary>
    /// Копирует выделенные записи в порядке показа.
    /// </summary>
    /// <remarks>
    /// В порядке показа, а не выделения: человек выделяет снизу вверх так же часто, как сверху
    /// вниз, а читает вставленное сверху вниз всегда.
    /// <para>
    /// Задача отпущена намеренно: буфер обмена — дело платформы, и ждать его в обработчике щелчка
    /// нечего. Не вышло — человек нажмёт ещё раз.
    /// </para>
    /// </remarks>
    private void CopyRows()
    {
        var chosen = _view.Records.SelectedItems?.OfType<LogRow>().ToHashSet();

        if (chosen is not { Count: > 0 })
            return;

        Copy(LogText.Of(_rows.Where(chosen.Contains)));
    }

    /// <summary>Копирует сообщение записи, на которой стоят, — без времени, уровня и источника.</summary>
    private void CopyMessage()
    {
        if (_view.Records.SelectedItem is LogRow row)
            Copy(LogText.Message(row));
    }

    private void Copy(string text) =>
        _ = TopLevel.GetTopLevel(_view)?.Clipboard?.SetTextAsync(text);

    /// <summary>
    /// Объясняет пустоту.
    /// </summary>
    /// <remarks>
    /// «Журнал пуст» и «ничего не найдено» — разные положения, и ответить вторым на первое значит
    /// дать человеку повод считать панель сломанной.
    /// </remarks>
    private void ShowEmpty(int records)
    {
        _view.Empty.IsVisible = _rows.Count == 0;

        if (_rows.Count == 0)
            _view.Empty.Text = Context.Strings[records == 0 ? "console.empty" : "console.nothing"];
    }

    private void ScrollToTail()
    {
        if (_rows.Count > 0)
            _view.Records.ScrollIntoView(_rows[^1]);
    }
}
