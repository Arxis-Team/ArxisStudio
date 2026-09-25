using ArxisStudio.Modules.Project.Model;
using ArxisStudio.ProjectSystem;
using Avalonia.Input;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Сочетания окна проекта — одной таблицей для дерева, правой колонки и подписей меню.
/// </summary>
/// <remarks>
/// Дерево и колонка разбирали одни и те же сочетания каждое своим перечнем, а меню подписывало их
/// третьим. Сочетание, поменянное в одном месте, в остальных осталось бы прежним, и пункт меню обещал
/// бы клавишу, которая не работает. Правку клавиши делают действиями меню (<see cref="MenuActions"/>):
/// той же дорогой, что пункт, и там же, где пункта нет, клавиши нет тоже.
/// </remarks>
internal static class EditKeys
{
    /// <summary>«Добавить ▸» отдельным меню — Alt+Insert, как «New…» у Rider.</summary>
    public static readonly KeyGesture Create = new(Key.Insert, KeyModifiers.Alt);

    /// <summary>Открыть файл; у контейнера — раскрыть или войти.</summary>
    public static readonly KeyGesture Open = new(Key.Enter);

    /// <summary>Скопировать полный путь.</summary>
    public static readonly KeyGesture CopyPath = new(Key.C, KeyModifiers.Control | KeyModifiers.Shift);

    /// <summary>Вырезать выбранное.</summary>
    public static readonly KeyGesture Cut = new(Key.X, KeyModifiers.Control);

    /// <summary>Скопировать выбранное.</summary>
    public static readonly KeyGesture Copy = new(Key.C, KeyModifiers.Control);

    /// <summary>Вставить в папку.</summary>
    public static readonly KeyGesture Paste = new(Key.V, KeyModifiers.Control);

    /// <summary>Удалить выбранное.</summary>
    public static readonly KeyGesture Delete = new(Key.Delete);

    /// <summary>Переименовать — F2, как в проводнике и VS Code: Shift+F6 Rider у студии занят обходом панелей.</summary>
    public static readonly KeyGesture Rename = new(Key.F2);

    /// <summary>Снять вырезанное, как Esc в проводнике.</summary>
    public static readonly KeyGesture Uncut = new(Key.Escape);

    /// <summary>Отменить последнее действие над файлами.</summary>
    public static readonly KeyGesture Undo = new(Key.Z, KeyModifiers.Control);

    /// <summary>Нажато ли сочетание: та же клавиша и ровно те же модификаторы.</summary>
    /// <param name="e">Нажатие.</param>
    /// <param name="gesture">Сочетание из таблицы.</param>
    public static bool Is(this KeyEventArgs e, KeyGesture gesture) =>
        e.Key == gesture.Key && e.KeyModifiers == gesture.KeyModifiers;

    /// <summary>Делает правку, если нажата её клавиша, — действием меню, как пункт.</summary>
    /// <param name="e">Нажатие.</param>
    /// <param name="actions">Действия окна.</param>
    /// <param name="selection">Что выбрано; спрашивается, только когда клавиша — правка выбора.</param>
    /// <param name="folder">Куда вставлять; пусто — вставлять некуда, и Ctrl+V не взят.</param>
    /// <param name="origin">Откуда правка: там потом и встанет выделение.</param>
    /// <returns>Взята ли клавиша: правки нет или делать нечего — клавиша идёт дальше.</returns>
    public static bool Press(
        KeyEventArgs e, MenuActions actions, Func<EditSelection> selection, Func<CanonicalPath?> folder, EditOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(folder);

        switch (e.Key)
        {
            case var _ when e.Is(Delete) && actions.Delete is { } delete:
                delete(selection(), origin);
                return true;
            case var _ when e.Is(Rename) && actions.Rename is { } rename:
                rename(selection(), origin);
                return true;
            case var _ when e.Is(Cut) && actions.CutFiles is { } cut:
                cut(selection(), origin);
                return true;
            case var _ when e.Is(Copy) && actions.CopyFiles is { } copy:
                copy(selection(), origin);
                return true;
            case var _ when e.Is(Paste) && actions.Paste is { } paste && folder() is { } target:
                paste(target, origin);
                return true;
            case var _ when e.Is(Uncut) && actions.Uncut?.Invoke() == true:
                return true;
            case var _ when e.Is(Undo) && actions.Undo is { } undo:
                undo(origin);
                return true;
            default:
                return false;
        }
    }
}
