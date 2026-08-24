namespace ArxisStudio.Sdk;

/// <summary>
/// Точка входа плагина: студия находит наследника в сборке плагина и поднимает
/// его при активации.
/// </summary>
/// <remarks>
/// Наследник обязан иметь конструктор без аргументов — студия создаёт его сама.
/// Всё, что плагину нужно от студии, приходит в <see cref="Activate"/>: держать
/// контекст в поле можно, пользоваться им после <see cref="Deactivate"/> — нет.
/// </remarks>
public abstract class StudioPlugin
{
    /// <summary>Плагин поднят: самое время заявить команды и панели.</summary>
    /// <param name="context">Что студия даёт плагину.</param>
    public virtual void Activate(IStudioContext context)
    {
    }

    /// <summary>
    /// Плагин выключают: пора отпустить всё, что он держал.
    /// </summary>
    /// <remarks>
    /// После этого сборку плагина выгружают, и объект, оставшийся у студии в
    /// руках, не дал бы этого сделать.
    /// </remarks>
    public virtual void Deactivate()
    {
    }
}

/// <summary>
/// Невизуальная часть плагина: служба, которая живёт, пока плагин включён.
/// </summary>
/// <remarks>
/// Плагин может быть только службой, только панелью или тем и другим сразу —
/// одно другого не требует.
/// </remarks>
public abstract class StudioService
{
    /// <summary>Служба запускается.</summary>
    /// <param name="context">Что студия даёт плагину.</param>
    public abstract void Start(IStudioContext context);

    /// <summary>Служба останавливается.</summary>
    public virtual void Stop()
    {
    }
}
