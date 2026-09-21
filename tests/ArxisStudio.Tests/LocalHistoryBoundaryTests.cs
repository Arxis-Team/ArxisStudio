using ArxisStudio.LocalHistory;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Локальная история не ссылается ни на что, кроме платформы.
/// </summary>
/// <remarks>
/// Журнал и хранилище переживают студию и читаются другой её версией — их формат не должен
/// зависеть ни от интерфейса, ни от движка MSBuild, ни от модели проектов. Сборка ловит объявленную
/// ссылку (<c>AXH1001</c>), а тест — ту, что дошла до метаданных готовой сборки.
/// </remarks>
public class LocalHistoryBoundaryTests
{
    /// <summary>В метаданных сборки — только платформа .NET.</summary>
    [Fact]
    public void The_local_history_references_nothing_but_the_platform()
    {
        var foreign = typeof(LocalHistoryStore).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name is not ("System" or "netstandard" or "mscorlib")
                && !name.StartsWith("System.", StringComparison.Ordinal))
            .ToList();

        Assert.True(foreign.Count == 0, $"локальная история ссылается не только на платформу: {string.Join(", ", foreign)}");
    }
}
