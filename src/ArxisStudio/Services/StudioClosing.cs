using Avalonia.Controls;

namespace ArxisStudio.Services;

/// <summary>
/// Закрытие окна, о котором спрашивают документы: первое закрытие отменяется, они решают, второе
/// проходит.
/// </summary>
/// <remarks>
/// <para>
/// Документы отвечают асинхронно — сохраняют, спрашивают человека, — а окно закрывается синхронно:
/// ответ, пришедший после <c>Closing</c>, ничего бы не остановил. Поэтому нужен вопрос — первое
/// закрытие отменяется, документы спрашиваются, и окно закрывается заново, уже без вопроса.
/// Без нужды закрытие не отменяется: ни несохранённого, ни документа, решающего о себе, — окно
/// закрывается сразу, как прежде.
/// </para>
/// <para>
/// Конец сеанса Windows ничьих ответов не ждёт, и его не останавливают: отменённое закрытие при
/// выходе из системы держит выход до тайм-аута, а потом окно всё равно убьют.
/// </para>
/// <para>
/// Отдельным классом, а не обработчиком главного окна: правило проверяется на простом окне, без
/// студии с плагинами за ним.
/// </para>
/// </remarks>
internal sealed class StudioClosing
{
    private readonly Window _window;
    private readonly Func<bool> _needs;
    private readonly Func<Task<bool>> _confirm;
    private readonly Action _farewell;

    // Документы уже согласились: следующее закрытие идёт без вопроса.
    private bool _confirmed;

    // Документы спрашиваются: второе закрытие, пришедшее до ответа, ждать нечего.
    private bool _asking;

    private StudioClosing(Window window, Func<bool> needs, Func<Task<bool>> confirm, Action farewell)
    {
        _window = window;
        _needs = needs;
        _confirm = confirm;
        _farewell = farewell;
        _window.Closing += OnClosing;
    }

    /// <summary>Ставит вопрос документам на закрытие окна.</summary>
    /// <param name="window">Окно.</param>
    /// <param name="needs">Нужен ли вопрос сейчас.</param>
    /// <param name="confirm">Сам вопрос; <c>false</c> — окно остаётся.</param>
    /// <param name="farewell">Что сделать, когда окно и правда закрывается.</param>
    public static void Attach(Window window, Func<bool> needs, Func<Task<bool>> confirm, Action farewell)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(needs);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(farewell);

        _ = new StudioClosing(window, needs, confirm, farewell);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_confirmed || e.CloseReason == WindowCloseReason.OSShutdown || !_needs())
        {
            _farewell();
            return;
        }

        e.Cancel = true;

        if (_asking)
            return;

        _asking = true;

        try
        {
            if (!await _confirm())
                return;
        }
        finally
        {
            _asking = false;
        }

        _confirmed = true;
        _window.Close();
    }
}
