using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>
/// Сочетания правок строения формы — одной таблицей для клавиш холста и подписей меню.
/// </summary>
/// <remarks>
/// Те же, что у окна проекта и у Visual Studio: Ctrl+X, Ctrl+C, Ctrl+V, Delete; Ctrl+D — дубликат, как у
/// Figma и Rider. Клавиша, поменянная в одном месте, в меню осталась бы прежней, и пункт обещал бы
/// сочетание, которое не работает, — поэтому таблица одна.
/// </remarks>
internal static class FormKeys
{
    /// <summary>Вырезать выбранное.</summary>
    public static readonly KeyGesture Cut = new(Key.X, KeyModifiers.Control);

    /// <summary>Скопировать выбранное.</summary>
    public static readonly KeyGesture Copy = new(Key.C, KeyModifiers.Control);

    /// <summary>Вставить из буфера.</summary>
    public static readonly KeyGesture Paste = new(Key.V, KeyModifiers.Control);

    /// <summary>Поставить копию рядом.</summary>
    public static readonly KeyGesture Duplicate = new(Key.D, KeyModifiers.Control);

    /// <summary>Удалить выбранное: его ловит ядро холста и просит удаления.</summary>
    public static readonly KeyGesture Delete = new(Key.Delete);

    /// <summary>К родителю — Esc, как в дизайнерах Visual Studio.</summary>
    public static readonly KeyGesture Parent = new(Key.Escape);

    /// <summary>Нажато ли сочетание: та же клавиша и ровно те же модификаторы.</summary>
    /// <param name="e">Нажатие.</param>
    /// <param name="gesture">Сочетание из таблицы.</param>
    public static bool Is(this KeyEventArgs e, KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(gesture);

        return e.Key == gesture.Key && e.KeyModifiers == gesture.KeyModifiers;
    }

    /// <summary>Отмена или возврат — сочетаниями платформы, а не таблицы: у macOS они свои.</summary>
    /// <param name="e">Нажатие.</param>
    /// <param name="where">Где нажали: сочетания платформы спрашивают у него.</param>
    /// <returns><c>true</c> — отмена, <c>false</c> — возврат, null — ни то ни другое.</returns>
    public static bool? Step(this KeyEventArgs e, Visual where)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(where);

        if (where.GetPlatformSettings()?.HotkeyConfiguration is not { } keys)
            return null;

        if (keys.Undo.Any(gesture => gesture.Matches(e)))
            return true;

        return keys.Redo.Any(gesture => gesture.Matches(e)) ? false : null;
    }
}
