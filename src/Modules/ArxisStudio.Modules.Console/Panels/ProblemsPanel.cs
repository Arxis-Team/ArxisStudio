using ArxisStudio.Modules.Console.Feed;
using ArxisStudio.Modules.Console.Problems;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ArxisStudio.Modules.Console.Panels;

/// <summary>
/// Панель находок: что расширения нашли и о чём сообщили студии.
/// </summary>
/// <remarks>
/// Читать список позволено целиком: обёртка, которую студия выдаёт модулю,
/// сужает только запись — под своим именем, — а видно панели всё, что сообщили
/// все. Иначе панель находок показывала бы находки одной себя.
/// <para>
/// Очистки здесь нет намеренно. Каждый источник отвечает за свой участок
/// целиком, и «очистить» со стороны смотрящего значило бы снять чужие находки
/// — те вернулись бы при первой же перепроверке, а человек решил бы, что
/// кнопка не работает.
/// </para>
/// </remarks>
[ToolWindow(ConsoleModule.ProblemsPanelId)]
public sealed class ProblemsPanel : ToolWindow
{
    private readonly Rows<ProblemRow> _rows = [];

    private ProblemsPanelView _view = null!;
    private Refresh _refresh = null!;
    private IStudioProblems? _problems;

    private ProblemFilter _filter = ProblemFilter.Everything;
    private ProblemCounts _counts;

    private IBrush? _error;
    private IBrush? _warning;

    /// <summary>Сколько раз таблица перестраивалась — открыто ради теста.</summary>
    public int Rebuilds => _refresh?.Runs ?? 0;

    /// <inheritdoc/>
    protected override Control Build()
    {
        _view = new ProblemsPanelView();
        _refresh = new Refresh(Rebuild);
        _problems = Context.GetService<IStudioProblems>();

        _view.Findings.ItemsSource = _rows;
        _view.Findings.SelectionChanged += OnSelected;
        _view.Findings.DoubleTapped += OnActivated;

        _view.Errors.Click += OnLevelClick;
        _view.Warnings.Click += OnLevelClick;
        _view.Infos.Click += OnLevelClick;

        _view.Details.Click += OnDetailsClick;
        _view.Open.Click += OnOpenClick;
        _view.Query.PropertyChanged += OnQueryChanged;

        if (_problems is not null)
            _problems.Changed += OnProblemsChanged;

        _view.ActualThemeVariantChanged += OnThemeChanged;

        ReadTheme();
        ShowLevels();

        _view.Details.IsChecked = false;
        _view.DetailsPane.IsVisible = false;

        _refresh.Now();

        ConsoleHub.AttachProblems(Reveal);

        return _view;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        if (_problems is not null)
            _problems.Changed -= OnProblemsChanged;

        _view.ActualThemeVariantChanged -= OnThemeChanged;
        _view.Findings.SelectionChanged -= OnSelected;
        _view.Findings.DoubleTapped -= OnActivated;
        _view.Query.PropertyChanged -= OnQueryChanged;

        _refresh.Stop();
        ConsoleHub.DetachProblems();
    }

    /// <summary>Панель попросили показаться; выбирать за человека нечего.</summary>
    private void Reveal()
    {
    }

    private void OnProblemsChanged(object? sender, EventArgs e) => _refresh.Ask();

    /// <summary>
    /// Перестраивает таблицу целиком.
    /// </summary>
    /// <remarks>
    /// Быстрого пути здесь нет и быть не может: источник заменяет свой участок
    /// целиком, поэтому «дописали» — не то, что происходит. Список при этом
    /// невелик — десятки находок, а не тысячи записей.
    /// </remarks>
    private void Rebuild()
    {
        var found = _problems?.All ?? [];

        _counts = ProblemCounts.Of(found);

        _rows.Reset([.. found.Where(_filter.Matches).Select(problem => new ProblemRow(problem, Tint(problem)))]);

        ShowCounts();
        ShowEmpty(found.Count);
        ShowOpen();
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
        };

        ShowLevels();
        _refresh.Ask();
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

    private void OnQueryChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.TextProperty)
            return;

        _filter = _filter with { Query = _view.Query.Text ?? string.Empty };

        _refresh.Ask();
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e) => Open();

    private void OnActivated(object? sender, TappedEventArgs e) => Open();

    /// <summary>
    /// Открывает файл находки.
    /// </summary>
    /// <remarks>
    /// Панель просит «открой это», а не «покажи мне вот такой редактор»: кто
    /// возьмётся за файл, решает оболочка по объявленному типу. Службы может
    /// не быть, и находки без файла — обычное дело.
    /// </remarks>
    private void Open()
    {
        if (_view.Findings.SelectedItem is not ProblemRow row || !row.HasFile)
            return;

        // Отпущенная задача намеренно: открытие документа — дело оболочки, и
        // ждать его в обработчике щелчка панели нечего.
        _ = Context.GetService<IStudioDocuments>()?.OpenAsync(row.Problem.FilePath!);
    }

    private void OnSelected(object? sender, SelectionChangedEventArgs e)
    {
        _view.DetailsText.Text = _view.Findings.SelectedItem is ProblemRow row
            ? Describe(row)
            : string.Empty;

        ShowOpen();
    }

    /// <summary>Находка целиком: код, объяснение и место.</summary>
    private static string Describe(ProblemRow row) =>
        row.Where is { Length: > 0 } where
            ? $"{row.Code}  {where}{Environment.NewLine}{Environment.NewLine}{row.Problem.Message}"
            : $"{row.Code}{Environment.NewLine}{Environment.NewLine}{row.Problem.Message}";

    /// <summary>
    /// Берёт у темы цвета уровней.
    /// </summary>
    /// <remarks>
    /// Кистью, а не стилем в разметке: селектор, лезущий в содержимое чужой
    /// кнопки, до значка не достаёт — проверено на живой студии. Кисти при этом
    /// по-прежнему принадлежат теме, здесь только просьба выдать их по имени.
    /// </remarks>
    private void ReadTheme()
    {
        _error = Brush("AxRedBrush");
        _warning = Brush("AxYelBrush");

        _view.ErrorIcon.Foreground = _error;
        _view.WarningIcon.Foreground = _warning;
    }

    private IBrush? Brush(string key) =>
        _view.TryFindResource(key, _view.ActualThemeVariant, out var value) ? value as IBrush : null;

    /// <summary>Цвет уровня находки; у замечания своего цвета нет.</summary>
    private IBrush? Tint(StudioProblem problem) => problem.Severity switch
    {
        StudioProblemSeverity.Error => _error,
        StudioProblemSeverity.Warning => _warning,
        _ => null,
    };

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ReadTheme();

        // Кисти раздаются строкам при построении, поэтому строки надо собрать
        // заново: иначе после смены темы они остались бы прежнего цвета.
        _refresh.Ask();
    }

    /// <summary>
    /// Показывает, какие уровни включены.
    /// </summary>
    /// <remarks>
    /// Выключенный счётчик приглушается целиком — по той же причине, что и в
    /// панели журнала: тема красит <c>:selected</c> только у кнопок класса
    /// <c>icon</c>, а счётчику нужна ширина под число.
    /// </remarks>
    private void ShowLevels()
    {
        Show(_view.Errors, _filter.Error);
        Show(_view.Warnings, _filter.Warning);
        Show(_view.Infos, _filter.Info);

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
    }

    private void ShowEmpty(int found)
    {
        _view.Empty.IsVisible = _rows.Count == 0;

        if (_rows.Count == 0)
            _view.EmptyText.Text = Context.Strings[found == 0 ? "problems.empty" : "problems.nothing"];

        // Подсказка объясняет, почему пусто у всех; к отбору она не относится.
        _view.EmptyHint.IsVisible = _rows.Count == 0 && found == 0;
    }

    private void ShowOpen() =>
        _view.Open.IsEnabled = _view.Findings.SelectedItem is ProblemRow { HasFile: true };
}
