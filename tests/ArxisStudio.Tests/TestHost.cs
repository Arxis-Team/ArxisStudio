using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;

namespace ArxisStudio.Tests;

/// <summary>
/// Хост, собранный так же, как его собирает студия.
/// </summary>
/// <remarks>
/// Главное здесь — подписка на <see cref="PluginHost.Unloading"/>. Реестры,
/// заведённые на владельца, обязаны чиститься при уходе плагина, иначе их
/// записи держат объекты выгружаемого контекста и тот не собирается никогда.
/// Тест, собравший хост без этой подписки, мерил бы не студию, а свою
/// самоделку — и проверка выгрузки в нём проходила бы или падала по причинам,
/// к продукту отношения не имеющим.
/// </remarks>
internal sealed class TestHost : IDisposable
{
    /// <summary>Собирает хост с продуктовой уборкой реестров.</summary>
    /// <param name="projectPath">Открытый проект; null — проекта нет.</param>
    /// <param name="commands">
    /// Чем заменить реестр команд. Нужно там, где проверяется поведение
    /// студии на сбое внутри самой выдачи команд.
    /// </param>
    public TestHost(string? projectPath = null, IStudioCommands? commands = null)
    {
        Host = new PluginHost(new StudioContextFactory(
            Log, commands ?? Commands, projectPath, exports: Exports));

        Host.Unloading += (_, id) =>
        {
            Commands.RemoveOwnedBy(id);
            Exports.RemoveOwnedBy(id);
        };
    }

    /// <summary>Сам хост.</summary>
    public PluginHost Host { get; }

    /// <summary>Реестр команд, отданный плагинам.</summary>
    public StudioCommands Commands { get; } = new();

    /// <summary>Реестр экспортов, отданный плагинам.</summary>
    public StudioExportRegistry Exports { get; } = new();

    /// <summary>Журнал, отданный плагинам.</summary>
    public StudioLog Log { get; } = new();

    /// <inheritdoc/>
    public void Dispose() => Host.Dispose();
}
