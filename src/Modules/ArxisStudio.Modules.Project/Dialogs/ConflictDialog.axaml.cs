using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;

namespace ArxisStudio.Modules.Project.Dialogs;

/// <summary>Как поступить с занятым именем.</summary>
public enum ConflictChoice
{
    /// <summary>Заменить: прежний файл уходит в локальную историю.</summary>
    Replace,

    /// <summary>Оставить оба: вставленное получает имя с номером.</summary>
    KeepBoth,

    /// <summary>Пропустить: это не вставлять.</summary>
    Skip,

    /// <summary>Бросить всю вставку.</summary>
    Cancel,
}

/// <summary>Ответ на конфликт имён.</summary>
/// <param name="Choice">Что делать.</param>
/// <param name="ForAll">Так же и с остальными конфликтами этой вставки.</param>
public readonly record struct ConflictAnswer(ConflictChoice Choice, bool ForAll);

/// <summary>Вставка встретила занятое имя.</summary>
public partial class ConflictDialog : AxDialog
{
    /// <summary>Собирает диалог из разметки.</summary>
    public ConflictDialog()
    {
        InitializeComponent();

        Cancel.Click += (_, _) => Close(new ConflictAnswer(ConflictChoice.Cancel, false));
        Skip.Click += (_, _) => Answer(ConflictChoice.Skip);
        Replace.Click += (_, _) => Answer(ConflictChoice.Replace);
        Both.Click += (_, _) => Answer(ConflictChoice.KeepBoth);
        Opened += (_, _) => Both.Focus(NavigationMethod.Tab);
    }

    /// <summary>Спрашивает, как поступить с занятым именем.</summary>
    /// <param name="owner">Окно, которому принадлежит вопрос.</param>
    /// <param name="strings">Словари модуля.</param>
    /// <param name="folder">Папка назначения.</param>
    /// <param name="name">Занятое имя.</param>
    /// <param name="canReplace">Можно ли заменить: у папки — нельзя.</param>
    /// <param name="more">Сколько конфликтов у этой вставки ещё впереди.</param>
    /// <returns>Ответ; Esc — отмена всей вставки.</returns>
    public static async Task<ConflictAnswer> AskAsync(
        Window owner, IStudioStrings strings, string folder, string name, bool canReplace, int more)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(strings);

        var dialog = new ConflictDialog();

        dialog.Question.Text = string.Format(CultureInfo.CurrentCulture, strings["project.conflict.question"], folder, name);
        dialog.Replace.IsEnabled = canReplace;
        dialog.Folders.IsVisible = !canReplace;
        dialog.ForAll.IsVisible = more > 0;
        dialog.ForAll.Content = string.Format(CultureInfo.CurrentCulture, strings["project.conflict.forAll"], more);

        return await dialog.ShowDialog<ConflictAnswer?>(owner) ?? new ConflictAnswer(ConflictChoice.Cancel, false);
    }

    private void Answer(ConflictChoice choice) => Close(new ConflictAnswer(choice, ForAll.IsChecked == true));
}
