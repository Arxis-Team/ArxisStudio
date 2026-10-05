using System.Globalization;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.Modules.UiDesigner.Panels;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>
/// Редактор документов дизайнера: в режиме вкладок форма <c>.axaml</c> открывается своей вкладкой
/// с холстом на одну неё, в режиме доски — показывается на доске.
/// </summary>
/// <remarks>
/// Студия спрашивает редакторы по порядку, и модули — раньше плагинов. Поэтому режим решается здесь: во
/// вкладках дизайнер берёт форму сам и открывает вкладкой, а на доске берёт форму решения и показывает её
/// на доске (<see cref="RevealAsync"/>) — выбранной целиком и в кадре, — и вкладки нет. Форму вне решения
/// доска не знает, и в режиме доски её, как и прежде, открывает просмотрщик разметки. Откуда открыли — с
/// доски, из окна проекта или вернули вкладку после перезапуска, — значения не имеет.
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
    /// <summary>Сколько ждать доску, которую показали ради формы: её строит раскладка окна.</summary>
    private static readonly TimeSpan BoardWait = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    public override bool CanOpen(string filePath) =>
        IsMarkup(filePath)
        && FormRoot.Read(filePath) is { Kind: not FormKind.Unreadable }
        && (Tabs() || InSolution(CanonicalPath.Create(filePath)));

    /// <inheritdoc/>
    /// <remarks>
    /// Только в режиме доски и только форма открытого решения — других доска не знает. Доска встаёт на виду
    /// и берёт клавиатуру; не показала — форма открывается вкладкой.
    /// </remarks>
    public override async Task<bool> RevealAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        if (Tabs() || !IsMarkup(filePath) || FormRoot.Read(filePath) is not { Kind: not FormKind.Unreadable })
            return false;

        var path = CanonicalPath.Create(filePath);

        if (!InSolution(path))
            return false;

        Context.GetService<IStudioToolWindows>()?.Show(UiDesignerModule.PanelId);

        if (await Boards.Of(Context).ShownAsync(BoardWait) is not { } board || !await board.RevealAsync(path))
            return false;

        Context.GetService<IStudioFocus>()?.Focus(UiDesignerModule.PanelId);

        return true;
    }

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

    /// <summary>Режим вкладок: форму открывает вкладка, а не доска.</summary>
    private bool Tabs() => Context.Settings.Get<bool?>(UiDesignerModule.TabsKey) == true;

    /// <summary>Файл — часть открытого решения: служба XAML строит только его формы.</summary>
    private bool InSolution(CanonicalPath path) =>
        Context.Projects()?.Status.Snapshot is { } snapshot && snapshot.TryGetProjectForFile(path, out _);

    private static bool IsMarkup(string filePath) =>
        Path.GetExtension(filePath).Equals(FormFiles.Extension, StringComparison.OrdinalIgnoreCase);
}
