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

    /// <summary>Показывает объект — его свойства CLR — поверх прежних целей.</summary>
    /// <param name="instance">Объект: настройки плагина, модель его панели.</param>
    /// <param name="title">Имя в шапке — уже переведённое.</param>
    /// <returns>Ручка: отпущенная, снимает цель.</returns>
    /// <remarks>
    /// <para>
    /// Свойства — открытые свойства экземпляра, которые читаются. Пишется то, у чего есть открытая запись и чей текст
    /// читается: строки, числа, флажки и перечисления — сами, значения общих сборок — цвет, отступ — их конвертером
    /// (<see cref="System.ComponentModel.TypeConverter"/>) или <c>Parse</c>. Значение своего типа плагина видно
    /// текстом и только читается: конвертер запомнил бы тип навсегда, и плагин было бы не выгрузить. Перемены объект
    /// объявляет сам — <see cref="System.ComponentModel.INotifyPropertyChanged"/>, — и строки переспрашивают значение.
    /// </para>
    /// <para>
    /// Подпись, раздел, пояснение, скрытость, только чтение, границы и значение по умолчанию — стандартными
    /// атрибутами .NET: <c>[DisplayName]</c>, <c>[Category]</c>, <c>[Description]</c>, <c>[Browsable]</c>,
    /// <c>[EditorBrowsable]</c>, <c>[ReadOnly]</c>, <c>[Range]</c>, <c>[DefaultValue]</c>. Значение, равное значению
    /// по умолчанию, — незаданное, а сброс возвращает его.
    /// </para>
    /// <para>
    /// Код объекта зовётся через шов его хозяина, и с выгрузкой хозяина цель закрывается и отпускает объект. Истории
    /// у объекта нет: Ctrl+Z в инспекторе ему не шагает. Появилось в контракте 1.1.
    /// </para>
    /// </remarks>
    IDisposable Inspect(object instance, string title);
}

/// <summary>Дорога к инспектору из контекста плагина.</summary>
/// <remarks>
/// Инспектор публикует модуль <c>arxis.inspector</c>; плагину, который показывает в нём свои цели, стоит объявить
/// модулю нижнюю границу <c>1.0</c>, а показывающему объекты через <see cref="IStudioInspector.Inspect"/> —
/// <c>1.1</c>. Тому, кто только дополняет инспектор редакторами и разделами, служба не нужна вовсе: вклады студия
/// соберёт сама.
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
