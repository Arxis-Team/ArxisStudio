using System.Text;
using ArxisStudio.Services;
using Avalonia;

namespace ArxisStudio;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Дорога к модулям — раньше всего остального. Сборка кладёт их в папку
        // Modules, и основной контекст без подсказки их не найдёт; а задеть
        // сборку модуля может любое обращение к его типу, не только подъём.
        StudioModuleFolder.Attach();

        // Первая отметка — здесь: всё, что было до неё, это старт среды, и
        // приложению оно не принадлежит. Но ждёт-то его человек.
        StudioLaunch.Mark("среда");

        UseUtf8();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Переводит стандартный вывод в UTF-8.
    /// </summary>
    /// <remarks>
    /// В журнал студии пишут по-русски, а перенаправленный вывод Windows
    /// отдаёт в кодировке системы: файл, собранный из него, читается как
    /// набор вопросительных знаков всяким, кто ждёт UTF-8, — а ждут её все.
    /// <para>
    /// Консоли может не быть вовсе: студию запускают и мышью. Тогда менять
    /// нечего, и это не повод не запуститься.
    /// </para>
    /// </remarks>
    private static void UseUtf8()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (Exception e) when (e is IOException or System.Security.SecurityException)
        {
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .WithCascadiaFont();
}
