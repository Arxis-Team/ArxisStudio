using System.Runtime.CompilerServices;
using System.Text;
using ArxisStudio.Services;
using Avalonia;

namespace ArxisStudio;

internal static class Program
{
    /// <summary>
    /// Точка входа: показывает дорогу к сборкам студии и уходит работать.
    /// </summary>
    /// <remarks>
    /// Больше здесь не делается ничего, и это не стиль, а условие запуска. Платформа лежит в
    /// <c>Lib</c>, модули — в <c>Modules</c>, а JIT компилирует метод целиком до первой его строки:
    /// назови этот метод хоть один тип Avalonia — и студия упала бы раньше, чем резолверы встали.
    /// Поэтому вся работа уехала в <see cref="Start"/>, и он не встраивается. Правило закреплено
    /// тестом: молча его нарушить нельзя.
    /// </remarks>
    /// <param name="args">Аргументы командной строки.</param>
    [STAThread]
    public static void Main(string[] args)
    {
        StudioAssemblyFolder.Library.Attach();
        StudioAssemblyFolder.Modules.Attach();

        Start(args);
    }

    /// <summary>Поднимает студию.</summary>
    /// <param name="args">Аргументы командной строки.</param>
    /// <remarks>
    /// Не встраивается намеренно: встроенный в <see cref="Main"/>, он заставил бы JIT разрешать
    /// типы Avalonia до того, как резолверы подключены.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Start(string[] args)
    {
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
