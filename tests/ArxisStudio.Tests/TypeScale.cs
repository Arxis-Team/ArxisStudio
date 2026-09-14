using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Шкала кеглей темы, выращенная в показанном окне, и проверка, что текст её пережил.
/// </summary>
/// <remarks>
/// Та же проверка, что у темы, но на экранах студии: высоты там прибивала не только тема, но
/// и разметка экранов, а тест темы о разметке не знает.
/// <para>
/// Шкалу растят ключами темы, как вырастила бы её настройка размера текста, а не
/// наследуемым кеглем окна: текст студии берёт кегль ключом, и кегль у корня до него просто
/// не дошёл бы.
/// </para>
/// </remarks>
internal static class TypeScale
{
    /// <summary>Полпикселя на округление раскладки.</summary>
    private const double Tolerance = 0.5;

    /// <summary>Вся шкала кеглей темы.</summary>
    private static readonly string[] Sizes =
    [
        "AxFontSize", "AxFontSizeSmall", "AxFontSizeCaption",
        "AxFontSizeLarge", "AxFontSizeTitle", "AxFontSizeDisplay",
    ];

    /// <summary>Растит всю шкалу кеглей в окне.</summary>
    public static void Enlarge(Window window, double factor)
    {
        foreach (var key in Sizes)
        {
            Assert.True(Application.Current!.TryFindResource(key, out var size), $"в теме нет кегля {key}");
            window.Resources[key] = (double)size! * factor;
        }

        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    /// <summary>Строки текста, которые окно сейчас показывает.</summary>
    public static IReadOnlyList<TextBlock> Labels(Visual root) =>
    [
        .. root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(label => label.IsEffectivelyVisible && !string.IsNullOrEmpty(label.Text)),
    ];

    /// <summary>Строка получила всё, что ей нужно, и ничто её не обрезает.</summary>
    /// <remarks>
    /// <para>
    /// Нужное меряется набранным текстом, а не <c>DesiredSize</c>: раскладка урезает желаемое
    /// до предложенного, и в прибитой высоте срезанная строка «просит» ровно столько, сколько
    /// ей дали.
    /// </para>
    /// <para>
    /// По ширине строке с многоточием помещаться не обязательно — она сама говорит, что
    /// сокращена. Остальным обязательно: без многоточия лишнее просто пропадает.
    /// </para>
    /// <para>
    /// Обрезает всякий предок с <c>ClipToBounds</c>. Окно прокрутки — только по той оси, по
    /// которой не прокручивает: ушедшее за его край по прокручиваемой оси не срезано, а
    /// прокручено, и до него человек доберётся. Дальше окна прокрутки проверка не идёт — выше
    /// видно уже его, а не содержимое.
    /// </para>
    /// </remarks>
    public static bool Whole(TextBlock label, Visual root, out string why)
    {
        var layout = label.TextLayout;
        var padding = label.Padding;
        var height = layout.Height + padding.Top + padding.Bottom;

        if (label.Bounds.Height + Tolerance < height)
        {
            why = $"по высоте нужно {height:0.#}, а дали {label.Bounds.Height:0.#}";
            return false;
        }

        var width = layout.WidthIncludingTrailingWhitespace + padding.Left + padding.Right;

        if (label.TextTrimming == TextTrimming.None && label.TextWrapping == TextWrapping.NoWrap &&
            label.Bounds.Width + Tolerance < width)
        {
            why = $"по ширине нужно {width:0.#}, а дали {label.Bounds.Width:0.#}";
            return false;
        }

        for (var clip = label.GetVisualParent(); clip is not null; clip = clip.GetVisualParent())
        {
            var scroll = clip as ScrollContentPresenter;

            if (clip == root || clip.ClipToBounds || scroll is not null)
            {
                var drawn = new Rect(label.Bounds.Size).TransformToAABB(label.TransformToVisual(clip)!.Value);
                var across = scroll is null || !scroll.CanHorizontallyScroll;
                var down = scroll is null || !scroll.CanVerticallyScroll;

                if ((across && (drawn.Left < -Tolerance || drawn.Right > clip.Bounds.Width + Tolerance)) ||
                    (down && (drawn.Top < -Tolerance || drawn.Bottom > clip.Bounds.Height + Tolerance)))
                {
                    why = $"строка {Show(drawn)} выходит из {clip.GetType().Name} {Show(new Rect(clip.Bounds.Size))}";
                    return false;
                }
            }

            if (clip == root || scroll is not null)
                break;
        }

        why = string.Empty;
        return true;
    }

    private static string Show(Rect rect) =>
        $"{rect.X:0.#},{rect.Y:0.#} {rect.Width:0.#}×{rect.Height:0.#}";
}
