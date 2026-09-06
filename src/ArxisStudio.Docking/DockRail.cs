using ArxisStudio.Controls;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArxisStudio.Docking;

/// <summary>Кнопка на рейке: чья группа и какая панель.</summary>
/// <param name="Group">Имя убранной группы.</param>
/// <param name="Item">Панель, которую эта кнопка называет.</param>
/// <remarks>
/// Кнопка на панель, а не на группу: группа из трёх вкладок даёт три кнопки, и
/// человек возвращает ту, которая ему нужна, а не ту, что была выбрана в
/// последний раз. Так же устроены рейки в Visual Studio и в Rider.
/// </remarks>
public sealed record DockRailItem(string Group, DockItem Item);

/// <summary>
/// Рейка вдоль края окна: кнопки панелей, убранных с этой стороны.
/// </summary>
/// <remarks>
/// Рейка — единственное место, откуда убранную панель можно вернуть: на экране
/// её нет, а в дереве есть только имя. Поэтому пустая рейка не показывается
/// вовсе, а непустая обязана называть свои кнопки — по подписи панели человек
/// её и находит.
/// <para>
/// Боковые рейки пишут подпись поперёк: слева снизу вверх, справа сверху вниз,
/// как в Visual Studio и Rider. Иначе кнопка была бы шириной в подпись, и рейка
/// съела бы ту самую полосу окна, ради которой панель убирали.
/// </para>
/// <para>
/// Своего текста у рейки нет и быть не может: движок докинга не знает о языках
/// студии. Подпись приходит от самой панели — <see cref="DockItem.Title"/>, — и
/// приходит привязкой: снятая однажды строка осталась бы на языке той минуты,
/// когда панель убрали.
/// </para>
/// </remarks>
public class DockRail : TemplatedControl
{
    /// <summary>
    /// Край окна, вдоль которого стоит рейка.
    /// </summary>
    /// <remarks>
    /// От неё зависит не только место, но и разворот: вдоль боковых кнопки
    /// идут столбиком и подпись у них повёрнута, вдоль нижней — строкой.
    /// </remarks>
    public static readonly StyledProperty<DockSide> SideProperty =
        AvaloniaProperty.Register<DockRail, DockSide>(nameof(Side));

    private readonly List<IDisposable> _bound = [];

    private StackPanel? _items;
    private IReadOnlyList<DockRailItem> _shown = [];

    static DockRail()
    {
        SideProperty.Changed.AddClassHandler<DockRail>((rail, _) => rail.Fill());

        // Заводится рейка пустой, а пустая рейка места не занимает. Не сделай
        // мы этого здесь, окно открывалось бы с тремя полосками по краям, за
        // которыми ничего нет: первое непустое Update приходит позже.
        IsVisibleProperty.OverrideDefaultValue<DockRail>(false);
    }

    /// <summary>Человек ткнул кнопку панели.</summary>
    /// <remarks>
    /// Как и всё в этом движке, рейка только просит: вернуть панель — правка
    /// дерева, а дерево принадлежит студии.
    /// </remarks>
    public event EventHandler<DockRailItem>? Chosen;

    /// <inheritdoc cref="SideProperty"/>
    public DockSide Side
    {
        get => GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    /// <summary>
    /// Раздаёт рейке кнопки; пустая рейка уходит с экрана.
    /// </summary>
    /// <param name="items">Панели убранных групп этой стороны.</param>
    /// <remarks>
    /// Пустая рейка не просто прячется, а перестаёт занимать место: полоса в
    /// два десятка пикселей вдоль трёх краёв — это ощутимая часть окна, и
    /// держать её ради того, чего нет, незачем.
    /// <para>
    /// Тот же список ничего не перестраивает. Спрашивают рейку на каждой правке
    /// дерева, а правок при тяге границы — десятки в секунду: пересобирай она
    /// кнопки каждый раз, человек тянул бы границу под мигающей рейкой, а
    /// привязки заводились бы и снимались на каждом кадре.
    /// </para>
    /// </remarks>
    public void Update(IReadOnlyList<DockRailItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (_shown.SequenceEqual(items))
            return;

        _shown = items;
        IsVisible = items.Count > 0;

        Fill();
    }

    /// <summary>
    /// Отпускает панели и снимает привязки.
    /// </summary>
    /// <remarks>
    /// Кнопка рейки держит <see cref="DockItem"/>, а тот — контрол расширения.
    /// Оставленная привязка пережила бы выгрузку плагина и не дала бы его
    /// контексту уйти — ровно та беда, от которой заведено снятие панелей по
    /// хозяину.
    /// </remarks>
    public void Release()
    {
        foreach (var binding in _bound)
            binding.Dispose();

        _bound.Clear();
        _items?.Children.Clear();
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnApplyTemplate(e);

        _items = e.NameScope.Find<StackPanel>("PART_Items");

        Fill();
    }

    /// <summary>Строит кнопки заново — по одной на панель.</summary>
    private void Fill()
    {
        Release();

        if (_items is null)
            return;

        var down = Side is DockSide.Left or DockSide.Right;

        _items.Orientation = down ? Orientation.Vertical : Orientation.Horizontal;

        foreach (var item in _shown)
            _items.Children.Add(Button(item, down));
    }

    /// <summary>
    /// Кнопка одной панели.
    /// </summary>
    /// <param name="item">Панель и её группа.</param>
    /// <param name="down">Рейка боковая: подпись идёт поперёк.</param>
    /// <remarks>
    /// Имя для средств доступности и подсказка ставятся отдельно и той же
    /// привязкой. Кнопка со сложным содержимым сама себя не называет: имя ей
    /// достаётся от содержимого, а у повёрнутого текста это имя его контрола.
    /// </remarks>
    private Control Button(DockRailItem item, bool down)
    {
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };

        _bound.Add(text.Bind(TextBlock.TextProperty, item.Item.GetObservable(DockItem.TitleProperty)));

        var button = new AxButton
        {
            Classes = { "rail" },
            Content = down
                ? new LayoutTransformControl
                {
                    // Слева читается снизу вверх, справа сверху вниз — так
                    // подпись поворачивается вслед за краем, а не против него.
                    LayoutTransform = new RotateTransform(Side == DockSide.Left ? -90 : 90),
                    Child = text,
                }
                : text,
        };

        _bound.Add(button.Bind(AutomationProperties.NameProperty, item.Item.GetObservable(DockItem.TitleProperty)));
        _bound.Add(button.Bind(ToolTip.TipProperty, item.Item.GetObservable(DockItem.TitleProperty)));

        button.Click += (_, _) => Chosen?.Invoke(this, item);

        return button;
    }
}
