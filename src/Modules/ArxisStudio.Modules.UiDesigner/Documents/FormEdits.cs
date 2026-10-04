using System.Globalization;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>
/// Правки формы по путям элементов: удаление, перестановка, размер и место, отмена — то, что делают жесты
/// холста и правки строения.
/// </summary>
/// <remarks>
/// <para>
/// <b>Одна правка — один шаг истории документа.</b> Удаление трёх выбранных элементов — одна правка,
/// и Ctrl+Z отменяет её целиком.
/// </para>
/// <para>
/// <b>Элементы — в правке.</b> Путь разрешается уже внутри правки, в тексте, каким его оставили правки
/// перед ней: правки идут очередью документа, а элемент прежнего разбора редактор отвергнет.
/// </para>
/// <para>
/// Правки строения — буфер обмена, дубликат, обёртка — в <c>FormEdits.Structure.cs</c>.
/// </para>
/// </remarks>
internal sealed partial class FormEdits
{
    private readonly IXamlDocumentHandle _document;
    private readonly IStudioStrings _strings;
    private readonly Action<IReadOnlyList<XamlElementPath>> _select;
    private readonly Action<string> _refused;

    /// <summary>Правки документа формы.</summary>
    /// <param name="document">Документ формы.</param>
    /// <param name="strings">Словарь модуля: имена шагов истории.</param>
    /// <param name="select">Что выбрать, когда правка, сдвинувшая пути, показана.</param>
    /// <param name="refused">Куда сказать, что правка не записана.</param>
    public FormEdits(
        IXamlDocumentHandle document,
        IStudioStrings strings,
        Action<IReadOnlyList<XamlElementPath>> select,
        Action<string> refused)
    {
        _document = document;
        _strings = strings;
        _select = select;
        _refused = refused;
    }

    /// <summary>Документ формы.</summary>
    public IXamlDocumentHandle Document => _document;

    /// <summary>Правит документ; правка показана — выбирает, что сказано.</summary>
    /// <param name="label">Имя шага истории.</param>
    /// <param name="edit">Правка.</param>
    /// <param name="select">Что выбрать после неё; null — выбор остаётся.</param>
    /// <returns>Изменился ли текст.</returns>
    public async Task<bool> EditAsync(string label, Action<XamlDocumentEditor> edit, IReadOnlyList<XamlElementPath>? select)
    {
        try
        {
            var outcome = await _document.EditAsync(label, edit);

            if (outcome.TextChanged && select is { Count: > 0 })
                _select(select);

            return outcome.TextChanged;
        }
        catch (ObjectDisposedException)
        {
            // Документ закрыт, пока шла правка: писать некуда.
            return false;
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            // Редактор отказал: правка, которой синтаксис не допускает, или перекрытые правки. В тексте не
            // изменилось ничего, и человеку говорится, почему правка не записана.
            _refused(e.Message);

            return false;
        }
    }

    /// <summary>Отменяет или возвращает шаг истории документа.</summary>
    /// <param name="back">Отменить, а не вернуть.</param>
    public async Task StepAsync(bool back)
    {
        try
        {
            _ = back ? await _document.UndoAsync() : await _document.RedoAsync();
        }
        catch (ObjectDisposedException)
        {
            // Документ закрыт: отменять нечего.
        }
    }

    /// <summary>Убирает элементы из документа одной правкой; корень остаётся.</summary>
    /// <param name="paths">Пути элементов.</param>
    /// <remarks>Удалённое выбрать нельзя: выбор переходит к тому, в чём стоял первый, — его путь удаление не двигает.</remarks>
    public Task DeleteAsync(IReadOnlyList<XamlElementPath> paths) => RemoveAsync(paths, "form.edit.delete", "form.edit.deleteMany");

    /// <summary>Убирает элементы одной правкой, назвав шаг истории так, как его назвал жест.</summary>
    /// <param name="paths">Пути элементов.</param>
    /// <param name="one">Ключ имени шага для одного элемента.</param>
    /// <param name="many">Ключ имени шага для нескольких.</param>
    private Task RemoveAsync(IReadOnlyList<XamlElementPath> paths, string one, string many)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var doomed = paths.Where(path => !path.Equals(XamlElementPath.Root)).Distinct().ToList();

        if (doomed.Count == 0)
            return Task.CompletedTask;

        var label = doomed.Count == 1
            ? Format(one, NameOf(doomed[0]))
            : string.Format(CultureInfo.CurrentCulture, _strings[many], doomed.Count);

        var after = doomed.Select(path => path.Parent ?? XamlElementPath.Root).Take(1).ToList();

        return EditAsync(label, editor =>
        {
            // Все элементы — из одного текста и до удаления: пути разрешены против него. Вложенное в
            // удаляемое уходит вместе с ним и отдельной правкой не пишется — правки перекрылись бы.
            var elements = doomed.Select(path => path.Resolve(editor.Document)).OfType<XamlElement>().ToList();

            foreach (var element in elements.Where(element => !elements.Any(other => IsInside(element, other))))
                editor.RemoveElement(element);
        }, after);
    }

    /// <summary>Ставит элемент перед соседом; без соседа — в конец содержимого родителя.</summary>
    /// <param name="path">Путь элемента.</param>
    /// <param name="anchor">Сосед, перед которым он встанет; null — в конец.</param>
    /// <remarks>
    /// Якорь, а не индекс: холст считает детей панели, документ — элементы содержимого, и у сетки с
    /// определениями строк они расходятся. Сосед, перед которым встаёт элемент, переживает разницу.
    /// </remarks>
    public Task MoveAsync(XamlElementPath path, XamlElementPath? anchor)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Steps.IsEmpty)
            return Task.CompletedTask;

        var moved = new List<XamlElementPath>(1);

        return EditAsync(Format("form.edit.move", NameOf(path)), editor =>
        {
            if (path.Resolve(editor.Document) is not { Parent: XamlElement parent } element)
                return;

            var siblings = parent.ContentElements.ToList();
            var from = siblings.IndexOf(element);
            var to = anchor?.Resolve(editor.Document) is { } at ? siblings.IndexOf(at) : siblings.Count;

            if (from < 0 || to < 0 || to == from || to == from + 1)
                return;

            editor.MoveElement(element, parent, to);

            // Перенос — удаление и вставка против прежнего текста: стоявший выше места встаёт на одну позицию
            // раньше названной.
            moved.Add(Sibling(path, to > from ? to - 1 : to));
        }, moved);
    }

    /// <summary>Как назвать элемент в имени шага: тип, а с именем — тип и имя.</summary>
    public string NameOf(XamlElementPath path) =>
        path.Resolve(_document.Syntax) is { } element
            ? element.Identity is { Length: > 0 } name ? $"{element.Name.LocalName} «{name}»" : element.Name.LocalName
            : path.ToString();

    /// <summary>
    /// Размер корня пишется туда, откуда его берёт дизайнер.
    /// </summary>
    /// <remarks>
    /// Шаблоны пишут окна с <c>d:DesignWidth</c> и <c>d:DesignHeight</c> и без <c>Width</c>, а в режиме
    /// дизайна побеждают именно они: размер, записанный в <c>Width</c>, лёг бы в файл, а живое окно
    /// вернулось бы к размеру дизайна с первым же обновлением. Поэтому элемент, назвавший размер дизайна,
    /// получает его; обычный атрибут — тоже, если он уже написан: двух разных чисел об одном в файле не
    /// остаётся.
    /// </remarks>
    public static void Size(XamlDocumentEditor editor, XamlElement element, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(element);

        var design = element.DesignTimeAttributes.FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, "Design" + name, StringComparison.Ordinal));

        if (design is not null)
        {
            if (value.Length == 0)
                editor.RemoveAttribute(element, design.Name);
            else
                editor.SetAttribute(element, design.Name, value);
        }

        if (design is null || element.GetAttribute(name) is not null)
        {
            if (value.Length == 0)
                editor.RemoveAttribute(element, XamlQualifiedName.Parse(name));
            else
                editor.SetAttribute(element, XamlQualifiedName.Parse(name), value);
        }
    }

    /// <summary>Путь соседа: тот же родитель, другое место среди его содержимого.</summary>
    public static XamlElementPath Sibling(XamlElementPath path, int index)
    {
        ArgumentNullException.ThrowIfNull(path);

        var last = path.Steps[^1];
        var parent = path.Parent is { Steps.IsEmpty: false } above ? above.ToString() : string.Empty;

        return XamlElementPath.Parse(last.MemberName is null
            ? string.Create(CultureInfo.InvariantCulture, $"{parent}/{index}")
            : string.Create(CultureInfo.InvariantCulture, $"{parent}/{last.MemberName}:{index}"));
    }

    private string Format(string key, string value) =>
        string.Format(CultureInfo.CurrentCulture, _strings[key], value);

    /// <summary>Лежит ли элемент внутри другого.</summary>
    private static bool IsInside(XamlElement element, XamlElement other)
    {
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, other))
                return true;
        }

        return false;
    }
}
