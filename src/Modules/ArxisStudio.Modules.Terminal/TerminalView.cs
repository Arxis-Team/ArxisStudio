using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Selection;
using XMouseButton = XTerm.Input.MouseButton;
using XMouseEventType = XTerm.Input.MouseEventType;
using XSelectionMode = XTerm.Selection.SelectionMode;

namespace ArxisStudio.Modules.Terminal;

/// <summary>
/// Экран терминала: рисует сеанс и принимает клавиатуру с мышью.
/// </summary>
/// <remarks>
/// Это не шаблонный контрол, а рисующий: терминал — сетка ячеек, и рисовать
/// её ячейкой за ячейкой быстрее и вернее любого дерева контролов. Всё, что
/// сеанс знает об экране, лежит в эмуляторе; вид только читает его при каждом
/// кадре и переводит цвета в кисти, а клавиши — в байты.
/// <para>
/// Шрифт, фон, текст и выделение берутся из темы студии при постановке на
/// экран: терминал стоит среди панелей студии и не должен выглядеть чужим
/// окном. Палитра шестнадцати цветов при этом своя — Campbell, под которую
/// рисуют оболочки Windows.
/// </para>
/// </remarks>
public sealed class TerminalView : Control
{
    /// <summary>Кегль моноширинного шрифта.</summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<TerminalView, double>(nameof(FontSize), TerminalSettings.DefaultFontSize);

    /// <summary>Мигает ли курсор в сфокусированном терминале.</summary>
    public static readonly StyledProperty<bool> CursorBlinkProperty =
        AvaloniaProperty.Register<TerminalView, bool>(nameof(CursorBlink), true);

    /// <summary>Отступ от края до первой ячейки.</summary>
    public const double Inset = 6;

    /// <summary>Ширина полосы прокрутки справа.</summary>
    public const double ScrollBarWidth = 8;

    private static readonly FontFamily FallbackFont = new("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace");

    private readonly Dictionary<int, IImmutableBrush> _brushes = new();
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(530) };

    private TerminalSession? _session;
    private FontFamily _fontFamily = FallbackFont;
    private Typeface _regular;
    private Typeface _bold;
    private Typeface _italic;
    private Typeface _boldItalic;
    private double _cellWidth = 8;
    private double _cellHeight = 16;
    private int _columns = 80;
    private int _rows = 24;
    private Color _background = Color.FromRgb(0x1E, 0x1F, 0x22);
    private Color _foreground = Color.FromRgb(0xCC, 0xCC, 0xCC);
    private Color _selection = Color.FromRgb(0x2E, 0x43, 0x6E);
    private Color _thumb = Color.FromRgb(0x6F, 0x73, 0x7A);
    private CursorStyle _cursorStyle = CursorStyle.Block;
    private bool _blinkOn = true;
    private bool _selecting;

    /// <summary>Создаёт вид: он принимает фокус и показывает текстовый курсор.</summary>
    public TerminalView()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);

        _regular = new Typeface(_fontFamily);
        _bold = new Typeface(_fontFamily, FontStyle.Normal, FontWeight.Bold);
        _italic = new Typeface(_fontFamily, FontStyle.Italic);
        _boldItalic = new Typeface(_fontFamily, FontStyle.Italic, FontWeight.Bold);

        _blink.Tick += (_, _) =>
        {
            _blinkOn = !_blinkOn;
            InvalidateVisual();
        };
    }

    /// <inheritdoc cref="FontSizeProperty"/>
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <inheritdoc cref="CursorBlinkProperty"/>
    public bool CursorBlink
    {
        get => GetValue(CursorBlinkProperty);
        set => SetValue(CursorBlinkProperty, value);
    }

    /// <summary>Сеанс, который показывает вид; null — пустой экран.</summary>
    public TerminalSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value))
                return;

            if (_session is not null)
            {
                _session.Changed -= OnSessionChanged;
                _session.Exited -= OnSessionExited;
                _session.Terminal.CursorStyleChanged -= OnCursorStyleChanged;
                _session.Terminal.Scrolled -= OnScrolled;
            }

            _session = value;

            if (_session is not null)
            {
                _session.Changed += OnSessionChanged;
                _session.Exited += OnSessionExited;
                _session.Terminal.CursorStyleChanged += OnCursorStyleChanged;
                _session.Terminal.Scrolled += OnScrolled;
                ApplyTheme();
                _session.Resize(_columns, _rows);
            }

            InvalidateVisual();
        }
    }

    /// <summary>Размер ячейки в пикселях — по шрифту и кеглю.</summary>
    public Size CellSize => new(_cellWidth, _cellHeight);

    /// <summary>Ширина экрана в знаках — по нынешнему размеру вида.</summary>
    public int Columns => _columns;

    /// <summary>Высота экрана в строках — по нынешнему размеру вида.</summary>
    public int Rows => _rows;

    /// <summary>Есть ли выделенный текст.</summary>
    public bool HasSelection => _session?.Terminal.Selection.HasSelection == true;

    /// <summary>Копирует выделенное в буфер обмена; без выделения ничего не делает.</summary>
    public async Task CopyAsync()
    {
        if (_session is null || !_session.Terminal.Selection.HasSelection)
            return;

        var text = SelectedText(_session.Terminal.Selection);

        if (text.Length > 0 && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    /// <summary>Вставляет текст из буфера обмена в оболочку.</summary>
    public async Task PasteAsync()
    {
        if (_session is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        if (await clipboard.TryGetTextAsync() is { Length: > 0 } text)
            _session.Paste(text);
    }

    /// <summary>Выделяет весь экран вместе с историей.</summary>
    public void SelectAll()
    {
        _session?.Terminal.Selection.SelectAll();
        InvalidateVisual();
    }

    /// <summary>Снимает выделение.</summary>
    public void ClearSelection()
    {
        _session?.Terminal.Selection.ClearSelection();
        InvalidateVisual();
    }

    /// <summary>Очищает экран и историю — как <c>clear</c>, только без участия оболочки.</summary>
    public void ClearScreen()
    {
        _session?.Terminal.Clear();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ReadTheme();
        Remeasure();
        ApplyTheme();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _blink.Stop();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FontSizeProperty)
        {
            Remeasure();
        }
        else if (change.Property == CursorBlinkProperty)
        {
            RestartBlink();
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : (80 * _cellWidth) + (2 * Inset) + ScrollBarWidth;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : (24 * _cellHeight) + (2 * Inset);

        return new Size(width, height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Math.Max(2, (int)Math.Floor((finalSize.Width - (2 * Inset) - ScrollBarWidth) / _cellWidth));
        var rows = Math.Max(1, (int)Math.Floor((finalSize.Height - (2 * Inset)) / _cellHeight));

        if (columns != _columns || rows != _rows)
        {
            _columns = columns;
            _rows = rows;
            _session?.Resize(columns, rows);
        }

        return finalSize;
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);

        context.FillRectangle(Brush(Rgb(_background)), bounds);

        if (_session is null)
            return;

        var terminal = _session.Terminal;
        var buffer = terminal.Buffer;
        var top = buffer.YDisp;

        using var clip = context.PushClip(bounds);

        for (var row = 0; row < _rows; row++)
        {
            var absolute = top + row;

            if (absolute >= buffer.Lines.Length)
                break;

            if (buffer.Lines[absolute] is { } line)
                RenderLine(context, line, row, terminal.Colors, terminal.Selection, terminal.Options.DrawBoldTextInBrightColors);
        }

        RenderCursor(context, terminal);
        RenderScrollBar(context, buffer);
    }

    /// <inheritdoc/>
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        RestartBlink();
        SendFocus(true);
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _blink.Stop();
        _blinkOn = true;
        SendFocus(false);
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || _session is null)
            return;

        var modifiers = e.KeyModifiers;
        var control = modifiers.HasFlag(KeyModifiers.Control);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);

        // Выход с клавиатуры. Tab отсюда не уводит — он нужен оболочке для
        // дополнения имён, — и без отдельного сочетания человек, попавший
        // сюда клавишами, остался бы в терминале навсегда. Shift+Escape взят
        // у JetBrains: оболочкам он не нужен, а простой Escape нужен, и его
        // отбирать нельзя.
        if (shift && e.Key == Key.Escape)
        {
            TopLevel.GetTopLevel(this)?.FocusManager?.TryMoveFocus(NavigationDirection.Next);
            e.Handled = true;
            return;
        }

        // Сочетания студии — раньше оболочки: Ctrl+Shift+C/V и Shift+Insert —
        // договорённость всех терминалов, Ctrl+V — привычка Windows. Ctrl+C с
        // выделением копирует, без него — прерывает, как в Windows Terminal.
        if (control && shift && e.Key == Key.C)
        {
            _ = CopyAsync();
            e.Handled = true;
            return;
        }

        if ((control && e.Key == Key.V) || (shift && !control && e.Key == Key.Insert))
        {
            _ = PasteAsync();
            e.Handled = true;
            return;
        }

        if (control && !shift && e.Key == Key.C && _session.Terminal.Selection.HasSelection)
        {
            _ = CopyAsync();
            ClearSelection();
            e.Handled = true;
            return;
        }

        if (_session.SendKey(e.Key, modifiers, e.KeySymbol))
        {
            _blinkOn = true;
            e.Handled = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (e.Handled || _session is null || string.IsNullOrEmpty(e.Text))
            return;

        // Особые клавиши уже ушли кодами из OnKeyDown; их текстовое эхо, если
        // платформа его присылает, отправлять второй раз нельзя.
        if (e.Text is "\r" or "\n" or "\t" or "\b" or "")
            return;

        _session.SendText(e.Text);
        _blinkOn = true;
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Focus();

        if (_session is null)
            return;

        var point = e.GetCurrentPoint(this);
        var (x, y) = Cell(point.Position);

        if (Reporting(e.KeyModifiers))
        {
            Report(point.Properties.IsLeftButtonPressed ? XMouseButton.Left
                : point.Properties.IsRightButtonPressed ? XMouseButton.Right
                : XMouseButton.Middle, x, y, XMouseEventType.Down, e.KeyModifiers);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        var mode = e.ClickCount switch
        {
            >= 3 => XSelectionMode.Line,
            2 => XSelectionMode.Word,
            _ => XSelectionMode.Normal,
        };

        _session.Terminal.Selection.StartSelection(x, y, mode);
        _selecting = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_selecting || _session is null)
            return;

        var (x, y) = Cell(e.GetPosition(this));

        _session.Terminal.Selection.UpdateSelection(x, y);
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_session is null)
            return;

        var (x, y) = Cell(e.GetPosition(this));

        if (_selecting)
        {
            _selecting = false;
            e.Pointer.Capture(null);

            var selection = _session.Terminal.Selection;

            selection.EndSelection();

            // Щелчок без протяжки — не выделение, а постановка фокуса.
            if (selection.TryGetSelection(out var range) && range.StartX == range.EndX && range.StartY == range.EndY)
                selection.ClearSelection();

            InvalidateVisual();
            return;
        }

        if (Reporting(e.KeyModifiers))
            Report(XMouseButton.Left, x, y, XMouseEventType.Up, e.KeyModifiers);
    }

    /// <inheritdoc/>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (_session is null)
            return;

        var terminal = _session.Terminal;
        var notches = (int)Math.Round(e.Delta.Y);

        if (notches == 0)
            return;

        var (x, y) = Cell(e.GetPosition(this));

        if (Reporting(e.KeyModifiers))
        {
            Report(notches > 0 ? XMouseButton.WheelUp : XMouseButton.WheelDown, x, y,
                notches > 0 ? XMouseEventType.WheelUp : XMouseEventType.WheelDown, e.KeyModifiers);
            e.Handled = true;
            return;
        }

        // На альтернативном экране истории нет, и колесо там значит стрелки:
        // так листают man и less в любом терминале.
        if (terminal.IsAlternateBufferActive)
        {
            var key = notches > 0 ? Key.Up : Key.Down;

            for (var i = 0; i < Math.Abs(notches) * 3; i++)
                _session.SendKey(key, KeyModifiers.None);
        }
        else
        {
            terminal.ScrollLines(-notches * 3);
        }

        e.Handled = true;
        InvalidateVisual();
    }

    /// <summary>Подписывает вид для средств доступности: имя сеанса, а не «контрол».</summary>
    /// <param name="name">Как назвать.</param>
    public void Describe(string name) => Avalonia.Automation.AutomationProperties.SetName(this, name);

    private void RenderLine(DrawingContext context, BufferLine line, int row, ColorPalette colors, SelectionManager selection, bool boldIsBright)
    {
        var y = Inset + (row * _cellHeight);
        var width = Math.Min(_columns, line.Length);
        var x = 0;

        while (x < width)
        {
            var first = line[x];

            if (first.Width == 0)
            {
                x++;
                continue;
            }

            var attributes = first.Attributes;
            var selected = selection.IsCellSelected(x, row);
            var start = x;
            var text = new System.Text.StringBuilder();

            // Пробег — соседние ячейки одного оформления. Широкий знак идёт
            // отдельным пробегом: его глифа нет в моноширинном шрифте, и
            // сдвинуть соседей он не должен.
            do
            {
                text.Append(string.IsNullOrEmpty(line[x].Content) ? " " : line[x].Content);
                x += Math.Max(1, line[x].Width);
            }
            while (x < width
                   && first.Width == 1
                   && line[x].Width == 1
                   && line[x].Attributes.Equals(attributes)
                   && selection.IsCellSelected(x, row) == selected);

            var (foreground, background) = TerminalTheme.Resolve(attributes, colors, boldIsBright);
            var cells = x - start;
            var origin = new Point(Inset + (start * _cellWidth), y);
            var rect = new Rect(origin, new Size(cells * _cellWidth, _cellHeight));

            if (selected)
                context.FillRectangle(Brush(Rgb(_selection)), rect);
            else if (background != colors.Background)
                context.FillRectangle(Brush(background), rect);

            if (attributes.IsInvisible())
                continue;

            var brush = attributes.IsDim() ? Brush(foreground, 0.6) : Brush(foreground);
            var typeface = attributes.IsBold()
                ? (attributes.IsItalic() ? _boldItalic : _bold)
                : (attributes.IsItalic() ? _italic : _regular);

            var layout = new TextLayout(text.ToString(), typeface, FontSize, brush);

            layout.Draw(context, origin);

            if (attributes.IsUnderline())
            {
                var underline = y + _cellHeight - 1.5;

                context.DrawLine(new ImmutablePen(brush), new Point(rect.Left, underline), new Point(rect.Right, underline));
            }

            if (attributes.IsStrikethrough())
            {
                var middle = y + (_cellHeight / 2);

                context.DrawLine(new ImmutablePen(brush), new Point(rect.Left, middle), new Point(rect.Right, middle));
            }
        }
    }

    private void RenderCursor(DrawingContext context, XTerm.Terminal terminal)
    {
        if (!terminal.CursorVisible || _session?.IsRunning != true)
            return;

        var buffer = terminal.Buffer;
        var row = buffer.YBase + buffer.Y - buffer.YDisp;

        if (row < 0 || row >= _rows)
            return;

        var column = Math.Clamp(buffer.X, 0, Math.Max(0, _columns - 1));
        var origin = new Point(Inset + (column * _cellWidth), Inset + (row * _cellHeight));
        var cursor = Brush(terminal.Colors.Cursor);
        var focused = IsFocused;

        if (focused && !_blinkOn)
            return;

        switch (_cursorStyle)
        {
            case CursorStyle.Underline:
                context.FillRectangle(cursor, new Rect(origin.X, origin.Y + _cellHeight - 2, _cellWidth, 2));
                return;

            case CursorStyle.Bar:
                context.FillRectangle(cursor, new Rect(origin.X, origin.Y, 1.5, _cellHeight));
                return;
        }

        var rect = new Rect(origin, new Size(_cellWidth, _cellHeight));

        // Без фокуса курсор — рамка: место видно, а печатать сюда сейчас нельзя.
        if (!focused)
        {
            context.DrawRectangle(new ImmutablePen(cursor), rect.Deflate(0.5));
            return;
        }

        context.FillRectangle(cursor, rect);

        if (buffer.Lines[buffer.YBase + buffer.Y] is { } line && column < line.Length)
        {
            var cell = line[column];

            if (!string.IsNullOrEmpty(cell.Content) && cell.Content != " ")
                new TextLayout(cell.Content, _regular, FontSize, Brush(terminal.Colors.Background)).Draw(context, origin);
        }
    }

    private void RenderScrollBar(DrawingContext context, TerminalBuffer buffer)
    {
        var total = buffer.Lines.Length;

        if (total <= _rows)
            return;

        var track = Bounds.Height - (2 * Inset);
        var thumb = Math.Max(20, track * _rows / total);
        var travel = track - thumb;
        var top = Inset + (travel * buffer.YDisp / Math.Max(1, total - _rows));
        var left = Bounds.Width - ScrollBarWidth + 2;

        context.FillRectangle(Brush(Rgb(_thumb), 0.7), new Rect(left, top, ScrollBarWidth - 4, thumb), 2);
    }

    /// <summary>Ячейка под точкой; за краями — ближайшая.</summary>
    private (int X, int Y) Cell(Point point)
    {
        var x = (int)Math.Floor((point.X - Inset) / _cellWidth);
        var y = (int)Math.Floor((point.Y - Inset) / _cellHeight);

        return (Math.Clamp(x, 0, Math.Max(0, _columns - 1)), Math.Clamp(y, 0, Math.Max(0, _rows - 1)));
    }

    /// <summary>Программа просила отдавать ей мышь, и человек не удерживает Shift, чтобы выделять самому.</summary>
    private bool Reporting(KeyModifiers modifiers) =>
        _session is not null
        && _session.Terminal.MouseTrackingMode != XTerm.Input.MouseTrackingMode.None
        && !modifiers.HasFlag(KeyModifiers.Shift);

    private void Report(XMouseButton button, int x, int y, XMouseEventType type, KeyModifiers modifiers)
    {
        if (_session is null)
            return;

        var sequence = _session.Terminal.GenerateMouseEvent(button, x, y, type, KeyMap.Convert(modifiers));

        if (!string.IsNullOrEmpty(sequence))
            _session.SendText(sequence);
    }

    private void SendFocus(bool focused)
    {
        if (_session is null || !_session.Terminal.SendFocusEvents)
            return;

        var sequence = _session.Terminal.GenerateFocusEvent(focused);

        if (!string.IsNullOrEmpty(sequence))
            _session.SendText(sequence);
    }

    private static string SelectedText(SelectionManager selection)
    {
        var lines = selection.GetSelectionText().Split('\n');

        // Хвост строки — пустые ячейки экрана, а не пробелы, которые кто-то
        // набрал: в буфер обмена они не нужны.
        return string.Join('\n', lines.Select(line => line.TrimEnd(' ')));
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        _blinkOn = true;
        InvalidateVisual();
    }

    private void OnSessionExited(object? sender, int code) => InvalidateVisual();

    private void OnScrolled(object? sender, EventArgs e) => InvalidateVisual();

    private void OnCursorStyleChanged(object? sender, XTerm.Events.TerminalEvents.CursorStyleChangedEventArgs e)
    {
        _cursorStyle = e.Style;
        InvalidateVisual();
    }

    private void RestartBlink()
    {
        _blink.Stop();
        _blinkOn = true;

        if (IsFocused && CursorBlink)
            _blink.Start();
    }

    /// <summary>Берёт из темы студии шрифт и цвета; чего в теме нет — остаётся встроенным.</summary>
    private void ReadTheme()
    {
        if (this.TryFindResource("AxFontFamilyMono", ActualThemeVariant, out var font) && font is FontFamily family)
            _fontFamily = family;

        _regular = new Typeface(_fontFamily);
        _bold = new Typeface(_fontFamily, FontStyle.Normal, FontWeight.Bold);
        _italic = new Typeface(_fontFamily, FontStyle.Italic);
        _boldItalic = new Typeface(_fontFamily, FontStyle.Italic, FontWeight.Bold);

        _background = ThemeColor("AxBg1Color", _background);
        _foreground = ThemeColor("AxFgColor", _foreground);
        _selection = ThemeColor("AxSelColor", _selection);
        _thumb = ThemeColor("AxScrollThumbColor", _thumb);
        _brushes.Clear();
    }

    private Color ThemeColor(string key, Color fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is Color color ? color : fallback;

    /// <summary>Отдаёт эмулятору цвета студии: так и рисуется, и на вопрос «какой у тебя фон» отвечается одно.</summary>
    private void ApplyTheme() =>
        _session?.Terminal.Colors.ApplyTheme(TerminalTheme.Campbell(_background, _foreground, _selection));

    /// <summary>Меряет ячейку по самому широкому знаку моноширинного шрифта.</summary>
    private void Remeasure()
    {
        var probe = new TextLayout("W", _regular, FontSize, Brushes.White);

        if (probe.WidthIncludingTrailingWhitespace > 0)
            _cellWidth = probe.WidthIncludingTrailingWhitespace;

        if (probe.Height > 0)
            _cellHeight = Math.Ceiling(probe.Height);

        InvalidateMeasure();
        InvalidateVisual();
    }

    private static int Rgb(Color color) => (color.R << 16) | (color.G << 8) | color.B;

    private IImmutableBrush Brush(int rgb, double opacity = 1)
    {
        var key = opacity >= 1 ? rgb : rgb | (1 << 24);

        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = new ImmutableSolidColorBrush(TerminalTheme.ToColor(rgb), opacity);
            _brushes[key] = brush;
        }

        return brush;
    }
}
