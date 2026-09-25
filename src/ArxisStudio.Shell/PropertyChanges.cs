using System.ComponentModel;

namespace ArxisStudio.Shell;

/// <summary>
/// Извещение привязок о сменившихся свойствах модели.
/// </summary>
/// <remarks>
/// Модель зовёт его своим событием: <c>PropertyChanged.Raise(this, nameof(Rows), nameof(Refusals))</c>.
/// Строка на каждое свойство с ручной сборкой аргументов жила в каждой модели своей копией, и
/// извещение о нескольких свойствах читалось столбиком одинаковых строк.
/// </remarks>
public static class PropertyChanges
{
    /// <summary>Извещает о каждом названном свойстве — по очереди, в названном порядке.</summary>
    /// <param name="handler">Событие модели; null — слушать некому, и аргументы не строятся.</param>
    /// <param name="sender">Модель, чьи свойства сменились.</param>
    /// <param name="names">Имена свойств.</param>
    public static void Raise(this PropertyChangedEventHandler? handler, object sender, params ReadOnlySpan<string?> names)
    {
        if (handler is null)
            return;

        foreach (var name in names)
            handler(sender, new PropertyChangedEventArgs(name));
    }
}
