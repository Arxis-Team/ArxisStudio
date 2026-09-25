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
    /// <c>lib</c>, модули — в <c>modules</c>, а JIT компилирует метод целиком до первой его строки:
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

        // Перезапущенная копия забирает сессию прежней и отвечает ей раньше всего остального:
        // прежняя ждёт ответа, прежде чем закрыться.
        StudioRelaunch.Accept(args);

        if (HandedOver(args))
            return;

        // Папку данных прежняя копия отпустила, но порт инструментов разработчика и файлы держит,
        // пока не выйдет совсем.
        if (StudioRelaunch.Predecessor is { } predecessor)
            StudioRelaunch.AwaitExit(predecessor, StudioRelaunch.Patience);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // Тем же потоком, что брал: мьютекс привязан к нему. Перезапущенная копия ждёт папку
            // и получает её здесь, не дожидаясь, пока этот процесс уйдёт совсем.
            StudioInstance.Current?.Dispose();
        }
    }

    /// <summary>
    /// Отдаёт аргументы уже запущенной студии, если такая есть над той же папкой данных.
    /// </summary>
    /// <param name="args">Аргументы этой студии.</param>
    /// <returns>Отдала — этой студии подниматься не надо.</returns>
    /// <remarks>
    /// Проверка стоит до Avalonia: вторая студия, собравшая приложение ради того, чтобы
    /// уйти, мелькнула бы заставкой. Первая, взявшая папку, запоминается в
    /// <see cref="StudioInstance.Current"/> и слушает вторых, когда у неё появится окно.
    /// <para>
    /// Не ответившая первая не держит вторую: человек, щёлкнувший по значку, обязан
    /// получить окно. Вторая тогда поднимается сама и говорит об этом журналом — две
    /// студии над одной папкой хуже одной, но лучше ни одной.
    /// </para>
    /// <para>
    /// <c>ARXIS_SINGLE_INSTANCE=0</c> снимает проверку: так поднимают вторую студию рядом
    /// с первой, проверяя одну через инструменты разработчика другой.
    /// </para>
    /// <para>
    /// Перезапущенная копия папку не проверяет, а ждёт: прежняя держит её, пока закрывается, и
    /// отдала бы новой её же аргументы.
    /// </para>
    /// </remarks>
    private static bool HandedOver(string[] args)
    {
        if (Environment.GetEnvironmentVariable("ARXIS_SINGLE_INSTANCE") == "0")
            return false;

        var name = StudioInstance.NameFor(ArxisStudio.Shell.StudioPaths.UserData);
        var instance = StudioRelaunch.Predecessor is { } predecessor
            ? StudioRelaunch.Claim(name, predecessor)
            : StudioInstance.Claim(name);

        if (instance.IsFirst)
        {
            StudioInstance.Current = instance;
            return false;
        }

        // Папку так и не отдали — держит её не прежняя копия, а кто-то третий. Уходить нельзя: сессия
        // уже забрана, и с этой копией ушло бы всё, что человек держал открытым.
        if (StudioRelaunch.Predecessor is not null)
        {
            StudioInstance.Unanswered = true;
            return false;
        }

        if (instance.Send(args))
            return true;

        StudioInstance.Unanswered = true;

        return false;
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
