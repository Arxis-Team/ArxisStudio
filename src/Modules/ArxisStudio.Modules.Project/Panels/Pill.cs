using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Плашка под подписью плитки: заливка шире подписи на <see cref="Outset"/>, а место в раскладке —
/// ровно подпись.
/// </summary>
/// <remarks>
/// Рамка с полем отнимала бы у подписи ширину. Колонка плитки постоянна, и имя, которому колонки
/// хватало, переламывалось бы посреди слова — «ViewLocator.c» и «s» строкой ниже, — причём у каждой
/// плитки, а не только у выбранной: плашка стоит всегда, прозрачной, чтобы выбор не двигал
/// раскладку. Здесь заливка выходит за края подписи в поле плитки, где ничего нет, и подписи
/// всё равно, горит плашка или нет.
/// </remarks>
internal sealed class Pill : Decorator
{
    /// <summary>Заливка; пусто — плашки не видно.</summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<Pill>();

    /// <summary>Скругление.</summary>
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<Pill>();

    /// <summary>На сколько заливка выходит за края подписи.</summary>
    public static readonly StyledProperty<Thickness> OutsetProperty =
        AvaloniaProperty.Register<Pill, Thickness>(nameof(Outset));

    static Pill() => AffectsRender<Pill>(BackgroundProperty, CornerRadiusProperty, OutsetProperty);

    /// <summary>Заливка; пусто — плашки не видно.</summary>
    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>Скругление.</summary>
    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>На сколько заливка выходит за края подписи.</summary>
    public Thickness Outset
    {
        get => GetValue(OutsetProperty);
        set => SetValue(OutsetProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        if (Background is { } background)
            context.DrawRectangle(background, null, new RoundedRect(new Rect(Bounds.Size).Inflate(Outset), CornerRadius));
    }
}
