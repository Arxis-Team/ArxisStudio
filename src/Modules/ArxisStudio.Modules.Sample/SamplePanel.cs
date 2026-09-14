using ArxisStudio.Sdk;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Sample;

/// <summary>
/// Панель примера: показывает то, что модуль знает о себе и о студии.
/// </summary>
/// <remarks>
/// Интерфейс написан разметкой — файл <c>SamplePanelView.axaml</c> рядом.
/// Панель остаётся тем же <see cref="ToolWindow"/>: она отвечает за то, когда
/// интерфейс появится и с чем его свяжут, а как он выглядит — сказано в
/// разметке. Правило «строить на контролах <c>Ax*</c>» действует и там:
/// разметку проверяет <c>ARX0006</c>.
/// <para>
/// Строится панель по требованию: пока её никто не показал, её содержимого не
/// существует. Всё, что ей нужно от студии, приходит контекстом — жёсткой
/// ссылки на приложение у модуля нет.
/// </para>
/// </remarks>
[ToolWindow("sample.panel")]
public sealed class SamplePanel : ToolWindow
{
    private SamplePanelModel? _model;

    /// <inheritdoc/>
    protected override Control Build()
    {
        _model = new SamplePanelModel(Context);

        return new SamplePanelView { DataContext = _model };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Модель подписана на службу проектов, и отпустить подписку может только панель: студия о
    /// ней не знает. Прощание — ровно то место, которое контракт для этого и отводит.
    /// </remarks>
    public override void Release()
    {
        _model?.Dispose();
        _model = null;
    }
}
