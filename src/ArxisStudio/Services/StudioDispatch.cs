using Avalonia.Threading;

namespace ArxisStudio.Services;

/// <summary>
/// Поток интерфейса глазами того, кто ждёт от него прохода.
/// </summary>
/// <remarks>
/// Пустое задание с фоновым старшинством встаёт за раскладкой, отрисовкой и всем отложенным, и его
/// завершение значит: дерево прошло. Этого ждут пять мест студии — заставка перед уходом, запуск
/// между этапами, выгрузка плагина, пока дерево отпустит снятые контролы, — и каждое писало его
/// само.
/// </remarks>
internal static class StudioDispatch
{
    /// <summary>Ждёт, пока диспетчер разберёт всё, что старше фона: раскладку, отрисовку, отложенное.</summary>
    public static Task PassAsync() =>
        Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background).GetTask();
}
