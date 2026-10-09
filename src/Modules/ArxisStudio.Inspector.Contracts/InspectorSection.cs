using ArxisStudio.Sdk;
using Avalonia.Controls;

namespace ArxisStudio.Inspector;

/// <summary>Для целей какого типа раздел — как <c>[CustomEditor]</c> у Unity.</summary>
/// <remarks>Тип называют типом или полным именем — для типов проекта, которых у плагина нет.</remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class InspectorSectionAttribute : Attribute
{
    /// <summary>Раздел для целей этого типа.</summary>
    /// <param name="targetType">Тип цели.</param>
    public InspectorSectionAttribute(Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        TypeName = InspectorTypeInfo.NameOf(targetType);
    }

    /// <summary>Раздел для целей типа с этим полным именем.</summary>
    /// <param name="typeName">Полное имя типа цели.</param>
    public InspectorSectionAttribute(string typeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);

        TypeName = typeName;
    }

    /// <summary>Полное имя типа цели.</summary>
    public string TypeName { get; }

    /// <summary>И для наследников.</summary>
    public bool ForDerived { get; set; }
}

/// <summary>Где стоит раздел.</summary>
public enum InspectorSectionPlacement
{
    /// <summary>В списке, своим заголовком или внутри раздела метаданных (<see cref="InspectorSection.Category"/>).</summary>
    Body,

    /// <summary>В шапке, под именем цели: выравнивание, быстрые действия.</summary>
    Header,
}

/// <summary>
/// Раздел инспектора: свой интерфейс для целей типа — как <c>Editor</c> Unity, вместе с <c>DrawDefaultInspector</c>.
/// </summary>
/// <remarks>
/// <para>
/// Раздел встаёт рядом со строками по умолчанию, а строки, которые он показывает сам, называет в
/// <see cref="Claims"/> — их из списка убирают. <see cref="ReplaceAll"/> убирает все строки: раздел показывает
/// цель целиком. Строку по умолчанию раздел берёт у контекста (<see cref="InspectorSectionContext.CreateRow"/>)
/// — с тем же редактором, тем же источником значения и той же подписью, что в списке.
/// </para>
/// <para>
/// Как и редактор, раздел — вклад: студия находит его по <see cref="InspectorSectionAttribute"/>, строит через шов
/// и снимает с выгрузкой плагина, а его интерфейс ставит за барьер — упавшая раскладка раздела станет строкой
/// о сбое, а не уронит инспектор.
/// </para>
/// </remarks>
public abstract class InspectorSection : StudioContribution
{
    /// <summary>Заголовок раздела.</summary>
    public abstract string Title { get; }

    /// <summary>Где стоит раздел.</summary>
    public virtual InspectorSectionPlacement Placement => InspectorSectionPlacement.Body;

    /// <summary>Раздел метаданных, внутрь которого встать; пусто — свой заголовок.</summary>
    public virtual string? Category => null;

    /// <summary>Порядок среди разделов; равные — по заголовку.</summary>
    public virtual int Order => 0;

    /// <summary>Свойства, которые раздел показывает сам: из списка их убирают.</summary>
    public virtual IReadOnlyCollection<string> Claims => [];

    /// <summary>Раздел показывает цель целиком: строк по умолчанию нет вовсе.</summary>
    public virtual bool ReplaceAll => false;

    /// <summary>Берётся ли раздел за эту цель: тип совпал, а он смотрит ещё и на свойства.</summary>
    /// <param name="type">Тип цели.</param>
    /// <param name="properties">Её свойства.</param>
    public virtual bool Accepts(InspectorTypeInfo type, IReadOnlyList<InspectorProperty> properties) => true;

    /// <summary>Строит интерфейс раздела.</summary>
    /// <param name="context">Дорога к цели, которая показана сейчас.</param>
    public abstract InspectorSectionView CreateView(InspectorSectionContext context);
}

/// <summary>Интерфейс раздела.</summary>
public abstract class InspectorSectionView
{
    /// <summary>Контрол раздела — на контролах <c>Ax*</c>.</summary>
    public abstract Control View { get; }

    /// <summary>У цели что-то сменилось: значения, состав, сама цель.</summary>
    /// <param name="change">Что сменилось.</param>
    public virtual void Refresh(InspectorChangedEventArgs change)
    {
    }

    /// <summary>Раздел уходит: показана цель другого типа или раздел выгружен.</summary>
    public virtual void Release()
    {
    }
}

/// <summary>Дорога раздела к цели, которая показана сейчас.</summary>
/// <remarks>Даёт модуль инспектора; раздел зовёт его только в потоке интерфейса.</remarks>
public abstract class InspectorSectionContext
{
    /// <summary>Тип цели.</summary>
    public abstract InspectorTypeInfo Type { get; }

    /// <summary>Сколько объектов показано.</summary>
    public abstract int Count { get; }

    /// <summary>Свойства цели — с метаданными.</summary>
    public abstract IReadOnlyList<InspectorProperty> Properties { get; }

    /// <summary>Значение свойства.</summary>
    /// <param name="id">Свойство.</param>
    public abstract InspectorValue ValueOf(string id);

    /// <summary>Пишет значения — одной правкой, одним шагом истории.</summary>
    /// <param name="writes">Записи.</param>
    /// <param name="label">Как назвать шаг.</param>
    /// <returns>Записалось ли.</returns>
    public abstract Task<bool> WriteAsync(IReadOnlyList<InspectorWrite> writes, string? label = null);

    /// <summary>Строка свойства по умолчанию: подпись, поле редактора, источник значения.</summary>
    /// <param name="id">Свойство.</param>
    /// <returns>Строка; пусто — такого свойства у цели нет.</returns>
    /// <remarks>Строку раздел ставит к себе; уходит она вместе с разделом.</remarks>
    public abstract Control? CreateRow(string id);
}
