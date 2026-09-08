using System.Diagnostics;

namespace ArxisStudio.Shell;

/// <summary>
/// Открыть что-нибудь средствами системы: папку в проводнике, ссылку в
/// браузере.
/// </summary>
/// <remarks>
/// Одно место на всю студию: тот же вызов нужен экрану Welcome для ссылок и
/// окну настроек для папки плагинов, а <c>UseShellExecute</c> с ловлей отказа
/// — ровно та мелочь, которую в двух местах пишут по-разному.
/// </remarks>
public static class StudioOpen
{
    /// <summary>Показывает путь или открывает ссылку.</summary>
    /// <param name="target">Путь или адрес.</param>
    /// <remarks>
    /// Отказ проглатывается: открыть ссылку или папку — не то, ради чего стоит
    /// падать. Нечем открыть, не та ассоциация, запрет политики — человек это
    /// увидит сам, ничего не открывшись.
    /// </remarks>
    public static void InShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
