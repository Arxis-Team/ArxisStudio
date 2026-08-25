using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Console;

/// <summary>
/// Точка входа модуля консоли.
/// </summary>
/// <remarks>
/// При активации модулю делать нечего: обе его панели берут журнал службой
/// контекста в тот момент, когда их строит оболочка. Точка входа всё равно
/// нужна — по ней студия узнаёт сборку модуля в лицо, тем же способом, что и у
/// внешних плагинов.
/// </remarks>
public sealed class ConsoleModule : StudioPlugin;
