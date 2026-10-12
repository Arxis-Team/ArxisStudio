namespace ArxisStudio.Modules.UiDesigner;

/// <summary>Аренда, отпускать которую нечего: у того, кому держать нечего.</summary>
internal sealed class Nothing : IDisposable
{
    private Nothing()
    {
    }

    /// <summary>Единственный экземпляр: состояния нет.</summary>
    public static Nothing Instance { get; } = new();

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
