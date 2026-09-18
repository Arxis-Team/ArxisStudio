using System.ComponentModel;
using System.Diagnostics;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Показывает файл или папку в файловом менеджере системы.
/// </summary>
/// <remarks>
/// Показывает, а не открывает: файл выделен в своей папке — так делает «Show in Explorer» в Rider, и
/// так человек видит, что лежит рядом. Проводник и Finder умеют выделить сами; прочим системам
/// открывается папка. Сбой глотается: не открывшийся проводник — не ошибка окна проекта, и
/// сказать о нём человеку нечего, кроме того, что он и так видит.
/// </remarks>
internal static class Reveal
{
    /// <summary>Подмена для тестов: вместо проводника — запись пути.</summary>
    internal static Action<string>? Override { get; set; }

    /// <summary>Показывает путь.</summary>
    /// <param name="path">Файл или папка.</param>
    public static void Show(string path)
    {
        if (Override is { } recorded)
        {
            recorded(path);
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", ["-R", path]);
            }
            else if ((Directory.Exists(path) ? path : Path.GetDirectoryName(path)) is { Length: > 0 } folder)
            {
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            // Проводник не открылся — окно проекта от этого не сломалось.
        }
    }

    /// <summary>Ключ подписи пункта меню — у каждой системы своё слово.</summary>
    public static string Words =>
        OperatingSystem.IsWindows() ? "project.menu.reveal.windows"
        : OperatingSystem.IsMacOS() ? "project.menu.reveal.mac"
        : "project.menu.reveal.other";
}
