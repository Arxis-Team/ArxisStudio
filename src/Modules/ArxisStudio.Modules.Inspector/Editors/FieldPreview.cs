using ArxisStudio.Inspector;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>
/// Протяжка поля: значение видно на цели на каждом движении, а пишется одно — при отпускании; Esc возвращает цель к
/// тому, что было.
/// </summary>
/// <remarks>
/// Предпросмотр заводится на первом движении (<see cref="InspectorFieldContext.BeginPreview"/>) и кончается
/// фиксацией или отменой. Цель, которая показывать на ходу не умеет, получает одну запись при отпускании: поле и так
/// показывает протянутое.
/// </remarks>
/// <param name="context">Контекст поля.</param>
internal sealed class FieldPreview(InspectorFieldContext context)
{
    private IInspectorPreview? _preview;
    private bool _refused;

    /// <summary>Показывает протянутое значение на цели.</summary>
    /// <param name="text">Значение текстом.</param>
    public void Show(string text)
    {
        if (_preview is null && !_refused)
        {
            _preview = context.BeginPreview();
            _refused = _preview is null;
        }

        _preview?.Show(text);
    }

    /// <summary>Пишет значение — одной правкой: протянутое — фиксацией предпросмотра, остальное — записью.</summary>
    /// <param name="text">Значение текстом; пусто — сбросить.</param>
    /// <returns>Записалось ли.</returns>
    public async Task<bool> CommitAsync(string? text)
    {
        var preview = _preview;

        _preview = null;
        _refused = false;

        if (preview is null)
            return await context.WriteAsync(text).ConfigureAwait(true);

        using (preview)
        {
            return text is null
                ? await context.WriteAsync(null).ConfigureAwait(true)
                : await preview.CommitAsync(text).ConfigureAwait(true);
        }
    }

    /// <summary>Протяжку отменили или поле уходит: цель возвращается к тому, что было, а написанное не тронуто.</summary>
    public void Cancel()
    {
        _preview?.Dispose();
        _preview = null;
        _refused = false;
    }
}
