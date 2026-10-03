using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Data;

namespace ArxisStudio.Dragging;

/// <summary>Подсказка у курсора, пока несут: что несут и что ответила цель; ведёт её <see cref="DragSession"/>.</summary>
public sealed partial class DragGhost : AxUserControl
{
    /// <summary>Ключ кисти значка без своей: вторичный цвет подписи, как у значка строки.</summary>
    private const string Plain = "AxTextSecondaryBrush";

    /// <summary>Строит разметку для показа XAML-инструментами; студия зовёт конструктор с видом.</summary>
    public DragGhost()
        : this(new StudioDragVisual("…"))
    {
    }

    /// <summary>Строит подсказку по виду источника.</summary>
    /// <param name="visual">Что несут.</param>
    public DragGhost(StudioDragVisual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);

        InitializeComponent();

        Label = visual.Label;
        Caption.Text = visual.Label;
        Glyph.Data = visual.Icon;
        Glyph.IsVisible = visual.Icon is not null;

        // Кисть источника — как есть; без неё цвет берётся у темы и идёт за ней сам.
        if (visual.IconBrush is { } brush)
            Glyph.Foreground = brush;
        else
            Glyph.Bind(ForegroundProperty, Glyph.GetResourceObservable(Plain), BindingPriority.Style);
    }

    /// <summary>Подпись источника.</summary>
    public string Label { get; }

    /// <summary>Ответ цели под подписью; пусто — строки нет.</summary>
    public string? Hint
    {
        get => Answer.Text;
        set
        {
            Answer.Text = value;
            Answer.IsVisible = !string.IsNullOrEmpty(value);
        }
    }
}
