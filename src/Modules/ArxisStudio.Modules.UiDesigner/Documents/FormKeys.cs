using Avalonia.Input;

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
}
