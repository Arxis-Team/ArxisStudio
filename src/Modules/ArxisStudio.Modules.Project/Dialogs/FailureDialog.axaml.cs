using ArxisStudio.Controls;
using ArxisStudio.ProjectSystem;
using Avalonia.Controls;
using Avalonia.Input;

namespace ArxisStudio.Modules.Project.Dialogs;

/// <summary>Правка файлов не удалась — и почему.</summary>
public partial class FailureDialog : AxDialog
{
    /// <summary>Собирает диалог из разметки.</summary>
    public FailureDialog()
    {
        InitializeComponent();

        Dismiss.Click += (_, _) => Close();
        Opened += (_, _) => Dismiss.Focus(NavigationMethod.Tab);
    }

    /// <summary>Говорит, что не получилось.</summary>
    /// <param name="owner">Окно, которому принадлежит диалог.</param>
    /// <param name="message">Причина.</param>
    /// <param name="title">Заголовок; пусто — «Не получилось». Не получилось частью — у отмены, вернувшей не всё, — так и говорят.</param>
    public static Task ShowAsync(Window owner, string message, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var dialog = new FailureDialog();

        dialog.Message.Text = message;

        if (title is not null)
            dialog.Title = title;

        return dialog.ShowDialog(owner);
    }

    /// <summary>
    /// Говорит, чем кончилось дело службы: отказ — причинами, которые она назвала, по строке на каждую;
    /// удача, вернувшая не всё, — тем, что не вернулось.
    /// </summary>
    /// <param name="owner">Окно, которому принадлежит диалог.</param>
    /// <param name="result">Итог службы.</param>
    /// <param name="partial">Заголовок удачи, вернувшей не всё; пусто — о ней молчат.</param>
    /// <remarks>
    /// Что не вернулось, служба говорит предупреждениями; сведения удачу неполной не делают. Отказ и
    /// неполную удачу говорили двое — правка окна проекта и окно истории — каждый своей копией, и
    /// копии разошлись: окно истории считало неполной и удачу со сведениями.
    /// </remarks>
    public static async Task ReportAsync(Window owner, ProjectOperationResult result, string? partial = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.HasErrors)
            await ShowAsync(owner, Messages(result, ProjectDiagnosticSeverity.Error)).ConfigureAwait(true);
        else if (partial is not null && result.Diagnostics.Any(diagnostic => diagnostic.Severity == ProjectDiagnosticSeverity.Warning))
            await ShowAsync(owner, Messages(result, ProjectDiagnosticSeverity.Warning), partial).ConfigureAwait(true);
    }

    /// <summary>Сказанное службой с этой строгостью — по строке на находку.</summary>
    private static string Messages(ProjectOperationResult result, ProjectDiagnosticSeverity severity) =>
        string.Join(Environment.NewLine, result.Diagnostics.Where(diagnostic => diagnostic.Severity == severity).Select(diagnostic => diagnostic.Message));
}
