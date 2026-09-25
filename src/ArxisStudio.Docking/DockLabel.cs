using Avalonia.Automation;
using Avalonia.Controls;

namespace ArxisStudio.Docking;

/// <summary>
/// Подпись кнопки-значка раскладки: подсказка под курсором и имя для средств доступности.
/// </summary>
/// <remarks>
/// На кнопках шапки — значок 12×12, и узнать о кнопке больше неоткуда. Подсказка приходит по
/// наведению мыши, а тому, кто слушает диктора, мышь не помощник: имя нужно обоим, и одним словом
/// (правило записей 72 и 98). Пара жила в четырёх местах, и забытая половина делала кнопку немой.
/// <para>
/// Там, где кнопку создают, пару пишут явно: <c>ARX0013</c> ищет имя рядом с созданием и этой
/// обёртки не узнаёт. Помощник — для подписи, которую ставят заново, когда сменился заголовок.
/// </para>
/// </remarks>
internal static class DockLabel
{
    /// <summary>Подписывает кнопку.</summary>
    /// <param name="control">Кнопка.</param>
    /// <param name="text">Что она сделает; null — подписи пока нет.</param>
    public static void Named(Control control, string? text)
    {
        ToolTip.SetTip(control, text);
        AutomationProperties.SetName(control, text ?? string.Empty);
    }
}
