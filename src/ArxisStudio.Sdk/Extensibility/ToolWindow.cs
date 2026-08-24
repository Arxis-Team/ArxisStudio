using Avalonia.Controls;

namespace ArxisStudio.Sdk;

/// <summary>
/// Панель плагина: студия ставит её содержимое в объявленную зону.
/// </summary>
/// <remarks>
/// Содержимое создаётся один раз и по требованию — панель, которую никто не
/// открыл, не должна строить свой интерфейс. Строить его следует на контролах
/// <c>Ax*</c>: панели плагина и панели студии стоят рядом, и разнобой видно
/// сразу.
/// </remarks>
public abstract class ToolWindow
{
    private Control? _content;

    /// <summary>Что студия даёт плагину; доступен после <see cref="Attach"/>.</summary>
    protected IStudioContext Context { get; private set; } = null!;

    /// <summary>Содержимое панели.</summary>
    public Control Content => _content ??= Build();

    /// <summary>Связывает панель со студией.</summary>
    /// <param name="context">Что студия даёт плагину.</param>
    public void Attach(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Context = context;
    }

    /// <summary>Строит интерфейс панели.</summary>
    /// <returns>Корневой контрол панели.</returns>
    protected abstract Control Build();
}
