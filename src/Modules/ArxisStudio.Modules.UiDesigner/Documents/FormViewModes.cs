using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Controls;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>Что показывает холст форм: холст, XAML или оба.</summary>
internal enum FormViewMode
{
    /// <summary>Холст.</summary>
    Design,

    /// <summary>XAML.</summary>
    Xaml,

    /// <summary>Холст и XAML под ним.</summary>
    Split,
}

/// <summary>
/// Вид холста форм — дизайн, XAML или разделение — у вкладки формы и у доски: сегменты на полосе, строки
/// тела и доли разделения.
/// </summary>
/// <remarks>
/// <para>
/// Выбранный вид — настройка <see cref="UiDesignerModule.ViewKey"/>: следующая вкладка и доска открываются
/// так, как работали в прошлый раз. Доли разделения помнит вид: ушли в один холст и вернулись — граница
/// там, где её оставили.
/// </para>
/// <para>
/// Тело — сетка из трёх строк: холст, граница, XAML. Спрятанная часть отдаёт строку целиком, а граница
/// стоит, только когда ей есть что разделять.
/// </para>
/// </remarks>
internal sealed class FormViewModes : IDisposable
{
    private readonly IStudioContext _context;
    private readonly AxSegmentedControl _switch;
    private readonly Grid _body;
    private readonly Control _sheet;
    private readonly Control _split;
    private readonly Control _code;
    private readonly Control _sheetCaret;
    private readonly Control _codeCaret;
    private GridLength? _sheetShare;
    private GridLength? _codeShare;
    private bool _applying;

    /// <summary>Ставит вид по настройке и слушает сегменты.</summary>
    /// <param name="context">Контекст модуля: настройка вида.</param>
    /// <param name="modes">Сегменты «Дизайн», «XAML», «Разделение» — в этом порядке.</param>
    /// <param name="body">Сетка тела: холст, граница, XAML.</param>
    /// <param name="sheet">Холст — в первой строке.</param>
    /// <param name="split">Граница — во второй.</param>
    /// <param name="code">XAML — в третьей.</param>
    /// <param name="sheetCaret">Кому достаётся клавиатура, когда виден холст; по умолчанию — <paramref name="sheet"/>.</param>
    /// <param name="codeCaret">Кому — в одном XAML; по умолчанию — <paramref name="code"/>.</param>
    /// <remarks>
    /// Строка и та, кому достаётся клавиатура, бывают разными: у доски в строке холста стоят ещё надписи
    /// состояний и слой тяги, а над XAML — имя формы.
    /// </remarks>
    public FormViewModes(
        IStudioContext context,
        AxSegmentedControl modes,
        Grid body,
        Control sheet,
        Control split,
        Control code,
        Control? sheetCaret = null,
        Control? codeCaret = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(modes);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(code);

        _context = context;
        _switch = modes;
        _body = body;
        _sheet = sheet;
        _split = split;
        _code = code;
        _sheetCaret = sheetCaret ?? sheet;
        _codeCaret = codeCaret ?? code;

        modes.SelectionChanged += OnChosen;
        Apply(ModeOf(context.Settings.Get<string>(UiDesignerModule.ViewKey)));
    }

    /// <summary>Вид сейчас.</summary>
    public FormViewMode Mode { get; private set; }

    /// <summary>Кому достаётся клавиатура в этом виде: в одном XAML — тексту, иначе — холсту.</summary>
    public Control Caret => Mode == FormViewMode.Xaml ? _codeCaret : _sheetCaret;

    /// <summary>Вид сменился — выбором на полосе или тем, кто его показал.</summary>
    public event EventHandler? Changed;

    /// <summary>Вид по значению настройки; незнакомое — разделение.</summary>
    /// <param name="text">Значение <see cref="UiDesignerModule.ViewKey"/>.</param>
    public static FormViewMode ModeOf(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "design" => FormViewMode.Design,
        "xaml" => FormViewMode.Xaml,
        _ => FormViewMode.Split,
    };

    /// <summary>Значение настройки для вида.</summary>
    /// <param name="mode">Вид.</param>
    public static string NameOf(FormViewMode mode) => mode switch
    {
        FormViewMode.Design => "design",
        FormViewMode.Xaml => "xaml",
        _ => "split",
    };

    /// <summary>Показывает вид — не трогая настройку: так вид меняет тот, кто ведёт к месту в XAML.</summary>
    /// <param name="mode">Вид.</param>
    public void Apply(FormViewMode mode)
    {
        var rows = _body.RowDefinitions;

        if (_sheetShare is null || (Mode == FormViewMode.Split && _split.IsVisible))
        {
            _sheetShare = rows[0].Height;
            _codeShare = rows[2].Height;
        }

        Mode = mode;

        var sheet = mode != FormViewMode.Xaml;
        var code = mode != FormViewMode.Design;
        var whole = new GridLength(1, GridUnitType.Star);

        _sheet.IsVisible = sheet;
        _code.IsVisible = code;
        _split.IsVisible = sheet && code;

        rows[0].Height = !sheet ? new GridLength(0) : code ? _sheetShare.Value : whole;
        rows[2].Height = !code ? new GridLength(0) : sheet ? _codeShare!.Value : whole;

        _applying = true;

        try
        {
            _switch.SelectedIndex = (int)mode;
        }
        finally
        {
            _applying = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Dispose() => _switch.SelectionChanged -= OnChosen;

    /// <summary>Вид выбрали на полосе: он же — вид следующих, а клавиатура — тому, что видно.</summary>
    private void OnChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (_applying || _switch.SelectedIndex is < 0 or > (int)FormViewMode.Split)
            return;

        var mode = (FormViewMode)_switch.SelectedIndex;

        if (mode == Mode)
            return;

        Apply(mode);
        _context.Settings.Set(UiDesignerModule.ViewKey, NameOf(mode));
        Caret.Focus();
    }
}
