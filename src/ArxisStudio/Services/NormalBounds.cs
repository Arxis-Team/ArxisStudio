using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArxisStudio.Services;

/// <summary>
/// Обычные границы окна: где оно стоит и какого размера, пока не развёрнуто.
/// </summary>
/// <remarks>
/// Развёрнутое окно отвечает размером экрана, а вернуться после перезапуска надо к тому, что было
/// до разворота. Правило одно на оба окна, которые перезапуск возвращает, — студии и настроек:
/// своей памяти у окна настроек не было, и развёрнутым оно записывало размер экрана как обычный.
/// <para>
/// Запоминается отложенно: система сообщает новые место и размер раньше, чем новый вид окна, и
/// разворот, спрошенный сразу, записался бы обычными границами.
/// </para>
/// </remarks>
internal sealed class NormalBounds
{
    private readonly Window _window;
    private PixelPoint _position;
    private Size _size;

    /// <summary>Начинает следить за окном — до его показа.</summary>
    /// <param name="window">Окно, чьи границы помнить.</param>
    public NormalBounds(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _window = window;

        window.Opened += (_, _) => Remember();
        window.PositionChanged += (_, _) => Later();
        window.PropertyChanged += (_, change) =>
        {
            if (change.Property == TopLevel.ClientSizeProperty)
                Later();
        };
    }

    /// <summary>Место окна для перезапуска; null — обычным окно ещё не бывало.</summary>
    public StudioPlacement? Placement =>
        _size is { Width: > 0, Height: > 0 } ? StudioPlacement.Of(_window, _position, _size) : null;

    /// <summary>
    /// Ставит окно туда, где его застал перезапуск, — до показа.
    /// </summary>
    /// <param name="placement">Место из сессии.</param>
    /// <remarks>
    /// Обычные границы запоминаются сразу: окно, поднятое развёрнутым, обычным ещё не бывало, а
    /// следующий перезапуск должен вернуть к тем же границам, а не к размеру экрана.
    /// </remarks>
    public void Put(StudioPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);

        placement.Put(_window);

        _position = _window.Position;
        _size = new Size(placement.Width, placement.Height);
    }

    private void Later() => Dispatcher.UIThread.Post(Remember, DispatcherPriority.Background);

    /// <summary>Запоминает границы, если окно сейчас в обычном виде.</summary>
    private void Remember()
    {
        if (_window.WindowState != WindowState.Normal)
            return;

        _position = _window.Position;
        _size = _window.ClientSize;
    }
}
