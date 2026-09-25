using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Dialogs;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ArxisStudio.Modules.Project.History;

/// <summary>
/// Окно локальной истории файла или папки: строки истории, разница и то, что с ними делают.
/// </summary>
/// <remarks>
/// Вопросы и отказы окно задаёт от себя: оно не модальное, и диалог, привязанный к главному окну,
/// встал бы за ним. Отмена спрашивает так же, как Ctrl+Z в окне проекта; возврат не спрашивает — он
/// сам действие истории и отменяется, как всякое.
/// </remarks>
public partial class HistoryWindow : AxWindow
{
    private IStudioStrings? _strings;
    private bool _busy;

    /// <summary>Собирает окно из разметки.</summary>
    public HistoryWindow()
    {
        InitializeComponent();

        Revert.Click += OnRevert;
        Undo.Click += OnUndo;
        PutLabel.Click += OnLabel;
        Previous.Click += (_, _) => Move(forward: false);
        Next.Click += (_, _) => Move(forward: true);
        KeyDown += OnKeyDown;
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    /// <summary>Модель окна.</summary>
    internal HistoryModel? Model => DataContext as HistoryModel;

    /// <summary>Последнее начатое дело окна — тестам, чтобы ждать его, а не время.</summary>
    internal Task Work { get; private set; } = Task.CompletedTask;

    /// <summary>Показывает историю рядом с окном студии и начинает её читать.</summary>
    /// <param name="owner">Окно студии.</param>
    /// <param name="model">История пути.</param>
    /// <param name="strings">Словари модуля: вопросы и отказы.</param>
    internal static HistoryWindow Open(Window owner, HistoryModel model, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(model);

        var window = new HistoryWindow { DataContext = model, _strings = strings };

        window.Show(owner);
        window.Work = model.LoadAsync();

        return window;
    }

    /// <summary>Встаёт на следующее или предыдущее изменение разницы.</summary>
    /// <param name="forward">Вперёд.</param>
    internal void Move(bool forward)
    {
        if (Model is not { } model || model.NextChange(Diff.SelectedIndex, forward) is not (>= 0 and var at))
            return;

        Diff.SelectedIndex = at;
        Diff.ScrollIntoView(at);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F7 when e.KeyModifiers == KeyModifiers.None:
                Move(forward: true);
                break;
            case Key.F7 when e.KeyModifiers == KeyModifiers.Shift:
                Move(forward: false);
                break;
            case Key.Escape when e.KeyModifiers == KeyModifiers.None:
                Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnRevert(object? sender, RoutedEventArgs e) =>
        Work = Run(model => model.RevertAsync());

    private void OnUndo(object? sender, RoutedEventArgs e) =>
        Work = Run(async model =>
        {
            if (model.Selected is not { } row || _strings is null
                || !await UndoDialog.AskAsync(this, _strings, row.Revision.Action.Label).ConfigureAwait(true))
            {
                return null;
            }

            return await model.UndoAsync().ConfigureAwait(true);
        });

    private void OnLabel(object? sender, RoutedEventArgs e) =>
        Work = Run(async model =>
            await LabelDialog.AskAsync(this).ConfigureAwait(true) is { } text
                ? await model.LabelAsync(text).ConfigureAwait(true)
                : null);

    /// <summary>
    /// Делает дело службы и говорит, чем кончилось (<see cref="FailureDialog.ReportAsync"/>): отказ —
    /// диалогом с причиной, удача, вернувшая не всё, — тем же диалогом с тем, что не вернулось.
    /// </summary>
    private async Task Run(Func<HistoryModel, Task<ProjectOperationResult?>> operation)
    {
        if (_busy || Model is not { } model)
            return;

        _busy = true;

        try
        {
            if (await operation(model).ConfigureAwait(true) is { } result)
                await FailureDialog.ReportAsync(this, result, _strings?["project.undo.partial"]).ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await FailureDialog.ShowAsync(this, error.Message).ConfigureAwait(true);
        }
        finally
        {
            _busy = false;
        }
    }
}
