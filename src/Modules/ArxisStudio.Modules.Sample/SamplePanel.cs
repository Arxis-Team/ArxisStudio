using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ArxisStudio.Modules.Sample;

/// <summary>
/// Панель примера: показывает то, что модуль знает о себе и о студии.
/// </summary>
/// <remarks>
/// Интерфейс собран на контролах <c>Ax*</c> — правило одно и для плагинов, и
/// для модулей: панели стоят рядом, и разнобой видно сразу. Панелей раскладки
/// Avalonia это не касается, своего оформления они не несут.
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
    protected override Control Build()
    {
        var button = new AxButton { Content = "Записать в журнал", HorizontalAlignment = HorizontalAlignment.Left };

        button.Click += (_, _) => Context.Commands.Invoke(SampleModule.AboutCommand);

        return new StackPanel
        {
            Spacing = 10,
            Margin = new Avalonia.Thickness(12),
            VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                Line("Пример встроенного модуля"),
                Line("Манифест: ресурс module.json в сборке"),
                Line("Контекст загрузки: основной, выгрузке не подлежит"),
                Line(Context.ProjectPath is { Length: > 0 } path
                    ? $"Проект: {Path.GetFileName(path)}"
                    : "Проект не открыт"),
                button,
            },
        };
    }

    private static TextBlock Line(string text) => new() { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
}
