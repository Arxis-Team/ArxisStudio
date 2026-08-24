using ArxisStudio.Sdk;

namespace Arxis.HelloPlugin;

/// <summary>
/// Невизуальная часть примера: служба, которая живёт, пока плагин включён.
/// </summary>
public sealed class HelloService : StudioService
{
    /// <inheritdoc/>
    public override void Start(IStudioContext context) =>
        context.Log.Write(StudioLogLevel.Debug, "Hello", "Служба запущена");
}
