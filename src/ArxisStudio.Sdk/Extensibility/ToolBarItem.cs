using Avalonia.Controls;

namespace ArxisStudio.Sdk;

/// <summary>
/// Свой контрол плагина в полосе студии: элемент манифеста вида <c>custom</c>.
/// </summary>
/// <remarks>
/// Кнопку и меню студия рисует по манифесту сама; сюда приходят за тем, чего
/// манифестом не описать, — переключатель конфигурации, виджет запуска, поле.
/// Цена — плагин поднимается при старте студии: нарисовать чужой контрол, не
/// подняв плагин, нечем.
/// <para>
/// Полоса невысока — сорок пикселей, — и контрол обязан в неё помещаться:
/// иконочные кнопки в ней 24 на 24 (<c>AxControlHeightCompact</c>), текстовые —
/// класса <c>compact</c>. Строить его следует на контролах <c>Ax*</c>: свой
/// контрол стоит в одном ряду с кнопками студии, и разнобой виден сразу.
/// </para>
/// <para>
/// Содержимое создаётся один раз и по требованию — так же, как у панели.
/// </para>
/// </remarks>
public abstract class ToolBarItem
{
    private Control? _content;

    /// <summary>Что студия даёт плагину; доступен после <see cref="Attach"/>.</summary>
    protected IStudioContext Context { get; private set; } = null!;

    /// <summary>Контрол, который встанет в полосу.</summary>
    public Control Content => _content ??= Build();

    /// <summary>Связывает элемент со студией.</summary>
    /// <param name="context">Что студия даёт плагину.</param>
    public void Attach(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Context = context;
    }

    /// <summary>Строит контрол.</summary>
    /// <returns>Корневой контрол элемента.</returns>
    protected abstract Control Build();

    /// <summary>
    /// Элемент уходит: пора отпустить всё, что он держал.
    /// </summary>
    /// <remarks>
    /// То же и по тем же причинам, что у <see cref="ToolWindow.Release"/>:
    /// экземпляр создаёт студия, и дотянуться до него расширению неоткуда.
    /// Зовётся, когда расширение выключают, перед выгрузкой его сборки.
    /// </remarks>
    public virtual void Release()
    {
    }
}
