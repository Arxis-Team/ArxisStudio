using ArxisStudio.Extensibility;

namespace ArxisStudio.Services;

/// <summary>
/// Отпускает всё, что студия держит за плагином, прежде чем его выгрузят.
/// </summary>
/// <remarks>
/// Порядок здесь важнее самих действий, и он один на все дороги — и на
/// перезагрузку плагина, и на отключение упавшего. Сперва фоновые задачи:
/// работающая держит типы плагина, а через них его контекст загрузки, и
/// выгрузить его, пока она жива, нельзя. Потом документы: их представления
/// построил плагин, и живут они там же. Потом то, что стоит на экране, —
/// панели и полоса.
/// <para>
/// Реестры, заведённые на владельца, здесь не трогают: команды, экспорты и
/// вклады убирает сам хост по своему <c>Unloading</c>. Сюда попадает только то,
/// чего хост знать не может.
/// </para>
/// <para>
/// Прохода раскладки здесь нет намеренно: снятые контролы отпускает не список,
/// а дерево окна, и дерево это одно на всех отпущенных — ждать его дело того,
/// кто отпустил последнего.
/// </para>
/// <para>
/// Порядок жил в двух местах сразу, и в одном из них был неполон: отключение
/// упавшего плагина не останавливало его задач вовсе — студия выгружала бы
/// плагин, чей поток ещё работает.
/// </para>
/// </remarks>
/// <param name="tasks">Список идущих задач студии.</param>
/// <param name="patience">
/// Сколько ждать фоновые задачи; null — <see cref="Patience"/>.
/// </param>
public sealed class PluginRelease(StudioTaskRegistry tasks, TimeSpan? patience = null)
{
    /// <summary>
    /// Сколько студия ждёт фоновые задачи плагина.
    /// </summary>
    /// <remarks>
    /// Ждать без предела нельзя: работа, не смотрящая в токен, заморозила бы
    /// студию насмерть. Пять секунд — предел, после которого честнее сказать
    /// человеку, что прежняя копия осталась в памяти.
    /// </remarks>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _patience = patience ?? Patience;

    /// <summary>Чем закрыть документы плагина; null — закрывать нечем.</summary>
    public Func<string, Task>? Documents { get; set; }

    /// <summary>Чем снять с экрана панели и полосу плагина; null — снимать нечем.</summary>
    public Action<string>? Views { get; set; }

    /// <summary>Задачи плагина не ушли в срок; в поле — чьи.</summary>
    public event EventHandler<string>? Lingered;

    /// <summary>
    /// Отпускает плагина.
    /// </summary>
    /// <param name="pluginId">Кого отпускаем.</param>
    /// <returns><c>true</c>, если задачи плагина успели уйти.</returns>
    /// <remarks>
    /// Упрямая задача работу не отменяет: документы и панели снимаются всё
    /// равно. Плагин, которого не выпустить целиком, — не повод оставить на
    /// экране половину его следов.
    /// </remarks>
    public async Task<bool> LetGoAsync(string pluginId)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);

        var quiet = await tasks.StopAsync(pluginId, _patience);

        if (!quiet)
            Lingered?.Invoke(this, pluginId);

        if (Documents is { } close)
            await close(pluginId);

        Views?.Invoke(pluginId);

        return quiet;
    }
}
