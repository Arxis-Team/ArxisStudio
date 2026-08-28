using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Sample;

/// <summary>
/// Невизуальная часть примера: служба, которая живёт, пока модуль поднят.
/// </summary>
/// <remarks>
/// Служба и панель — не два вида модулей, а две его части: модуль бывает
/// только службой, только панелью или тем и другим сразу. Здесь она есть
/// затем, чтобы пример показывал обе.
/// </remarks>
public sealed class SampleService : StudioService
{
    /// <inheritdoc/>
    public override void Start(IStudioContext context) =>
        context.Log.Write(StudioLogLevel.Debug, "Пример", "Служба запущена");
}
