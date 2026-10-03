using System.Globalization;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>
/// Редактор документов дизайнера: в режиме вкладок форма <c>.axaml</c> открывается своей вкладкой
/// с холстом на одну неё.
/// </summary>
/// <remarks>
/// Студия спрашивает редакторы по порядку, и модули — раньше плагинов. Поэтому режим решается здесь, в
/// ответе на вопрос «возьмёшь файл?»: во вкладках дизайнер берёт форму сам, на доске отказывается, и
/// файл, как и прежде, открывает тот, кто открывал его до дизайнера, — просмотрщик разметки. Откуда
/// открыли — с доски, из окна проекта или вернули вкладку после перезапуска, — значения не имеет.
/// <para>
/// Формой считается то, что считает ею доска: окно, пользовательский элемент, другой контрол в корне.
/// Приложение и словари стилей дизайнер не берёт и в режиме вкладок, а разметку с нечитаемым корнем
/// отдаёт тексту — сломанное чинят там, где его видно.
/// </para>
/// <para>
/// Вкладка живая (<see cref="LiveFormDocument"/>), когда у студии есть служба XAML, а форма — файл
/// открытого решения: строить её не из чего, кроме типов проекта. Иначе — рамка её размера
/// (<see cref="FormDocument"/>), как до службы.
/// </para>
/// <para>
/// Вкладка студии одна на файл: открытая прежде вкладка того же файла — текстом или формой — выводится
/// вперёд, а не открывается второй, в каком бы режиме дизайнер ни был.
/// </para>
/// </remarks>
public sealed class FormEditor : DocumentEditor
{
    /// <inheritdoc/>
    public override bool CanOpen(string filePath) =>
        IsMarkup(filePath)
        && Context.Settings.Get<bool?>(UiDesignerModule.TabsKey) == true
        && FormRoot.Read(filePath) is { Kind: not FormKind.Unreadable };

    /// <inheritdoc/>
    public override Task<(DocumentView? View, string? Error)> OpenAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        // Между вопросом и открытием файл мог смениться: спрошенное читается заново, а не помнится.
        if (FormRoot.Read(filePath) is not { Kind: not FormKind.Unreadable } root)
        {
            var problem = string.Format(
                CultureInfo.CurrentCulture, Context.Strings["form.notForm"], Path.GetFileName(filePath));

            return Task.FromResult<(DocumentView?, string?)>((null, problem));
        }

        var path = CanonicalPath.Create(filePath);

        DocumentView view = Context.XamlDocuments() is { } documents && InSolution(path)
            ? new LiveFormDocument(Context, documents, path, root, Context.GetService<UiDesignerOptions>() ?? UiDesignerOptions.Default)
            : new FormDocument(Context, filePath, root);

        return Task.FromResult<(DocumentView?, string?)>((view, null));
    }

    /// <summary>Файл — часть открытого решения: служба XAML строит только его формы.</summary>
    private bool InSolution(CanonicalPath path) =>
        Context.Projects()?.Status.Snapshot is { } snapshot && snapshot.TryGetProjectForFile(path, out _);

    private static bool IsMarkup(string filePath) =>
        Path.GetExtension(filePath).Equals(FormFiles.Extension, StringComparison.OrdinalIgnoreCase);
}
