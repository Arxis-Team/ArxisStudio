using System.Globalization;
using ArxisStudio.Modules.Project.Dialogs;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Правка файлов из окна проекта: спросить человека, сделать службой файлов и сказать, чем кончилось.
/// </summary>
/// <remarks>
/// <para>
/// Диск окно не трогает само: дерево — снимок службы проектов, и правка в обход неё разошлась бы с
/// ним. Служба файлов переписывает ссылки в файлах проектов, перечитывает модель и пишет действие в
/// локальную историю, а окно спрашивает, передаёт и докладывает: удача — строкой состояния, отказ —
/// диалогом с причиной, которую назвала служба.
/// </para>
/// <para>
/// Правка идёт одна: пока служба занята, второе удаление или переименование не начинается — выбор
/// под ним мог уже уехать.
/// </para>
/// </remarks>
/// <param name="context">Контекст модуля: словари, строка состояния, журнал.</param>
/// <param name="files">Служба файлов.</param>
/// <param name="owner">Окно, которому принадлежат вопросы; пусто — спросить негде.</param>
internal sealed class Editing(IStudioContext context, IStudioFiles files, Func<Window?> owner)
{
    /// <summary>Сколько файлов папки считать для вопроса: дальше — «больше».</summary>
    internal const int CountLimit = 1000;

    private bool _busy;

    /// <summary>Идёт ли правка.</summary>
    public bool IsBusy => _busy;

    /// <summary>
    /// Спрашивает и удаляет выбранное.
    /// </summary>
    /// <param name="selection">Что удалить.</param>
    /// <returns>
    /// Удалено ли всё: тогда дерево перестроится без удалённого. Отказ посередине — удаление, которое
    /// успело уйти частью; о нём говорит диалог, а выделение остаётся тем, каким его оставит дерево.
    /// </returns>
    public async Task<bool> DeleteAsync(EditSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.IsEmpty || _busy || owner() is not { } window)
            return false;

        _busy = true;

        try
        {
            if (!await DeleteDialog.AskAsync(window, Question(selection, context.Strings, CountFiles)))
                return false;

            var first = selection.Roots[0].Name;
            var more = selection.Roots.Count - 1;
            var label = more == 0 ? Format("project.delete.label", first) : Format("project.delete.label.many", first, more);

            if (await Run(window, () => files.DeleteAsync(selection.Paths, label)) is not { HasErrors: false })
                return false;

            Tell(more == 0 ? Format("project.deleted", first) : Format("project.deleted.many", first, more));

            return true;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Спрашивает новое имя и переименовывает узел вместе с вложенными.
    /// </summary>
    /// <param name="node">Файл или папка.</param>
    /// <returns>Новый путь узла; пусто — переименования не было.</returns>
    public async Task<CanonicalPath?> RenameAsync(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!EditSelection.IsEditable(node) || _busy || owner() is not { } window)
            return null;

        _busy = true;

        try
        {
            if (await RenameDialog.AskAsync(window, node, context.Strings, Exists) is not { } name)
                return null;

            var moves = Renaming.Plan(node, name)
                .Select(step => new FileMove(step.Node.Path, step.Node.Path.Directory.Combine(step.Name)))
                .ToList();

            if (await Run(window, () => files.MoveAsync(moves, Format("project.rename.label", node.Name))) is not { HasErrors: false })
                return null;

            Tell(Format("project.renamed", node.Name, name));

            return moves[0].To;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Вопрос перед удалением: что уйдёт — по имени, сколько — числом.
    /// </summary>
    /// <param name="selection">Что удаляют.</param>
    /// <param name="strings">Словари модуля.</param>
    /// <param name="count">Сколько файлов в папке, самое большее <see cref="CountLimit"/> и ещё один; пусто — не сосчиталось.</param>
    /// <remarks>
    /// Имена, а не формы множественного числа: словари модулей их не знают, а число после двоеточия
    /// читается одинаково при любом числе.
    /// </remarks>
    internal static string Question(EditSelection selection, IStudioStrings strings, Func<CanonicalPath, int?> count)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(count);

        string Say(string key, params object[] values) => string.Format(CultureInfo.CurrentCulture, strings[key], values);

        if (selection.Roots is [var root])
        {
            if (root.Kind == NodeKind.Folder)
            {
                return count(root.Path) switch
                {
                    null => Say("project.delete.folder", root.Name),
                    > CountLimit => Say("project.delete.folder.count", root.Name, $"{CountLimit}+"),
                    var files => Say("project.delete.folder.count", root.Name, files),
                };
            }

            return EditSelection.Nested(root) switch
            {
                [] => Say("project.delete.file", root.Name),
                [var nested] => Say("project.delete.file.nested", root.Name, nested.Name),
                var nested => Say("project.delete.file.nestedMany", root.Name, nested.Count),
            };
        }

        return (selection.Files, selection.Folders) switch
        {
            (var files, 0) => Say("project.delete.files", files),
            (0, var folders) => Say("project.delete.folders", folders),
            var (files, folders) => Say("project.delete.mixed", files, folders),
        };
    }

    /// <summary>Сколько файлов в папке на любой глубине — с пределом: вопросу нужно «сколько», а не точная опись.</summary>
    internal static int? CountFiles(CanonicalPath folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder.Value, "*", SearchOption.AllDirectories).Take(CountLimit + 1).Count();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Зовёт службу и говорит об отказе; исключение службы — тоже отказ, а не падение окна.</summary>
    /// <returns>Итог службы; пусто — служба бросила.</returns>
    private async Task<ProjectOperationResult?> Run(Window window, Func<Task<ProjectOperationResult>> operation)
    {
        ProjectOperationResult result;

        try
        {
            result = await operation().ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            context.Log.Write(StudioLogLevel.Error, ProjectModule.LogSource, $"Правка файлов не удалась: {e.Message}");
            await FailureDialog.ShowAsync(window, e.Message).ConfigureAwait(true);

            return null;
        }

        if (result.HasErrors)
        {
            var reasons = result.Diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => diagnostic.Message);

            await FailureDialog.ShowAsync(window, string.Join(Environment.NewLine, reasons)).ConfigureAwait(true);
        }

        return result;
    }

    private void Tell(string message) => context.GetService<IStudioStatus>()?.Show(message);

    private string Format(string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, context.Strings[key], values);
}
