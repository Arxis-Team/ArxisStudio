using System.Globalization;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>Запись члена элемента: как его называет тот, кто правит, и что записать.</summary>
/// <param name="Member">
/// Имя члена: <c>Width</c>, присоединённый — <c>Grid.Row</c>, директива имени — <c>x:Name</c>, член для дизайна —
/// <c>d:DesignWidth</c>.
/// </param>
/// <param name="Text">Значение текстом; null — снять: член вернётся к тому, что дадут стиль, наследование и умолчание.</param>
/// <param name="OwnerNamespace">Пространство разметки владельца присоединённого члена; null — свой член.</param>
/// <param name="OwnerPrefix">Префикс, который предлагает сборка владельца, — если пространство придётся объявить.</param>
/// <param name="Owners">
/// Имена, которыми документ может написать свой член длинной записью, — тип элемента и его базы, коротко:
/// <c>&lt;Window.DataContext&gt;</c>, <c>&lt;Control.DataContext&gt;</c>; null — никакие, кроме имени самого элемента
/// (<see cref="FormEdits.PropertyElement"/>).
/// </param>
internal sealed record FormWrite(
    string Member,
    string? Text,
    string? OwnerNamespace = null,
    string? OwnerPrefix = null,
    IReadOnlyCollection<string>? Owners = null);

/// <summary>Запись членов элементов — правка инспектора.</summary>
internal sealed partial class FormEdits
{
    /// <summary>Директива имени: так документ называет элемент для кода за разметкой.</summary>
    public const string NameDirective = "x:Name";

    /// <summary>Приставка членов для дизайна: <c>d:DesignWidth</c>.</summary>
    public const string DesignPrefix = "d:";

    /// <summary>Выравнивание и размер вдоль него: <c>Stretch</c> первого отпускает второй.</summary>
    private static readonly (string Alignment, string Size)[] Stretches =
        [("HorizontalAlignment", "Width"), ("VerticalAlignment", "Height")];

    /// <summary>
    /// Пишет члены в каждый элемент одной правкой — одним шагом истории, который отменяется целиком.
    /// </summary>
    /// <param name="paths">Пути элементов.</param>
    /// <param name="writes">Записи.</param>
    /// <param name="label">Имя шага истории; null — по записям.</param>
    /// <returns>Изменился ли текст.</returns>
    /// <remarks>
    /// <para>
    /// Член пишется так, как его прочтёт загрузка. Имя — директивой под приставкой, которую документ дал
    /// пространству XAML. Присоединённый — с приставкой пространства своего владельца, объявленной на корне, если
    /// её нет: <c>c:Card.Elevation</c>, а не голым именем, которое загрузка искала бы среди контролов Avalonia.
    /// Член для дизайна — под приставкой дизайна, и её пространство той же правкой становится игнорируемым:
    /// <c>d:</c>, о котором компилятору не сказали, — ошибка сборки. Размер корня — туда, откуда его берёт
    /// дизайнер (<see cref="Size"/>).
    /// </para>
    /// <para>
    /// Выравнивание <c>Stretch</c> той же правкой отпускает размер вдоль себя: тянуть элемент с заданной шириной
    /// нельзя, и <c>Stretch</c> рядом с <c>Width</c> только центрировал бы его.
    /// </para>
    /// </remarks>
    public Task<bool> SetAsync(IReadOnlyList<XamlElementPath> paths, IReadOnlyList<FormWrite> writes, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(writes);

        var targets = paths.Distinct().ToList();

        if (targets.Count == 0 || writes.Count == 0)
            return Task.FromResult(false);

        return EditAsync(label ?? Label(targets, writes), editor =>
        {
            foreach (var path in targets)
            {
                if (path.Resolve(editor.Document) is not { } element)
                    continue;

                var root = path.Equals(XamlElementPath.Root);

                foreach (var write in Released(writes, root))
                    Write(editor, element, root, write);
            }
        }, select: null);
    }

    /// <summary>Записи вместе с размером, который отпускает выравнивание <c>Stretch</c>.</summary>
    private static IEnumerable<FormWrite> Released(IReadOnlyList<FormWrite> writes, bool root)
    {
        foreach (var write in writes)
            yield return write;

        // Корень тянет рамка дизайнера, а не родитель: его размер выравнивание не отпускает.
        if (root)
            yield break;

        foreach (var (alignment, size) in Stretches)
        {
            if (writes.Any(write => write.Member == alignment && string.Equals(write.Text, "Stretch", StringComparison.Ordinal)) &&
                writes.All(write => write.Member != size))
            {
                yield return new FormWrite(size, null);
            }
        }
    }

    private static void Write(XamlDocumentEditor editor, XamlElement element, bool root, FormWrite write)
    {
        if (root && write.Member is "Width" or "Height")
        {
            Size(editor, element, write.Member, write.Text ?? string.Empty);
            return;
        }

        var existing = Existing(element, write);

        // Снять — только написанное; а пространство объявляется тогда, когда имя под ним правда пишется.
        if (write.Text is null)
        {
            if (existing is not null)
                editor.RemoveAttribute(element, existing.Name);

            return;
        }

        if (existing is not null)
        {
            editor.SetAttribute(element, existing.Name, write.Text);
            return;
        }

        if (string.Equals(write.Member, NameDirective, StringComparison.Ordinal))
        {
            editor.SetAttribute(element, editor.QualifyAttribute(element, XamlNamespaces.Xaml, "Name", "x"), write.Text);
        }
        else if (write.Member.StartsWith(DesignPrefix, StringComparison.Ordinal))
        {
            var name = editor.QualifyAttribute(element, XamlNamespaces.Design, write.Member[DesignPrefix.Length..], "d");

            editor.EnsureIgnorable(XamlNamespaces.Design, "d");
            editor.SetAttribute(element, name, write.Text);
        }
        else if (write.Member.Contains('.', StringComparison.Ordinal) && write.OwnerNamespace is { } owner)
        {
            editor.SetAttribute(element, editor.Qualify(element, owner, write.Member, write.OwnerPrefix), write.Text);
        }
        else
        {
            editor.SetAttribute(element, XamlQualifiedName.Parse(write.Member), write.Text);
        }
    }

    /// <summary>Атрибут, которым документ уже пишет член, — как бы он ни назвал его пространство.</summary>
    /// <param name="element">Элемент.</param>
    /// <param name="write">Член — как его называет тот, кто правит; текст не смотрится.</param>
    /// <returns>Атрибут; null — член не написан.</returns>
    internal static XamlAttribute? Existing(XamlElement element, FormWrite write)
    {
        if (string.Equals(write.Member, NameDirective, StringComparison.Ordinal))
            return NameAttribute(element);

        if (write.Member.StartsWith(DesignPrefix, StringComparison.Ordinal))
        {
            var local = write.Member[DesignPrefix.Length..];

            return element.DesignTimeAttributes.FirstOrDefault(attribute => attribute.Name.LocalName == local);
        }

        return write.Member.Contains('.', StringComparison.Ordinal) ? Attached(element, write) : element.GetAttribute(write.Member);
    }

    /// <summary>Элемент свойства, которым документ пишет член: <c>&lt;Window.DataContext&gt;</c>.</summary>
    /// <remarks>
    /// Свой член пишут владельцем — именем элемента, типом или его базой, — а присоединённый — его владельцем: у окна
    /// <c>&lt;Design.DataContext&gt;</c> — не его <c>DataContext</c>, а данные для дизайна (запись 422).
    /// </remarks>
    /// <param name="element">Элемент.</param>
    /// <param name="write">Член — как его называет тот, кто правит; текст не смотрится.</param>
    /// <returns>Элемент свойства; null — член так не написан.</returns>
    internal static XamlElement? PropertyElement(XamlElement element, FormWrite write)
    {
        if (string.Equals(write.Member, NameDirective, StringComparison.Ordinal) ||
            write.Member.StartsWith(DesignPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return element.MemberElements.FirstOrDefault(child => write.Member.Contains('.', StringComparison.Ordinal)
            ? string.Equals(child.Name.LocalName, write.Member, StringComparison.Ordinal)
            : string.Equals(child.Name.MemberName, write.Member, StringComparison.Ordinal) &&
              child.Name.OwnerName is { } owner &&
              (string.Equals(owner, element.Name.LocalName, StringComparison.Ordinal) || write.Owners?.Contains(owner) == true));
    }

    /// <summary>
    /// Атрибут, которым документ называет элемент: <c>x:Name</c>, а без него — <c>Name</c>.
    /// </summary>
    /// <remarks>
    /// Avalonia читает <c>Name</c> так же, как <c>x:Name</c>, и Markup называет элемент тем же порядком
    /// (<c>XamlElement.Identity</c>). Строка имени видела только директиву, и запись имени у элемента с <c>Name</c>
    /// добавляла <c>x:Name</c> рядом — два присваивания одного члена, на которых загрузка падает (запись 390).
    /// </remarks>
    /// <param name="element">Элемент.</param>
    internal static XamlAttribute? NameAttribute(XamlElement element) =>
        element.GetDirectiveAttribute("Name") ?? element.GetAttribute("Name");

    /// <summary>Атрибут присоединённого члена — как бы документ ни назвал пространство его владельца.</summary>
    private static XamlAttribute? Attached(XamlElement element, FormWrite write) =>
        element.Attributes.FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, write.Member, StringComparison.Ordinal) &&
            (write.OwnerNamespace is null ||
             string.Equals(element.NamespaceContext.LookupNamespace(attribute.Name.Prefix), write.OwnerNamespace, StringComparison.Ordinal)));

    /// <summary>Имя шага: что записано и у кого.</summary>
    private string Label(IReadOnlyList<XamlElementPath> paths, IReadOnlyList<FormWrite> writes)
    {
        var many = paths.Count > 1;
        var target = many ? paths.Count.ToString(CultureInfo.CurrentCulture) : NameOf(paths[0]);

        if (writes.Count > 1)
            return string.Format(CultureInfo.CurrentCulture, _strings[many ? "form.edit.propertiesMany" : "form.edit.properties"], target);

        var key = (writes[0].Text is null, many) switch
        {
            (true, false) => "form.edit.clear",
            (true, true) => "form.edit.clearMany",
            (false, false) => "form.edit.set",
            (false, true) => "form.edit.setMany",
        };

        return string.Format(CultureInfo.CurrentCulture, _strings[key], writes[0].Member, target);
    }
}
