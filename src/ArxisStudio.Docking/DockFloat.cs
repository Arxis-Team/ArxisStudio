using ArxisStudio.Controls;
using ArxisStudio.Icons;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArxisStudio.Docking;

/// <summary>
/// Оторванное окно: своё дерево доков, а вместо полосы заголовка — полоса
/// вкладок.
/// </summary>
/// <remarks>
/// Живые панели у него общие с главным окном — тот же
/// <see cref="DockView.Items"/>. Контрол не копируется и не строится заново:
/// у контрола Avalonia ровно один родитель, и панель просто переезжает вместе с
/// именем, сохраняя прокрутку, выделение и всё, что помнит о себе сама.
/// <para>
/// Заголовок окна — подпись показанной вкладки: другого имени у окна с одной
/// панелью нет, а «ArxisStudio» в третий раз человеку ничего не говорит.
/// Своего текста у окна при этом нет — подпись приходит из панели.
/// </para>
/// <para>
/// Отдельной полосы заголовка у окна нет: она стояла бы пустой поверх полосы
/// вкладок и съедала бы четверть невысокого окна ради двух кнопок. Кнопки стоят
/// в правом краю полосы вкладок, за её пустое место окно двигают, а двойным
/// щелчком по нему разворачивают — так же это устроено и у Unity.
/// </para>
/// <para>
/// Кнопок две, и это кнопки не окна, а панели: «вернуть в главное окно» и
/// «скрыть». Так же устроена шапка плавающего окна инструментов в Rider.
/// Обычных кнопок окна здесь нет ни одной — окно при студии со всех сторон
/// палитра, а у палитры в Windows узкая шапка без «свернуть» и «развернуть».
/// </para>
/// </remarks>
public class DockFloat : AxWindow
{
    /// <summary>
    /// Ширина, с которой окно заводится, пока его не двигали и не тянули.
    /// </summary>
    /// <remarks>
    /// Тем же размером обещает окно и <see cref="DockGhost"/> под курсором:
    /// названный дважды, размер разошёлся бы, и обещание перестало бы совпадать
    /// с тем, что человек получит.
    /// </remarks>
    public const double DefaultWidth = 420;

    /// <inheritdoc cref="DefaultWidth"/>
    public const double DefaultHeight = 320;

    private AxButton? _dock;
    private AxButton? _hide;

    /// <summary>Заводит окно с деревом внутри.</summary>
    public DockFloat()
    {
        Width = DefaultWidth;
        Height = DefaultHeight;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Кнопки «скрыть» в шапке группы у этого дерева нет: «скрыть» стоит в
        // шапке самого окна и убирает всё, что в нём лежит. Две кнопки рядом,
        // делающие одно, — не выбор для человека, а недосмотр.
        //
        // Кнопки окна не ставятся вовсе. Окно при студии со всех сторон
        // палитра: своё место оно не заводит ни в панели задач, ни в Alt+Tab,
        // лежит над хозяйкой и уходит вместе с ней, — а у палитры в Windows
        // узкая шапка без «свернуть» и «развернуть». Сворачиваться ему и
        // некуда: свёрнутое, оно исчезает без следа, и найти его человеку
        // нечем. Разворот остаётся жестом — двойным щелчком по шапке, как у
        // всякого окна Windows; жест сам себе обратный, и тупика в нём нет.
        View = new DockView { Actions = Buttons, Hideable = false };

        // Своей полосы заголовка у окна нет: полоса вкладок и есть его
        // заголовок, а кнопки панели стоят в её правом краю. Отдельная полоса
        // поверх неё стояла бы пустой и съедала бы четверть невысокого окна
        // ради двух кнопок — так же это устроено и у Unity.
        Content = View;

        // Заголовок окна идёт за выбранной вкладкой: она в нём и показана.
        View.Chosen += (_, _) => Retitle();
        View.Grabbed += (_, e) => Grab(e);

        // Подписи приходят из студии и меняются с языком. Ставятся они кодом, а
        // не привязкой: кнопки строятся заново на каждой перекладке дерева, и
        // привязка от каждой брошенной копии осталась бы висеть на виде.
        View.PropertyChanged += (_, e) =>
        {
            if (e.Property == DockView.DockTitleProperty || e.Property == DockView.HideTitleProperty)
                Describe();
        };
    }

    /// <summary>
    /// Человек попросил убрать панели этого окна с глаз.
    /// </summary>
    /// <remarks>
    /// Как и всюду в этом движке, окно только просит: скрытая панель числится в
    /// раскладке студии, и возвращает её оттуда же меню. Что значит «скрыть»,
    /// движок докинга не знает — он знает лишь имена в дереве.
    /// <para>
    /// Вернуть себя окно, наоборот, умеет само: закрытие — это и есть возврат,
    /// панели закрытого окна уходят домой. Просить об этом некого.
    /// </para>
    /// </remarks>
    public event EventHandler? Hiding;

    /// <summary>Дерево этого окна.</summary>
    public DockView View { get; }

    /// <summary>Место и размер окна — в том виде, в каком они лягут в файл.</summary>
    /// <remarks>
    /// Точка экрана, а не окна-владельца: у оторванного окна владельца нет, и
    /// человек волен унести его на второй монитор.
    /// </remarks>
    public DockWindow Snapshot() => new()
    {
        Root = View.Root ?? new DockGroup { Id = "float" },
        X = Position.X,
        Y = Position.Y,
        Width = Width,
        Height = Height,
    };

    /// <summary>
    /// Ставит окно туда и такого размера, как записано.
    /// </summary>
    /// <param name="window">Запись из файла раскладки.</param>
    /// <remarks>
    /// Место сверяется с мониторами, которые есть сейчас. Записано оно было при
    /// той раскладке экранов, какая была тогда: отключили второй монитор,
    /// сменили его разрешение, принесли ноутбук домой — и окно возвращается
    /// туда, где смотреть его некому. Найти его после этого нечем: кнопки в
    /// панели задач у окна при студии нет.
    /// </remarks>
    public void Restore(DockWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        View.Root = window.Root;
        Width = window.Width;
        Height = window.Height;
        Position = Landed(
            new PixelRect((int)window.X, (int)window.Y, (int)window.Width, (int)window.Height),
            [.. Screens.All.Select(screen => screen.Bounds)],
            Screens.Primary?.WorkingArea);

        Retitle();
    }

    /// <summary>
    /// Место окна, которое видно хотя бы на одном мониторе.
    /// </summary>
    /// <param name="wanted">Прямоугольник, записанный в раскладке.</param>
    /// <param name="screens">Мониторы, какие есть сейчас.</param>
    /// <param name="fallback">Рабочая область основного монитора; null — некуда возвращать.</param>
    /// <returns>Записанное место или новое, если записанное потерялось.</returns>
    /// <remarks>
    /// Достаточно пересечения, а не полного вхождения: окно, наполовину
    /// свешенное за край, человек так и оставил — двигать его значит спорить с
    /// ним о том, где ему удобно. Двигаем потерявшееся: то, чей прямоугольник
    /// не задевает ни одного монитора.
    /// <para>
    /// Отдельная функция, потому что решение здесь — про числа, а не про окна:
    /// проверяется она без единого монитора и без единого окна.
    /// </para>
    /// </remarks>
    public static PixelPoint Landed(PixelRect wanted, IReadOnlyList<PixelRect> screens, PixelRect? fallback)
    {
        ArgumentNullException.ThrowIfNull(screens);

        if (fallback is not { } home || screens.Any(screen => screen.Intersects(wanted)))
            return wanted.Position;

        // По центру основного монитора: окно, потерявшее свой, человек ищет
        // глазами там же, где ищет всё остальное.
        return new PixelPoint(
            home.X + Math.Max(0, (home.Width - wanted.Width) / 2),
            home.Y + Math.Max(0, (home.Height - wanted.Height) / 2));
    }

    /// <summary>
    /// Двигает окно за пустое место шапки, двойным щелчком разворачивает.
    /// </summary>
    /// <remarks>
    /// То же, что делает <c>AxTitleBar</c> у прочих окон: своей полосы
    /// заголовка здесь нет, и её работу берёт полоса вкладок.
    /// <para>
    /// Разворот у этого окна остался только жестом — кнопки в шапке палитры
    /// нет. Жест сам себе обратный: второй двойной щелчок возвращает прежний
    /// размер, и развёрнутое окно тупиком не становится.
    /// </para>
    /// </remarks>
    private void Grab(PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

            e.Handled = true;

            return;
        }

        BeginMoveDrag(e);
    }

    /// <summary>
    /// Кнопки панели в правом краю шапки: вернуть в студию и скрыть.
    /// </summary>
    /// <remarks>
    /// Обе — про панель, а не про окно, и обе одноклеточные, как кнопка уборки
    /// в шапке пристыкованной группы: рядом с ними стоят вкладки, и кнопка
    /// размером с кнопку окна выбивалась бы из строя.
    /// <para>
    /// Значки взяты из набора студии. «Вернуть» — рамка, в которую возвращается
    /// окно: своего значка «в док» в наборе нет, а состав набора — дело
    /// дизайн-проекта, не наше. «Скрыть» — минус, как в Rider.
    /// </para>
    /// </remarks>
    private Control Buttons()
    {
        _dock = Button(AxIcons.WindowRestore, Close);
        _hide = Button(AxIcons.Minus, () => Hiding?.Invoke(this, EventArgs.Empty));

        Describe();

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _dock, _hide },
        };
    }

    /// <summary>Одна кнопка шапки: значок и то, что она делает.</summary>
    private static AxButton Button(Geometry icon, Action act)
    {
        var button = new AxButton
        {
            Classes = { "icon" },
            Content = new AxIcon { Classes = { "small" }, Data = icon },
        };

        button.Click += (_, _) => act();

        return button;
    }

    /// <summary>
    /// Подписывает кнопки тем, что они сделают.
    /// </summary>
    /// <remarks>
    /// Подпись у обеих — и подсказка, и имя для средств доступности: на кнопке
    /// значок 12×12, и узнать о ней больше неоткуда. Правило записей 72 и 98.
    /// </remarks>
    private void Describe()
    {
        if (_dock is not null)
        {
            ToolTip.SetTip(_dock, View.DockTitle);
            AutomationProperties.SetName(_dock, View.DockTitle ?? string.Empty);
        }

        if (_hide is not null)
        {
            ToolTip.SetTip(_hide, View.HideTitle);
            AutomationProperties.SetName(_hide, View.HideTitle ?? string.Empty);
        }
    }

    /// <summary>Берёт заголовок у показанной вкладки.</summary>
    public void Retitle()
    {
        if (View.Root is not { } root)
            return;

        var shown = root.Groups().Select(group => group.Selected).FirstOrDefault(item => item is not null);

        Title = View.Items?.Find(shown)?.Title ?? Title;
    }
}
