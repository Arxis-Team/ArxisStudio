using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Reactive;

namespace ArxisStudio.Modules.Project.Dialogs;

/// <summary>
/// Новое имя файла или папки — вместе с вложенными, которые его подхватят.
/// </summary>
/// <remarks>
/// Проверка идёт на каждую букву тем же правилом, каким окно проверит имя перед службой
/// (<see cref="Renaming.Check"/>): кнопка знает то же, что строка под полем, и Enter с негодным
/// именем не делает ничего.
/// </remarks>
public partial class RenameDialog : AxDialog
{
    private Node? _node;
    private IStudioStrings? _strings;
    private Func<string, bool> _exists = static _ => false;

    /// <summary>Собирает диалог из разметки.</summary>
    public RenameDialog()
    {
        InitializeComponent();

        // Слушается свойство, а не правка: имя ставит и сам диалог, открываясь.
        Chosen.GetObservable(TextBox.TextProperty).Subscribe(new AnonymousObserver<string?>(_ => Refresh()));

        Opened += (_, _) =>
        {
            Chosen.Focus();

            if (_node is { } node && Chosen.Text is { } text)
            {
                Chosen.SelectionStart = 0;
                Chosen.SelectionEnd = Math.Min(text.Length, Renaming.Selected(node.Name, node.Kind == NodeKind.Folder));
            }
        };

        Cancel.Click += (_, _) => Close(null);
        Confirm.Click += (_, _) => Submit();

        Form.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter)
            {
                Submit();
                key.Handled = true;
            }
        };
    }

    /// <summary>
    /// Спрашивает новое имя.
    /// </summary>
    /// <param name="owner">Окно, которому принадлежит диалог.</param>
    /// <param name="node">Что переименовывают.</param>
    /// <param name="strings">Словари модуля: подписи и то, что не так с именем.</param>
    /// <param name="exists">Есть ли на диске такой путь.</param>
    /// <returns>Новое имя; null — человек передумал.</returns>
    internal static Task<string?> AskAsync(Window owner, Node node, IStudioStrings strings, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var dialog = new RenameDialog();

        dialog.Prepare(node, strings, exists);

        return dialog.ShowDialog<string?>(owner);
    }

    /// <summary>Ставит диалогу то, что переименовывают; поле получает нынешнее имя.</summary>
    internal void Prepare(Node node, IStudioStrings strings, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(exists);

        _node = node;
        _strings = strings;
        _exists = exists;

        Prompt.Text = Format(node.Kind == NodeKind.Folder ? "project.rename.prompt.folder" : "project.rename.prompt", node.Name);
        Chosen.Text = node.Name;
        Refresh();
    }

    private void Refresh()
    {
        if (_node is not { } node)
            return;

        var typed = Chosen.Text ?? string.Empty;
        var check = Renaming.Check(typed, node, _exists);

        Confirm.IsEnabled = check.IsFine;
        Problem.Text = Say(check);
        Problem.IsVisible = Problem.Text is not null;

        // Вложенные показываются и при занятом имени: занятым может оказаться как раз имя вложенного.
        var along = check.Problem is NameProblem.None or NameProblem.Taken
            ? Renaming.Plan(node, typed).Skip(1).ToList()
            : [];

        Pairs.Children.Clear();

        foreach (var (nested, name) in along)
        {
            Pairs.Children.Add(new TextBlock
            {
                Text = $"{nested.Name} → {name}",
                TextWrapping = TextWrapping.Wrap,
            });
        }

        Along.IsVisible = along.Count > 0;
    }

    /// <summary>Что сказать о негодном имени; null — говорить нечего.</summary>
    private string? Say(NameCheck check) => check.Problem switch
    {
        NameProblem.Empty => Format("project.rename.empty"),
        NameProblem.Invalid => Format("project.rename.invalid", check.Subject ?? string.Empty),
        NameProblem.Trailing => Format("project.rename.trailing"),
        NameProblem.Reserved => Format("project.rename.reserved", check.Subject ?? string.Empty),
        NameProblem.Taken => Format("project.rename.taken", check.Subject ?? string.Empty),
        _ => null,
    };

    private string Format(string key, params object[] values) =>
        _strings is null ? key : string.Format(CultureInfo.CurrentCulture, _strings[key], values);

    private void Submit()
    {
        if (Confirm.IsEnabled && Chosen.Text is { } chosen)
            Close(chosen);
    }
}
