namespace ArxisStudio.Modules.Console.Log;

/// <summary>
/// Сколько записей каждого уровня в журнале.
/// </summary>
/// <remarks>
/// Считается по всему журналу, а не по показанному: счётчик, падающий до нуля оттого, что человек
/// выключил уровень, ничего не сообщает. Смысл счётчика в обратном — заметить, что ошибок
/// двенадцать, глядя на предупреждения. Так это сделано в консоли Unity.
/// </remarks>
/// <param name="Debug">Подробностей для отладки.</param>
/// <param name="Info">Обычных сообщений.</param>
/// <param name="Warning">Предупреждений.</param>
/// <param name="Error">Ошибок.</param>
public readonly record struct LogCounts(int Debug, int Info, int Warning, int Error);
