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
    /// <inheritdoc/>
    protected override Control Build() => new SamplePanelView
    {
        DataContext = new SamplePanelModel(Context),
    };
}
