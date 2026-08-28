using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Тесты, трогающие общий словарь студии, идут по одному.
/// </summary>
/// <remarks>
/// <c>Localizer</c> — один на процесс, а xUnit пускает классы параллельно:
/// тест, переключивший язык или подменивший папку словарей, менял бы текст
/// под руками у соседа, и падал бы при этом сосед. Общая коллекция ставит
/// такие классы в очередь.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class LocalizationCollection
{
    /// <summary>Имя коллекции.</summary>
    public const string Name = "Localization";
}
