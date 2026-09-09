using ArxisStudio.Controls;
using ArxisStudio.Modules.Console.Feed;
using ArxisStudio.Modules.Console.Log;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ArxisStudio.Modules.Console.Panels;

/// <summary>
/// Панель журнала: что студия и расширения сказали за этот сеанс.
/// </summary>
/// <remarks>
/// До этой панели журнал был виден только в стандартном выводе, то есть только
/// тому, кто запустил студию из терминала. О сбое расширения, об отключении его
/// за три падения и о неудавшемся этапе запуска обычный человек не узнавал
/// никак.
/// <para>
/// <b>Панель никогда не пишет в журнал.</b> Студия пишет «панель встала в
/// раскладку» ровно в тот миг, когда панель подписывается на журнал, и панель,
/// пишущая из своего же обработчика, кормила бы себя. Склейка перестроений это
/// пережила бы, но разбираться в такой петле не должен никто.
/// </para>
/// </remarks>
[ToolWindow(ConsoleModule.LogPanelId)]
public sealed class LogPanel : ToolWindow
{
    private readonly Rows<LogRow> _rows = [];

    private LogPanelView _view = null!;
    private Refresh _refresh = null!;
    private IStudioLogFeed? _feed;

    private LogFilter _filter = LogFilter.Everything;
    private LogCounts _counts;
    private bool _collapse;
    private bool _autoscroll = true;
    private bool _stamps = true;

    // Чем закончилось прошлое перестроение: первая учтённая запись и сколько
    // их было. По ним и видно, дописали журнал или вытеснили из него старое.
    private StudioLogRecord? _head;
    private int _seen;

    /// <summary>Сколько раз список перестраивался — открыто ради теста.</summary>
    public int Rebuilds => _refresh?.Runs ?? 0;

    /// <inheritdoc/>
    protected override Control Build()
    {
        _view = new LogPanelView();
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

        // Обработчики именованные: лямбду не отписать, а отписаться придётся —
        // студия зовёт Release прежде, чем отпустит панель.
        if (_feed is not null)
            _feed.Changed += OnFeedChanged;

        Context.Settings.Changed += OnSettingsChanged;

        _view.ActualThemeVariantChanged += OnThemeChanged;

        ReadTheme();
        Apply(ConsoleSettings.Read(Context.Settings));
        ShowLevels();

        _view.Collapse.IsChecked = _collapse;
        _view.Details.IsChecked = false;
        _view.DetailsPane.IsVisible = false;

        // Немедленно, а не отложенно: панель обязана показать то, что в журнале
        // уже есть, — иначе человек увидит пустоту и заполнение через кадр.
        _refresh.Now();

        ConsoleHub.AttachLog(Reveal);

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

        _refresh.Stop();
        ConsoleHub.DetachLog();
    }

    /// <summary>Панель попросили показаться: выделять нечего, но хвост показать стоит.</summary>
    private void Reveal()
    {
        if (_autoscroll)
            ScrollToTail();
    }

    /// <summary>
    /// Журнал изменился.
    /// </summary>
    /// <remarks>
    /// Здесь не делается ничего, кроме просьбы перестроить: событие приходит на
    /// каждую запись и на потоке того, кто писал, — а писать могут пачкой и из
    /// фоновой задачи.
    /// </remarks>
    private void OnFeedChanged(object? sender, EventArgs e) => _refresh.Ask();

    private void OnSettingsChanged(object? sender, string key)
    {
        if (!ConsoleSettings.Keys.Contains(key))
            return;

        Apply(ConsoleSettings.Read(Context.Settings));

        // Показ времени меняет сами строки, поэтому перестраивать надо целиком.
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

        if (Grew(records))
        {
            _counts = LogRows.Add(
                _counts,
                LogRows.Append(_rows, records, _seen, _filter, _collapse, _stamps));
        }
        else
        {
            var built = LogRows.Build(records, _filter, _collapse, _stamps);

            _rows.Reset(built.Rows);
            _counts = built.Counts;
        }

        _seen = records.Count;
        _head = records.Count > 0 ? records[0] : null;

        ShowCounts();
        ShowEmpty(records.Count);

        if (_autoscroll)
            ScrollToTail();
    }

    /// <summary>
    /// Журнал только дописали — старое на месте.
    /// </summary>
    /// <remarks>
    /// Сравнивается ссылка на первую запись: журнал вытесняет старое с начала,
    /// и стоило ему это сделать, как прежние строки перестают отвечать
    /// содержимому. Записи неизменяемы, поэтому ссылки достаточно.
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

    private void OnLevelClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ConsoleToggle toggle)
            return;

        toggle.IsChecked = !toggle.IsChecked;

        _filter = _filter with
        {
            Error = _view.Errors.IsChecked,
            Warning = _view.Warnings.IsChecked,
            Info = _view.Infos.IsChecked,
            Debug = _view.Debugs.IsChecked,
        };

        ShowLevels();
        Forget();
        _refresh.Ask();
    }

    private void OnCollapseClick(object? sender, RoutedEventArgs e)
    {
        _collapse = !_collapse;
        _view.Collapse.IsChecked = _collapse;

        Forget();
        _refresh.Ask();
    }

    /// <summary>
    /// Следовать за хвостом или нет.
    /// </summary>
    /// <remarks>
    /// Решает только этот переключатель: прокрутка вверх его не выключает.
    /// Так поведение остаётся предсказуемым — человек, отказавшийся следовать
    /// за хвостом, не обнаружит, что студия передумала за него.
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
    /// Показывает или прячет подробности.
    /// </summary>
    /// <remarks>
    /// Меняется доля строки, а не только видимость: скрытая строка ненулевой
    /// высоты оставила бы под списком пустую полосу, а показанная в
    /// фиксированные полтораста пикселей съела бы невысокую панель целиком —
    /// список пропал бы с глаз. Дальше долю правит человек: между строками
    /// стоит <c>AxSplitter</c>, и прячется он вместе с подробностями — граница,
    /// которой не с чем граничить, ничего не разделяет.
    /// </remarks>
    private void OnDetailsClick(object? sender, RoutedEventArgs e)
    {
        _view.Details.IsChecked = !_view.Details.IsChecked;

        _view.DetailsPane.IsVisible = _view.Details.IsChecked;
        _view.Handle.IsVisible = _view.Details.IsChecked;

        _view.Body.RowDefinitions[2].Height = _view.Details.IsChecked
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
    }

    private void OnClearClick(object? sender, RoutedEventArgs e) => _feed?.Clear();

    private void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (_view.Records.SelectedItem is not LogRow row)
            return;

        var text = $"{row.Stamp} {row.Level} {row.Source} {row.Record.Message}";

        // Отпущенная задача намеренно: буфер обмена — дело платформы, и ждать
        // его в обработчике щелчка нечего. Не вышло — человек нажмёт ещё раз.
        _ = TopLevel.GetTopLevel(_view)?.Clipboard?.SetTextAsync(text);
    }

    /// <summary>
    /// Меню источников: все, кто писал в этот журнал.
    /// </summary>
    /// <remarks>
    /// Список собирается при каждом открытии, а не держится: источник — просто
    /// строка, которую называет пишущий, и появиться новый может в любой миг.
    /// Это то же, что «Show output from» у Visual Studio, только имена приходят
    /// не из перечня каналов, а из самих записей.
    /// </remarks>
    private void OnSourcesClick(object? sender, RoutedEventArgs e)
    {
        var flyout = new AxMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };

        flyout.Items.Add(Item(Context.Strings["console.source.all"], null));

        foreach (var source in Sources())
            flyout.Items.Add(Item(source, source));

        flyout.ShowAt(_view.Sources);

        AxMenuItem Item(string header, string? source)
        {
            var item = new AxMenuItem { Header = header };

            item.Click += (_, _) =>
            {
                _filter = _filter with { Source = source };

                Forget();
                _refresh.Ask();
            };

            return item;
        }
    }

    private IEnumerable<string> Sources() =>
        (_feed?.Records ?? [])
            .Select(record => record.Source)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(source => source, StringComparer.CurrentCulture);

    private void OnSelected(object? sender, SelectionChangedEventArgs e) =>
        _view.DetailsText.Text = _view.Records.SelectedItem is LogRow row ? row.Record.Message : string.Empty;

    private void OnQueryChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.TextProperty)
            return;

        _filter = _filter with { Query = _view.Query.Text ?? string.Empty };

        Forget();
        _refresh.Ask();
    }

    /// <summary>
    /// Берёт у темы цвета уровней.
    /// </summary>
    /// <remarks>
    /// Кистью, а не стилем в разметке: селектор, лезущий в содержимое чужой
    /// кнопки, до значка не достаёт — проверено на живой студии, значок
    /// оставался цвета кнопки. Кисти при этом по-прежнему принадлежат теме:
    /// здесь только просьба выдать их по имени, как это делает вид терминала.
    /// </remarks>
    private void ReadTheme()
    {
        _view.ErrorIcon.Foreground = Brush("AxRedBrush");
        _view.WarningIcon.Foreground = Brush("AxYelBrush");
    }

    private IBrush? Brush(string key) =>
        _view.TryFindResource(key, _view.ActualThemeVariant, out var value) ? value as IBrush : null;

    private void OnThemeChanged(object? sender, EventArgs e) => ReadTheme();

    /// <summary>
    /// Показывает, какие уровни включены.
    /// </summary>
    /// <remarks>
    /// Выключенный счётчик приглушается целиком. Тема красит <c>:selected</c>
    /// только у кнопок класса <c>icon</c> — квадратных по высоте строки, — а
    /// счётчику нужна ширина под число, и класс у него другой.
    /// </remarks>
    private void ShowLevels()
    {
        Show(_view.Errors, _filter.Error);
        Show(_view.Warnings, _filter.Warning);
        Show(_view.Infos, _filter.Info);
        Show(_view.Debugs, _filter.Debug);

        static void Show(ConsoleToggle toggle, bool on)
        {
            toggle.IsChecked = on;
            toggle.Opacity = on ? 1 : 0.45;
        }
    }

    private void ShowCounts()
    {
        _view.ErrorCount.Text = _counts.Error.ToString(System.Globalization.CultureInfo.CurrentCulture);
        _view.WarningCount.Text = _counts.Warning.ToString(System.Globalization.CultureInfo.CurrentCulture);
        _view.InfoCount.Text = _counts.Info.ToString(System.Globalization.CultureInfo.CurrentCulture);
        _view.DebugCount.Text = _counts.Debug.ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Объясняет пустоту.
    /// </summary>
    /// <remarks>
    /// «Журнал пуст» и «под отбор ничего не подходит» — разные положения, и
    /// ответить вторым на первое значит дать человеку повод считать панель
    /// сломанной.
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
