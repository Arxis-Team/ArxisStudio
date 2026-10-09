using ArxisStudio.Sdk;

namespace ArxisStudio.Inspector;

/// <summary>
/// Инспектор студии: одна панель свойств на всех — дизайнер показывает в ней выбранное на холсте, плагин —
/// свои объекты.
/// </summary>
/// <remarks>
/// <para>
/// <b>Показан последний.</b> <see cref="Show"/> ставит цель поверх прежних, а её ручка, отпущенная, снимает
/// её — и показан снова тот, кто был до неё, если он ещё жив: дизайнер, уступивший место плагину, получает
/// его назад, когда плагин закончил. Закрытая цель (<see cref="InspectorTarget.IsClosed"/>) уходит сама, цель
/// выгруженного плагина — тоже.
/// </para>
/// <para>
/// <b>Замок</b> (<see cref="IsLocked"/>) держит показанную цель, как замок инспектора Unity: новые цели встают в
/// очередь и показываются, когда замок снимут.
/// </para>
/// </remarks>
public interface IStudioInspector
{
    /// <summary>Показанная цель; пусто — показывать нечего.</summary>
    InspectorTarget? Current { get; }

    /// <summary>Замок держит показанную цель.</summary>
    bool IsLocked { get; }

    /// <summary>Показана другая цель или снят замок.</summary>
    event EventHandler? CurrentChanged;

    /// <summary>Показывает цель поверх прежних.</summary>
    /// <param name="target">Цель.</param>
    /// <returns>Ручка: отпущенная, снимает цель.</returns>
    /// <remarks>
    /// Ставить, когда цель есть, и отпускать, когда её больше нет: дизайнер ставит выбранное на холсте при каждом
    /// выборе, а ручку прежнего отпускает. Каретки инспектор не берёт: показ — не переход.
    /// </remarks>
    IDisposable Show(InspectorTarget target);
}

/// <summary>Дорога к инспектору из контекста плагина.</summary>
/// <remarks>
/// Инспектор публикует модуль <c>arxis.inspector</c>; плагину, который показывает в нём свои объекты, стоит
/// объявить модулю нижнюю границу <c>1.0</c>. Тому, кто только дополняет инспектор редакторами и разделами,
/// служба не нужна вовсе: вклады студия соберёт сама.
/// </remarks>
public static class StudioInspectorAccess
{
    /// <summary>Инспектор студии.</summary>
    /// <param name="context">Контекст плагина.</param>
    /// <returns>Служба или null: модуля нет, он не поднялся, или его версия ниже объявленной границы.</returns>
    public static IStudioInspector? Inspector(this IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.GetService<IStudioExports>()?.Get<IStudioInspector>();
    }
}
