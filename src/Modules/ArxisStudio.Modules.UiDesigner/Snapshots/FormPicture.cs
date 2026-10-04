using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace ArxisStudio.Modules.UiDesigner.Snapshots;

/// <summary>
/// Картинка формы: то, что показывает карточка, без рамки и заголовка, на фоне окна её приложения.
/// </summary>
/// <remarks>
/// <para>
/// Форма рисуется сразу в размер снимка — вектором, а не уменьшением большого растра: дешевле и чище.
/// Рисуется она в потоке интерфейса — объекты формы принадлежат ему, — и стоит это около миллисекунды;
/// кодирование в PNG — ещё две.
/// </para>
/// <para>
/// Фон — тот, которым тема приложения одевает окно: форма на нём и будет стоять у человека. Корень со
/// своим фоном рисует его сам поверх. Тема окна ищется от корня вверх, через приложение, которое
/// карточка поставила над формой; найденная тема самой студии — не тема приложения, и тогда снимок
/// остаётся без фона.
/// </para>
/// </remarks>
internal static class FormPicture
{
    /// <summary>Снимает элемент в PNG длинной стороной <paramref name="pixels"/>.</summary>
    /// <param name="face">Что снимать: область формы на карточке.</param>
    /// <param name="root">Корень формы: от него ищется тема окна её приложения.</param>
    /// <param name="pixels">Длинная сторона картинки в точках.</param>
    /// <returns>PNG; null — снимать нечего: область ещё не разложена.</returns>
    public static byte[]? Take(Visual face, Control root, int pixels)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(root);

        var bounds = face.Bounds;

        if (bounds.Width < 1 || bounds.Height < 1 || pixels < 1)
            return null;

        var scale = pixels / Math.Max(bounds.Width, bounds.Height);
        var size = new PixelSize(Math.Max(1, (int)Math.Round(bounds.Width * scale)), Math.Max(1, (int)Math.Round(bounds.Height * scale)));

        // Render рисует элемент в начале холста, а не на его месте в родителе: рамка карточки снимок не
        // сдвигает. Холста два: форма рисуется в масштабе снимка, а фон под неё кладётся при сведении.
        using var content = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));

        content.Render(face);

        using var picture = new RenderTargetBitmap(size);

        using (var context = picture.CreateDrawingContext(true))
        {
            var whole = new Rect(0, 0, size.Width, size.Height);

            if (WindowBackground(root) is { } background)
                context.FillRectangle(background, whole);

            context.DrawImage(content, whole, whole);
        }

        using var stream = new MemoryStream();

        picture.Save(stream, new PngBitmapEncoderOptions());

        return stream.ToArray();
    }

    /// <summary>Фон, которым тема приложения формы одевает окно; null — темы окна у приложения нет.</summary>
    private static IBrush? WindowBackground(Control root)
    {
        var variant = root.ActualThemeVariant;

        if (!root.TryFindResource(typeof(Window), variant, out var found) || found is not ControlTheme theme)
            return null;

        if (Application.Current?.TryGetResource(typeof(Window), variant, out var own) == true && ReferenceEquals(own, theme))
            return null;

        for (var current = theme; current is not null; current = current.BasedOn)
        {
            foreach (var setter in current.Setters.OfType<Setter>())
            {
                if (setter.Property != TemplatedControl.BackgroundProperty)
                    continue;

                return setter.Value switch
                {
                    IBrush brush => brush,
                    DynamicResourceExtension { ResourceKey: { } key }
                        when root.TryFindResource(key, variant, out var resource) => resource as IBrush,
                    _ => null,
                };
            }
        }

        return null;
    }
}
