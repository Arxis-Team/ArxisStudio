using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Shell;

/// <summary>
/// Ряд из двух частей: ведущая слева занимает остаток, хвост стоит у правого края, а когда
/// рядом с ведущей ему не хватает места — переносится под неё.
/// </summary>
/// <remarks>
/// <para>
/// Заведён ради шапки Welcome: поиск слева, кнопки справа. Сетка с колонкой под кнопки по их
/// ширине держала это, пока кегль был обычным. При двойном кнопки забирали всю ширину, поле
/// поиска сжималось в ноль, а последняя кнопка уходила за край окна. Переносом решает ширина
/// частей, а не порог: порог в точках не знает, во что вырастут подписи.
/// </para>
/// <para>
/// Помещается ли хвост, решают естественные ширины — то, что части просят без ограничения:
/// у поля это подсказка с лупой, у кнопок — вся их строка. Ведущую ниже естественной ряд не
/// сжимает: поле, в котором не читается даже подсказка, хуже перенесённых кнопок.
/// </para>
/// <para>
/// Ведущая растягивается не шире своего <c>MaxWidth</c> и остаётся у левого края. Слот шире
/// предела Avalonia ставит её по середине, поэтому слот ей дают ровно по её ширине.
/// </para>
/// <para>
/// Перенесённый хвост получает всю ширину ряда. Не помещается он и в неё — переносить дальше
/// должен он сам: кнопки для этого стоят в <c>WrapPanel</c>, а не в стопке.
/// </para>
/// <para>
/// С <see cref="LeadWidth"/> ряд становится строкой формы: ведущая — колонка подписей этой ширины,
/// хвост встаёт сразу за ней, а не у правого края. Так стоят поля у Unity и в формах Rider:
/// контролы страницы — одной линией возле подписей, а не у края широкого окна, куда глазу
/// идти через пустоту. Переносит ряд по тому же правилу — не хватило места хвосту.
/// </para>
/// </remarks>
public sealed class WrapRow : Panel
{
    /// <summary>Зазор между частями — и в ряд, и при переносе.</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<WrapRow, double>(nameof(Spacing));

    /// <summary>Ширина колонки ведущей; не задана — ведущая берёт остаток ряда.</summary>
    public static readonly StyledProperty<double> LeadWidthProperty =
        AvaloniaProperty.Register<WrapRow, double>(nameof(LeadWidth), double.NaN);

    static WrapRow() => AffectsMeasure<WrapRow>(SpacingProperty, LeadWidthProperty);

    /// <summary>Зазор между частями — и в ряд, и при переносе.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Ширина колонки ведущей; не задана — ведущая берёт остаток ряда, а хвост стоит у правого края.
    /// </summary>
    /// <remarks>
    /// Ведущая получает ровно эту ширину и переносит в ней свой текст, а хвост встаёт за колонкой.
    /// Колонка одна на все ряды, где её назвали одним ключом темы, — отсюда и выравнивание: ширина,
    /// снятая с самой подписи, у каждого ряда была бы своя.
    /// </remarks>
    public double LeadWidth
    {
        get => GetValue(LeadWidthProperty);
        set => SetValue(LeadWidthProperty, value);
    }

    /// <summary>Колонка ведущей задана.</summary>
    private bool IsForm => !double.IsNaN(LeadWidth) && LeadWidth >= 0;

    /// <summary>Стоит ли хвост под ведущей после последнего замера.</summary>
    public bool IsWrapped { get; private set; }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var (lead, trail) = Parts();

        if (lead is null)
            return Wrap(false, default);

        if (trail is null)
        {
            lead.Measure(availableSize);
            return Wrap(false, lead.DesiredSize);
        }

        // Колонка задана — ведущая меряется в неё, а не без предела: подпись длиннее колонки
        // переносится в ней, и ширину строки решает колонка, а не текст.
        lead.Measure(IsForm ? new Size(LeadWidth, double.PositiveInfinity) : Size.Infinity);
        trail.Measure(Size.Infinity);

        var leadWidth = IsForm ? LeadWidth : lead.DesiredSize.Width;
        var trailWidth = trail.DesiredSize.Width;
        var row = leadWidth + Spacing + trailWidth;

        if (row <= availableSize.Width)
        {
            if (!IsForm)
                lead.Measure(new Size(availableSize.Width - trailWidth - Spacing, availableSize.Height));

            return Wrap(false, new Size(row, Math.Max(lead.DesiredSize.Height, trail.DesiredSize.Height)));
        }

        var column = new Size(availableSize.Width, double.PositiveInfinity);

        lead.Measure(column);
        trail.Measure(column);

        return Wrap(true, new Size(
            Math.Max(lead.DesiredSize.Width, trail.DesiredSize.Width),
            lead.DesiredSize.Height + Spacing + trail.DesiredSize.Height));
    }

    /// <summary>Запоминает решение замера и отдаёт замер.</summary>
    /// <remarks>
    /// Перемена решения просит раскладку заново, даже если родитель отведёт ряду прежний
    /// прямоугольник: раскладка по прежнему месту решает, стоять ли хвосту справа или снизу, и
    /// пропущенная, она оставила бы его там, где он стоял до замера.
    /// </remarks>
    private Size Wrap(bool wrapped, Size desired)
    {
        if (wrapped != IsWrapped)
        {
            IsWrapped = wrapped;
            InvalidateArrange();
        }

        return desired;
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var (lead, trail) = Parts();

        if (lead is null)
            return finalSize;

        if (trail is null)
        {
            lead.Arrange(new Rect(0, 0, Math.Min(lead.MaxWidth, finalSize.Width), finalSize.Height));
            return finalSize;
        }

        if (!IsWrapped && IsForm)
        {
            // Хвост — сразу за колонкой, и места ему — весь остаток: поле, которое тянется,
            // дотянется до края, а контрол своей ширины встанет у колонки.
            var column = Math.Min(lead.MaxWidth, LeadWidth);
            var start = LeadWidth + Spacing;

            lead.Arrange(new Rect(0, 0, column, finalSize.Height));
            trail.Arrange(new Rect(start, 0, Math.Max(trail.DesiredSize.Width, finalSize.Width - start), finalSize.Height));

            return finalSize;
        }

        if (!IsWrapped)
        {
            var trailWidth = trail.DesiredSize.Width;
            var room = Math.Max(0, finalSize.Width - trailWidth - Spacing);

            lead.Arrange(new Rect(0, 0, Math.Min(lead.MaxWidth, room), finalSize.Height));
            trail.Arrange(new Rect(finalSize.Width - trailWidth, 0, trailWidth, finalSize.Height));

            return finalSize;
        }

        var leadHeight = lead.DesiredSize.Height;

        lead.Arrange(new Rect(0, 0, Math.Min(lead.MaxWidth, finalSize.Width), leadHeight));
        trail.Arrange(new Rect(0, leadHeight + Spacing, finalSize.Width, trail.DesiredSize.Height));

        return finalSize;
    }

    /// <summary>Ведущая и хвост — первые два видимых ребёнка.</summary>
    /// <exception cref="InvalidOperationException">Видимых детей больше двух.</exception>
    private (Control? Lead, Control? Trail) Parts()
    {
        Control? lead = null;
        Control? trail = null;

        foreach (var child in Children)
        {
            if (!child.IsVisible)
                continue;

            if (lead is null)
                lead = child;
            else if (trail is null)
                trail = child;
            else
                throw new InvalidOperationException(
                    $"{nameof(WrapRow)} раскладывает две части — ведущую и хвост; третьей в нём места нет.");
        }

        return (lead, trail);
    }
}
