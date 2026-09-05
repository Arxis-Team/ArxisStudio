using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Interactivity;

namespace Arxis.MyPlugin;

/// <summary>
/// Представление панели: разметка и то, что за ней стоит.
/// </summary>
/// <remarks>
/// Конструктор без доводов — намеренно: представление обязано собираться и
/// само по себе, иначе его не откроет предпросмотр. Всё, что оно знает о
/// студии, ставит панель после создания.
/// <para>
/// Понадобятся вычисляемые значения — заведите модель и положите её в
/// <c>DataContext</c>: разметка тогда привяжется к ней, а представление
/// останется без единой ссылки на студию.
/// </para>
/// </remarks>
public partial class PluginPanelView : AxUserControl
{
    /// <summary>Собирает представление из разметки.</summary>
    public PluginPanelView() => InitializeComponent();

    /// <summary>Что студия даёт плагину; ставит панель.</summary>
    public IStudioContext? Studio { get; init; }

    /// <summary>
    /// Просит студию исполнить команду плагина.
    /// </summary>
    /// <remarks>
    /// Через команду, а не напрямую: та же дорога, что у пункта меню и у
    /// кнопки в полосе, — и одно место, где написано, что делать.
    /// </remarks>
    private void OnHelloClick(object? sender, RoutedEventArgs e) =>
        Studio?.Commands.Invoke("arxis.my-plugin.hello");
}
