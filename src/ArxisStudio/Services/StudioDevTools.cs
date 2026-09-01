using Avalonia;
#if DEBUG
using AvaDevTools;
#endif

namespace ArxisStudio.Services;

/// <summary>
/// Подключает к студии инструменты отладки интерфейса (AvaDevTools): дерево
/// элементов, стили, раскладка, журнал и конечная точка MCP на петле.
/// </summary>
/// <remarks>
/// Инструменты живут в том же процессе, что и студия, поэтому смотрят на те же
/// живые объекты, что видит пользователь, — включая содержимое канвы дизайнера.
/// Это отладочная оснастка: пакета в Release-сборке нет, и весь код обращения к
/// нему закрыт условной компиляцией. Порт и разрешения читаются из окружения,
/// чтобы запуск под агентом не требовал правки исходников.
/// </remarks>
public static class StudioDevTools
{
    /// <summary>Порт конечной точки MCP по умолчанию.</summary>
    public const int DefaultMcpPort = 5171;

    /// <summary>
    /// Привязывает инструменты ко всему приложению: F12 открывает их из любого
    /// окна студии.
    /// </summary>
    /// <param name="application">Приложение студии.</param>
    public static void Attach(Application application)
    {
#if DEBUG
        ArgumentNullException.ThrowIfNull(application);

        application.AttachAvaDevTools(new DevToolsOptions
        {
            // Конечная точка поднимается сразу: агент подключается к уже
            // запущенной студии, а не запускает её сам, поэтому ждать, пока
            // кто-то откроет окно инструментов и щёлкнет переключатель,
            // означало бы не подключиться вовсе.
            McpServer = ReadFlag("ARXIS_DEVTOOLS_MCP", true),
            McpPort = ReadPort("ARXIS_DEVTOOLS_MCP_PORT", DefaultMcpPort),

            // Разрешения на удержание всплывающих состояний и на ввод — то, чем
            // агент проверяет студию так же, как это делает человек.
            McpAllowHold = ReadFlag("ARXIS_DEVTOOLS_MCP_HOLD", true),
            McpAllowInput = ReadFlag("ARXIS_DEVTOOLS_MCP_INPUT", true),

            // Чтение под другими условиями — светлая тема, двойной текст, окно
            // в 360 пикселей, письмо справа налево. Инструменты сами меняют для
            // этого свойства приложения и сами возвращают их назад, поэтому
            // разрешение отдельное от ввода: ничего не нажимается, но студия на
            // время перестаёт быть той, что была.
            //
            // Открыто, как и остальные: студия рисует чужую разметку, и вопрос
            // «а не обрежется ли это у пользователя» относится к ней самой не
            // меньше, чем к тому, что в ней открыли.
            McpAllowVariants = ReadFlag("ARXIS_DEVTOOLS_MCP_VARIANTS", true),
        });
#endif
    }

    private static bool ReadFlag(string name, bool fallback) =>
        Environment.GetEnvironmentVariable(name) switch
        {
            null or "" => fallback,
            "0" or "false" or "False" => false,
            _ => true,
        };

    private static int ReadPort(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var port) && port is > 0 and < 65536
            ? port
            : fallback;
}
