using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Shell.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ArxisStudio.Palette;

/// <summary>
/// Палитра на экране: карточка поиска поверх окна.
/// </summary>
/// <remarks>
/// Живёт в слое оверлеев окна, а не отдельным окном. Отдельное окно у палитры
/// было бы вторым окном студии на секунду: его пришлось бы позиционировать,
/// поднимать, отбирать у него фокус и следить, чтобы оно не осталось висеть,
/// когда главное свернули. Слой оверлеев делает это сам.
/// <para>
/// Стрелки, Enter и Esc перехватываются туннельно и принадлежат палитре, а не
/// её полю ввода: набирают в поле, а ходят по списку.
/// </para>
/// </remarks>
public sealed class PaletteOverlay
{
    private readonly Func<string, bool> _invoke;

    private IReadOnlyList<PaletteEntry> _all = [];
    private IReadOnlyList<PaletteEntry> _shown = [];
    private AxQuickSearch? _card;
    private Panel? _scrim;
    private Window? _owner;

    /// <summary>Заводит палитру над реестром команд.</summary>
    /// <param name="invoke">Кому передать имя выбранной команды.</param>
    public PaletteOverlay(Func<string, bool> invoke)
    {
        ArgumentNullException.ThrowIfNull(invoke);

        _invoke = invoke;
    }

    /// <summary>Палитра сейчас на экране.</summary>
    public bool IsOpen => _scrim is not null;

    /// <summary>
    /// Показывает палитру над окном.
    /// </summary>
    /// <param name="owner">Окно студии.</param>
    /// <param name="entries">Что показывать.</param>
    /// <remarks>
    /// Повторный вызов при открытой палитре её закрывает: то же сочетание,
    /// которым её открыли, обязано её и убрать — иначе человек, нажавший дважды,
    /// остаётся с открытым окном и вопросом, что он сделал не так.
    /// </remarks>
    public void Show(Window owner, IReadOnlyList<PaletteEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(entries);

        if (IsOpen)
        {
            Close();

            return;
        }

        if (OverlayLayer.GetOverlayLayer(owner) is not { } layer)
            return;

        _owner = owner;
        _all = entries;

        _card = new AxQuickSearch
        {
            PlaceholderText = Localizer.Instance["palette.hint"],
            Hints = Localizer.Instance["palette.keys"],
            ItemTemplate = Row(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 80, 0, 0),
            Width = 520,
        };

        _card.PropertyChanged += OnCardChanged;

        // Затемнения нет нарочно: палитра открывается на секунду, и гашение
        // всего окна ради неё мигало бы сильнее, чем сама карточка. Подложка
        // нужна только затем, чтобы щелчок мимо карточки закрывал палитру.
        _scrim = new Panel { Background = Brushes.Transparent, Children = { _card } };
        _scrim.PointerPressed += OnScrimPressed;
        _scrim.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);

        layer.Children.Add(_scrim);

        // Слой оверлеев — канва, и детей она не растягивает: подложка без
        // размера выходит ровно с карточку, карточка прижимается к левому краю,
        // а мимо неё щёлкнуть становится некуда. Размер поэтому берётся у окна
        // и обновляется вместе с ним.
        Stretch();

        owner.SizeChanged += OnOwnerResized;

        Refresh();

        owner.Closed += OnOwnerClosed;

        // Раскладку приходится пройти вслух: до первого прохода у карточки нет
        // ни поля ввода, ни списка — шаблон ещё не применён, и искать в ней
        // нечего. Без этого каретка не встаёт никуда, а вместе с ней не доходит
        // до палитры и туннельный путь клавиши: туннель идёт от корня к
        // сфокусированному, а сфокусированного нет.
        owner.UpdateLayout();

        // Каретка идёт в поле ввода: палитру открывают, чтобы набирать.
        _card.GetVisualDescendants().OfType<AxTextBox>().FirstOrDefault()?.Focus();
    }

    /// <summary>Убирает палитру с экрана.</summary>
    public void Close()
    {
        if (_scrim is null || _owner is null)
            return;

        if (OverlayLayer.GetOverlayLayer(_owner) is { } layer)
            layer.Children.Remove(_scrim);

        _scrim.PointerPressed -= OnScrimPressed;
        _scrim.RemoveHandler(InputElement.KeyDownEvent, OnKey);
        _owner.Closed -= OnOwnerClosed;
        _owner.SizeChanged -= OnOwnerResized;

        if (_card is not null)
            _card.PropertyChanged -= OnCardChanged;

        _scrim = null;
        _card = null;
        _owner = null;
        _shown = [];
    }

    /// <summary>Строка списка: значок и название слева, сочетание справа.</summary>
    /// <remarks>
    /// Место под значок держит каждая строка, есть у команды значок или нет, — как колонка
    /// значков в меню: иначе названия стояли бы лесенкой, и глаз, идущий по списку сверху вниз,
    /// спотыкался бы на каждой строке без значка.
    /// </remarks>
    private static IDataTemplate Row() => new FuncDataTemplate<PaletteEntry>(
        (_, _) =>
        {
            var icon = new AxIcon
            {
                VerticalAlignment = VerticalAlignment.Center,
                [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxGapIconTextThickness"),
            };
            var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var gesture = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("AxFg3Brush"),
                [!TextBlock.FontSizeProperty] = new DynamicResourceExtension("AxFontSizeSmall"),
            };

            icon.Bind(AxIcon.DataProperty, new Avalonia.Data.Binding(nameof(PaletteEntry.Icon)));
            title.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(PaletteEntry.Title)));
            gesture.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(PaletteEntry.Gesture)));

            DockPanel.SetDock(icon, Avalonia.Controls.Dock.Left);
            DockPanel.SetDock(gesture, Avalonia.Controls.Dock.Right);

            return new DockPanel { Children = { icon, gesture, title } };
        },
        supportsRecycling: true);

    /// <summary>Перебирает список по набранному и держит выбор на первой строке.</summary>
    private void Refresh()
    {
        if (_card is null)
            return;

        _shown = CommandPalette.Match(_all, _card.Text);

        _card.ItemsSource = _shown;
        _card.SelectedItem = _shown.Count > 0 ? _shown[0] : null;
    }

    private void OnCardChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == AxQuickSearch.TextProperty)
            Refresh();
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        // Щелчок по самой карточке палитру не закрывает: закрывает только мимо.
        if (ReferenceEquals(e.Source, _scrim))
            Close();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                break;

            case Key.Enter:
                e.Handled = true;
                Run();
                break;

            case Key.Down:
                e.Handled = true;
                Step(1);
                break;

            case Key.Up:
                e.Handled = true;
                Step(-1);
                break;
        }
    }

    /// <summary>Двигает выбор по кругу.</summary>
    private void Step(int by)
    {
        if (_card is null || _shown.Count == 0)
            return;

        var shown = _shown.ToList();
        var at = _card.SelectedItem is PaletteEntry chosen ? shown.IndexOf(chosen) : -1;

        _card.SelectedItem = shown[((at + by) % shown.Count + shown.Count) % shown.Count];
    }

    /// <summary>
    /// Выполняет выбранное.
    /// </summary>
    /// <remarks>
    /// Палитра закрывается до вызова, а не после: команда может открыть своё
    /// окно или увести каретку, и карточка, оставшаяся поверх, оказалась бы
    /// поверх её же результата.
    /// </remarks>
    private void Run()
    {
        if (_card?.SelectedItem is not PaletteEntry chosen)
            return;

        Close();

        _invoke(chosen.CommandId);
    }

    private void OnOwnerClosed(object? sender, EventArgs e) => Close();

    private void OnOwnerResized(object? sender, SizeChangedEventArgs e) => Stretch();

    /// <summary>Растягивает подложку на всё окно.</summary>
    private void Stretch()
    {
        if (_scrim is null || _owner is null)
            return;

        _scrim.Width = _owner.ClientSize.Width;
        _scrim.Height = _owner.ClientSize.Height;
    }
}
