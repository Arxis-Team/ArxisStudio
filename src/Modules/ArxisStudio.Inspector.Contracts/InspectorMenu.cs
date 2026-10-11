using ArxisStudio.Sdk;
using Avalonia.Media;

namespace ArxisStudio.Inspector;

/// <summary>
/// Пункты меню инспектора: меню строки свойства и меню ⋮ всей цели — как <c>contextualPropertyMenu</c> у Unity.
/// </summary>
/// <remarks>
/// Вклад: студия найдёт его сама, построит через шов и снимет с выгрузкой плагина. Пункт зовётся тоже через шов —
/// упавший пункт засчитывается его плагину, а не инспектору.
/// </remarks>
public abstract class InspectorMenu : StudioContribution
{
    /// <summary>Дополняет меню.</summary>
    /// <param name="menu">Меню: свойство строки, если меню строки, и дорога к цели.</param>
    public abstract void Fill(InspectorMenuContext menu);
}

/// <summary>Меню инспектора, которое дополняют: свойство, значение и дорога к цели.</summary>
/// <remarks>Даёт модуль инспектора; дополняют его в потоке интерфейса.</remarks>
public abstract class InspectorMenuContext
{
    /// <summary>Свойство строки; пусто — меню всей цели.</summary>
    public abstract InspectorProperty? Property { get; }

    /// <summary>Значение свойства строки; пусто — меню всей цели.</summary>
    public abstract InspectorValue? Value { get; }

    /// <summary>
    /// Раздел, чьё меню ⋮ дополняют, — идентификатор раздела метаданных: <c>layout</c>; пусто — меню строки или всей
    /// цели.
    /// </summary>
    public virtual string? Category => null;

    /// <summary>Тип цели.</summary>
    public abstract InspectorTypeInfo Type { get; }

    /// <summary>Сколько объектов показано.</summary>
    public abstract int Count { get; }

    /// <summary>Добавляет пункт.</summary>
    /// <param name="title">Подпись — уже переведённая.</param>
    /// <param name="action">Что сделать.</param>
    /// <param name="icon">Значок — контур в клетке 16.</param>
    /// <param name="isEnabled">Можно ли выбрать.</param>
    public abstract void Add(string title, Action action, Geometry? icon = null, bool isEnabled = true);

    /// <summary>Добавляет черту между группами пунктов.</summary>
    public abstract void AddSeparator();

    /// <summary>Пишет значения — одной правкой, одним шагом истории.</summary>
    /// <param name="writes">Записи.</param>
    /// <param name="label">Как назвать шаг.</param>
    /// <returns>Записалось ли.</returns>
    public abstract Task<bool> WriteAsync(IReadOnlyList<InspectorWrite> writes, string? label = null);
}
