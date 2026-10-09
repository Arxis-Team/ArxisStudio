using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Controls;

namespace ArxisStudio.Inspector;

/// <summary>Для значений какого типа редактор — как <c>[CustomPropertyDrawer]</c> у Unity.</summary>
/// <remarks>
/// Тип называют либо типом — <c>[PropertyEditor(typeof(Thickness))]</c>, — либо полным именем:
/// <c>[PropertyEditor("MyLib.Elevation")]</c> — для типов проекта, которых у плагина нет. Атрибутов может быть
/// несколько: один редактор — нескольким типам.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class PropertyEditorAttribute : Attribute
{
    /// <summary>Редактор значений этого типа.</summary>
    /// <param name="valueType">Тип значения.</param>
    public PropertyEditorAttribute(Type valueType)
    {
        ArgumentNullException.ThrowIfNull(valueType);

        TypeName = InspectorTypeInfo.NameOf(valueType);
    }

    /// <summary>Редактор значений типа с этим полным именем.</summary>
    /// <param name="typeName">Полное имя типа значения; у обобщённого — определение: <c>System.Collections.Generic.List`1</c>.</param>
    public PropertyEditorAttribute(string typeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);

        TypeName = typeName;
    }

    /// <summary>Полное имя типа значения.</summary>
    public string TypeName { get; }

    /// <summary>И для наследников: точный тип редактора наследника всё равно сильнее.</summary>
    public bool ForDerived { get; set; }

    /// <summary>Редактор по умолчанию: уступает любому другому той же точности.</summary>
    public bool Fallback { get; set; }

    /// <summary>Подсказка редактора — <see cref="InspectorProperty.Editor"/>; с ней тип не проверяется.</summary>
    public string? Hint { get; set; }
}

/// <summary>Как поле редактора стоит в строке.</summary>
public enum InspectorFieldLayout
{
    /// <summary>Рядом с подписью; тесно — под ней.</summary>
    Inline,

    /// <summary>Под подписью, во всю ширину.</summary>
    Below,

    /// <summary>Во всю ширину и без подписи: редактор подписывает себя сам.</summary>
    Full,
}

/// <summary>
/// Редактор свойства: строит поле для строки свойства — как <c>PropertyDrawer</c> Unity.
/// </summary>
/// <remarks>
/// <para>
/// Плагин наследует его открытым классом с открытым конструктором и помечает
/// <see cref="PropertyEditorAttribute"/>: студия найдёт его сама, построит через шов сбоев и снимет с выгрузкой
/// плагина. Поле строится своё у каждой строки (<see cref="CreateField"/>) и живёт, пока живёт строка: строки
/// переживают смену цели того же типа, и поле получает новое значение через <see cref="InspectorField.Show"/>.
/// </para>
/// <para>
/// Выбор редактора идёт как у Unity — от точного к общему: подсказка свойства, точный тип, обобщённое
/// определение, базы с <see cref="PropertyEditorAttribute.ForDerived"/>, интерфейсы, затем умолчания по виду
/// значения и текст. При равной точности побеждает не-<see cref="PropertyEditorAttribute.Fallback"/>, а среди
/// равных — плагин раньше модуля: свою настройку плагин ставит поверх студийной.
/// </para>
/// </remarks>
public abstract class PropertyEditor : StudioContribution
{
    /// <summary>Берётся ли редактор за это свойство: тип совпал, а он смотрит ещё и на ограничения.</summary>
    /// <param name="property">Свойство.</param>
    public virtual bool Accepts(InspectorProperty property) => true;

    /// <summary>Как поле стоит в строке.</summary>
    /// <param name="property">Свойство.</param>
    public virtual InspectorFieldLayout LayoutOf(InspectorProperty property) => InspectorFieldLayout.Inline;

    /// <summary>Строит поле для строки.</summary>
    /// <param name="context">Свойство строки и дорога к цели.</param>
    public abstract InspectorField CreateField(InspectorFieldContext context);
}

/// <summary>Поле строки свойства, построенное редактором.</summary>
/// <remarks>
/// Поле пишет только через свой контекст (<see cref="InspectorFieldContext"/>) — цели оно не видит: строка
/// переживает смену цели, и запись уходит той цели, что показана сейчас.
/// </remarks>
public abstract class InspectorField
{
    /// <summary>Контрол поля — на контролах <c>Ax*</c>.</summary>
    public abstract Control View { get; }

    /// <summary>Куда ставить каретку, когда строку открывают Enter или F2; пусто — в само поле.</summary>
    public virtual Control? FocusTarget => null;

    /// <summary>Число, которое тянет подпись строки, как у Unity; пусто — подпись не тянет.</summary>
    public virtual AxNumberField? ScrubTarget => null;

    /// <summary>В поле набрано то, что ещё не стало значением: обновление снаружи его не затирает.</summary>
    public virtual bool HasDraft => false;

    /// <summary>Показывает значение.</summary>
    /// <param name="value">Значение у цели.</param>
    public abstract void Show(InspectorValue value);

    /// <summary>Поле уходит: строку убрали или редактор выгружен.</summary>
    public virtual void Release()
    {
    }
}

/// <summary>Свойство строки и дорога к цели, которая показана сейчас.</summary>
/// <remarks>Даёт модуль инспектора; поле зовёт его только в потоке интерфейса.</remarks>
public abstract class InspectorFieldContext
{
    /// <summary>Свойство строки.</summary>
    public abstract InspectorProperty Property { get; }

    /// <summary>Сколько объектов показано: у нескольких значения могут расходиться.</summary>
    public abstract int Count { get; }

    /// <summary>Пишет значение — одной правкой.</summary>
    /// <param name="text">Текст значения; пусто — сбросить.</param>
    /// <returns>Записалось ли.</returns>
    public abstract Task<bool> WriteAsync(string? text);

    /// <summary>Прочтётся ли текст значением.</summary>
    /// <param name="text">Текст.</param>
    public abstract InspectorCheck Check(string text);

    /// <summary>
    /// Говорит строке, годится ли набранное: непрочтённое строка показывает заметкой под полем и отметкой ошибки, а
    /// черновик остаётся в поле.
    /// </summary>
    /// <param name="check">Что сказала проверка; <see cref="InspectorCheck.Valid"/> снимает заметку.</param>
    public abstract void Report(InspectorCheck check);

    /// <summary>Начинает предпросмотр: значение видно на цели, а пишется одно — при отпускании.</summary>
    /// <returns>Предпросмотр; пусто — цель его не умеет.</returns>
    public abstract IInspectorPreview? BeginPreview();

    /// <summary>Держит цель, пока в поле набрано своё.</summary>
    /// <returns>Отпускание; пусто — держать нечего.</returns>
    public abstract IDisposable? Hold();

    /// <summary>Подсказки к набору.</summary>
    /// <param name="typed">Набранное.</param>
    public abstract IReadOnlyList<InspectorChoice> Suggest(string typed);
}
