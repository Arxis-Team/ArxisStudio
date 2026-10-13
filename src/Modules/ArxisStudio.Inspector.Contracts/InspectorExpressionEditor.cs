using Avalonia.Controls;

namespace ArxisStudio.Inspector;

/// <summary>
/// Редактор выражения — содержимое панели под строкой: источник и путь привязки, ключ ресурса.
/// </summary>
/// <remarks>
/// <para>
/// Панель — инспектора: она встаёт под строкой, держит цель, пока открыта, закрывается по Esc и щелчку мимо и
/// возвращает каретку в строку. Редактор отвечает за то, что внутри, и за запись: <see cref="CommitAsync"/> зовут,
/// когда человек применил — кнопкой, Enter или просьбой самого редактора (<see cref="InspectorExpressionContext.Commit"/>).
/// </para>
/// <para>
/// Что записать нельзя, редактор говорит панели (<see cref="InspectorExpressionContext.Report"/>): она выключает
/// «Применить» и показывает причину под содержимым. Всё — в потоке интерфейса. Появилось в контракте 1.4.
/// </para>
/// </remarks>
public abstract class InspectorExpressionEditor
{
    /// <summary>Заголовок панели: «Привязка — Text».</summary>
    public abstract string Title { get; }

    /// <summary>Содержимое панели.</summary>
    public abstract Control View { get; }

    /// <summary>Куда встаёт каретка, когда панель открылась; пусто — в первое, что её берёт.</summary>
    public virtual Control? FocusTarget => null;

    /// <summary>Пишет выражение одной правкой.</summary>
    /// <returns>Записалось ли: панель закрывается, только если да.</returns>
    public abstract Task<bool> CommitAsync();

    /// <summary>Панель закрылась: редактор отпускает всё, что держал.</summary>
    public virtual void Release()
    {
    }
}

/// <summary>Дорога редактора выражения к панели, в которой он стоит.</summary>
/// <remarks>Появилось в контракте 1.4.</remarks>
public abstract class InspectorExpressionContext
{
    /// <summary>Свойство, чьё выражение правят.</summary>
    public abstract InspectorProperty Property { get; }

    /// <summary>Сколько объектов показано: выражение запишется каждому.</summary>
    public abstract int Count { get; }

    /// <summary>Можно ли записать то, что набрано: нельзя — «Применить» выключено, а причина — под содержимым.</summary>
    /// <param name="check">Проверка; <see cref="InspectorCheck.Valid"/> снимает заметку.</param>
    public abstract void Report(InspectorCheck check);

    /// <summary>Просит записать — то же, что «Применить».</summary>
    public abstract void Commit();

    /// <summary>Просит закрыть панель, ничего не записав.</summary>
    public abstract void Cancel();
}
