using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using ArxisStudio.Surface;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>
/// Органы холста, общие у доски и у вкладки формы: цвета студии, масштаб словами, «Вписать всё»,
/// «100 %» и сетка.
/// </summary>
/// <remarks>
/// Доска и вкладка показывают разное — все формы или одну, — а холст у них один и ведёт себя одинаково:
/// кадр не крупнее ста процентов, кнопка полосы возвращает клавиатуру холсту, сетку держит одна
/// настройка на оба вида. Поэтому и код один: второй экземпляр разошёлся бы с первым при первой же правке.
/// <para>
/// Кадр не крупнее ста процентов: одна карточка или маленькая форма во весь холст ничего не показывает.
/// Кнопка полосы отдаёт клавиатуру холсту — с кареткой на кнопке <c>Ctrl+Z</c> до истории холста не
/// дошёл бы; полоса Unity держит так же.
/// </para>
/// </remarks>
internal sealed class SheetControls : IDisposable
{
    /// <summary>Наибольший масштаб кадра.</summary>
    public const double FrameZoom = 1;

    private readonly IStudioContext _context;
    private readonly SurfaceView _sheet;
    private readonly AxButton _fit;
    private readonly AxButton _actual;
    private readonly AxToggleButton _grid;
    private readonly Func<Rect?> _all;
    private bool _disposed;

    /// <summary>Подключает органы к холсту.</summary>
    /// <param name="context">Контекст модуля: словари и настройки.</param>
    /// <param name="sheet">Холст.</param>
    /// <param name="fit">Кнопка «Вписать всё».</param>
    /// <param name="actual">Кнопка масштаба: она же показывает его словами.</param>
    /// <param name="grid">Выключатель сетки.</param>
    /// <param name="all">Что на холсте целиком; null — показывать нечего.</param>
    public SheetControls(
        IStudioContext context,
        SurfaceView sheet,
        AxButton fit,
        AxButton actual,
        AxToggleButton grid,
        Func<Rect?> all)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(fit);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(all);

        _context = context;
        _sheet = sheet;
        _fit = fit;
        _actual = actual;
        _grid = grid;
        _all = all;

        sheet.ShowGrid = ShowsGrid();
        grid.IsChecked = sheet.ShowGrid;

        sheet.PropertyChanged += OnSheetPropertyChanged;
        sheet.ActualThemeVariantChanged += OnThemeChanged;
        fit.Click += OnFit;
        actual.Click += OnActual;
        grid.IsCheckedChanged += OnGridToggled;
        context.Settings.Changed += OnSettingsChanged;

        CanvasLooks.Apply(sheet);
        Zoomed();
    }

    /// <summary>
    /// Показывает область целиком, но не крупнее ста процентов.
    /// </summary>
    /// <param name="bounds">Область в мировых координатах холста.</param>
    public void Frame(Rect bounds)
    {
        _sheet.FitToView(bounds);

        if (_sheet.ViewportZoom > FrameZoom)
        {
            _sheet.ZoomAt(FrameZoom, Middle(_sheet));
            _sheet.CenterOn(bounds);
        }
    }

    /// <summary>Показывает всё, что на холсте; показывать нечего — ничего не делает.</summary>
    public void FrameAll()
    {
        if (_all() is { } bounds)
            Frame(bounds);
    }

    /// <summary>Возвращает клавиатуру холсту.</summary>
    public void Back() => _sheet.Focus();

    /// <summary>
    /// Длина из темы, найденная от элемента вверх; ключ, которого нет, проверяют тесты ключей, а не холст.
    /// </summary>
    /// <param name="from">Откуда искать.</param>
    /// <param name="key">Ключ темы.</param>
    /// <remarks>
    /// Вид, ещё не поставленный в окно, видит только свои ресурсы: дорога вверх кончается на нём самом,
    /// и тема приложения до него не доходит. Строят вид раньше, чем ставят, поэтому ненайденное у
    /// элемента спрашивается у приложения.
    /// </remarks>
    public static double LengthOf(Control from, string key)
    {
        ArgumentNullException.ThrowIfNull(from);

        object? value = null;
        var found = from.TryFindResource(key, from.ActualThemeVariant, out value)
                    || Application.Current?.TryGetResource(key, from.ActualThemeVariant, out value) == true;

        return found && value is double length ? length : 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sheet.PropertyChanged -= OnSheetPropertyChanged;
        _sheet.ActualThemeVariantChanged -= OnThemeChanged;
        _fit.Click -= OnFit;
        _actual.Click -= OnActual;
        _grid.IsCheckedChanged -= OnGridToggled;
        _context.Settings.Changed -= OnSettingsChanged;
    }

    private void OnSheetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SurfaceView.ViewportZoomProperty)
            Zoomed();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => CanvasLooks.Apply(_sheet);

    private void OnFit(object? sender, RoutedEventArgs e)
    {
        FrameAll();
        Back();
    }

    private void OnActual(object? sender, RoutedEventArgs e)
    {
        _sheet.ZoomAt(FrameZoom, Middle(_sheet));
        Back();
    }

    private void OnGridToggled(object? sender, RoutedEventArgs e)
    {
        if (_grid.IsChecked is { } shown && shown != ShowsGrid())
            _context.Settings.Set(UiDesignerModule.GridKey, shown);
    }

    private void OnSettingsChanged(object? sender, string key)
    {
        if (key != UiDesignerModule.GridKey)
            return;

        // Настройка меняется и из окна настроек — не в потоке интерфейса, если её записал не он.
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
                return;

            _sheet.ShowGrid = ShowsGrid();
            _grid.IsChecked = _sheet.ShowGrid;
        });
    }

    private void Zoomed() =>
        _actual.Content = string.Format(
            CultureInfo.CurrentCulture, _context.Strings["board.zoom"], Math.Round(_sheet.ViewportZoom * 100));

    private bool ShowsGrid() => _context.Settings.Get<bool?>(UiDesignerModule.GridKey) ?? true;

    private static Point Middle(SurfaceView sheet) => new(sheet.Bounds.Width / 2, sheet.Bounds.Height / 2);
}
