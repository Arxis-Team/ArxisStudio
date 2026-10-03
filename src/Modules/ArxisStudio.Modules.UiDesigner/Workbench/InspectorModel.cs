using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Workbench;

/// <summary>
/// Что показывает инспектор: шапку выбранного и строки его членов по разделам.
/// </summary>
/// <remarks>
/// <para>
/// <b>Короткий список, а не все члены.</b> У кнопки их больше сотни, и инспектор, показывающий все, не
/// говорит, какие важны. Поэтому разделы — каталог: имена, о которых спрашивают первыми, и каждое
/// спрашивается у объекта — <c>CornerRadius</c> встанет у <c>Border</c> и не встанет у <c>TextBlock</c>.
/// Присоединённые члены, которые читает родитель, — свой раздел, «В Canvas», «В Grid». Всё прочее, что
/// документ написал, — «Прочее»: инспектор, прячущий то, что говорит файл, хуже, чем показывающий лишнее.
/// Поиск ищет по всем членам.
/// </para>
/// <para>
/// <b>Строки пересобираются</b> на смену выбора, текста и показа: значения объекта — чтение сейчас, а
/// правка, отмена и замена поколения их меняют.
/// </para>
/// </remarks>
internal sealed class InspectorModel : INotifyPropertyChanged
{
    /// <summary>
    /// Разделы каталога и их члены, по порядку. Член, которого у объекта нет, не показывается.
    /// </summary>
    /// <remarks>
    /// <c>PlaceholderText</c>, а не <c>Watermark</c>: оба есть у поля ввода, но второй — устаревшее имя,
    /// и инспектор, предлагающий его, писал бы в файл человека устаревший API.
    /// </remarks>
    private static readonly (string Group, string[] Members)[] Catalogue =
    [
        ("window", ["Title", "SizeToContent", "CanResize", "Topmost", "WindowStartupLocation", "ShowInTaskbar"]),
        ("layout",
        [
            "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin", "Padding",
            "HorizontalAlignment", "VerticalAlignment", "HorizontalContentAlignment", "VerticalContentAlignment",
            "Orientation", "Spacing", "ZIndex", "RowDefinitions", "ColumnDefinitions",
        ]),
        ("content", ["Content", "Header", "Text", "PlaceholderText", "Source", "IsChecked", "Minimum", "Maximum", "Value", "SelectedIndex"]),
        ("text", ["FontSize", "FontWeight", "FontStyle", "FontFamily", "TextWrapping", "TextAlignment", "TextTrimming"]),
        ("appearance", ["Background", "Foreground", "BorderBrush", "BorderThickness", "CornerRadius", "Opacity", "Stretch"]),
        ("interaction",
        [
            "IsEnabled", "IsVisible", "IsReadOnly", "AcceptsReturn", "MaxLength", "IsDefault", "IsCancel",
            "Command", "CommandParameter", "HotKey", "ToolTip.Tip", "Cursor", "Focusable", "IsTabStop",
        ]),
    ];

    private readonly IStudioStrings _strings;
    private string _title = string.Empty;
    private string _detail = string.Empty;
    private string _name = string.Empty;
    private bool _canName;
    private bool _hasSelection;
    private IReadOnlyList<InspectorItem> _items = [];

    /// <summary>Модель инспектора.</summary>
    /// <param name="strings">Словарь модуля.</param>
    public InspectorModel(IStudioStrings strings) => _strings = strings;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Тип выбранного или сколько выбрано.</summary>
    public string Title { get => _title; private set => Set(ref _title, value); }

    /// <summary>Пространство CLR типа — у одного выбранного.</summary>
    public string Detail { get => _detail; private set => Set(ref _detail, value); }

    /// <summary><c>x:Name</c> выбранного.</summary>
    public string Name { get => _name; private set => Set(ref _name, value); }

    /// <summary>Можно ли назвать: выбран один элемент.</summary>
    public bool CanName { get => _canName; private set => Set(ref _canName, value); }

    /// <summary>Строки: заголовки разделов и члены.</summary>
    public IReadOnlyList<InspectorItem> Items { get => _items; private set => Set(ref _items, value); }

    /// <summary>Выбрано ли что-нибудь, что можно показать.</summary>
    public bool HasSelection { get => _hasSelection; private set => Set(ref _hasSelection, value); }

    /// <summary>Строит шапку и строки для выбора формы.</summary>
    /// <param name="form">Форма впереди; null — показывать нечего.</param>
    /// <param name="filter">Что набрано в поиске.</param>
    public void Show(LiveFormDocument? form, string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (Selected(form) is not { } selected)
        {
            Clear();
            return;
        }

        var (shown, _, edits, elements) = selected;
        var primary = elements[0];
        var info = shown.DescribeElement(primary.Path);

        HasSelection = true;
        Title = elements.Count == 1
            ? primary.Element.Name.LocalName
            : string.Format(CultureInfo.CurrentCulture, _strings["inspector.many"], elements.Count);
        Detail = elements.Count == 1 ? info?.TypeNamespace ?? string.Empty : string.Empty;
        CanName = elements.Count == 1;
        Name = primary.Element.GetDirective("Name") ?? string.Empty;

        Items = new Builder(this, shown, edits, elements, filter.Trim()).Build();
    }

    /// <summary>Почему имя не годится; null — годится.</summary>
    /// <param name="form">Форма.</param>
    /// <param name="typed">Набранное имя.</param>
    /// <remarks>
    /// <c>x:Name</c> становится полем класса формы — значит, это идентификатор; и область имён держит
    /// каждое имя один раз — второй элемент с тем же именем форма не загрузит.
    /// </remarks>
    public string? NameError(LiveFormDocument form, string typed)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(typed);

        if (typed.Length == 0)
            return null;

        if (!(char.IsLetter(typed[0]) || typed[0] == '_') || !typed.All(static c => char.IsLetterOrDigit(c) || c == '_'))
            return _strings["inspector.name.invalid"];

        if (Selected(form) is not { } selected || selected.Syntax.Root is not { } root)
            return null;

        var self = selected.Elements[0].Element;

        return root.DescendantElements().Prepend(root).Any(other => !ReferenceEquals(other, self) && other.GetDirective("Name") == typed)
            ? string.Format(CultureInfo.CurrentCulture, _strings["inspector.name.taken"], typed)
            : null;
    }

    private void Clear()
    {
        HasSelection = false;
        Title = string.Empty;
        Detail = string.Empty;
        Name = string.Empty;
        CanName = false;
        Items = [];
    }

    private static Selection? Selected(LiveFormDocument? form)
    {
        if (form is not { Shown: { } shown, Document: { } document, Edits: { } edits })
            return null;

        var syntax = document.Syntax;
        var elements = form.Selection
            .Select(path => (Path: path, Element: path.Resolve(syntax)))
            .Where(pair => pair.Element is not null)
            .Select(pair => new Picked(pair.Path, pair.Element!))
            .ToList();

        return elements.Count == 0 ? null : new Selection(shown, syntax, edits, elements);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private string Origin(XamlValueOrigin origin) => origin switch
    {
        XamlValueOrigin.Style => _strings["inspector.origin.style"],
        XamlValueOrigin.Inherited => _strings["inspector.origin.inherited"],
        XamlValueOrigin.Template => _strings["inspector.origin.template"],
        XamlValueOrigin.Binding => _strings["inspector.origin.binding"],
        XamlValueOrigin.Animation => _strings["inspector.origin.animation"],
        XamlValueOrigin.Document => _strings["inspector.origin.document"],
        _ => _strings["inspector.origin.default"],
    };

    /// <summary>Выбранный элемент: путь и элемент текста.</summary>
    private sealed record Picked(XamlElementPath Path, XamlElement Element);

    /// <summary>Что показывают: показ, текст, правки и выбранные элементы.</summary>
    private sealed record Selection(IXamlDesignView Shown, XamlDocument Syntax, FormEdits Edits, List<Picked> Elements);

    /// <summary>Сборка строк для одного выбора.</summary>
    private sealed class Builder(
        InspectorModel owner,
        IXamlDesignView shown,
        FormEdits edits,
        List<Picked> elements,
        string filter)
    {
        private readonly List<Dictionary<string, XamlMemberRow>> _members =
        [
            .. elements.Select(element => shown.GetMembers(element.Path).ToDictionary(row => row.Name, StringComparer.Ordinal)),
        ];

        // Имя у элемента одно — x:Name в шапке; одноимённое свойство CLR — то же самое.
        private readonly HashSet<string> _offered = new(StringComparer.Ordinal) { "Name" };
        private readonly List<InspectorItem> _items = [];

        public List<InspectorItem> Build()
        {
            foreach (var (group, members) in Catalogue)
                Section(owner._strings[$"inspector.group.{group}"], members.Where(Offerable));

            // Присоединённые — каждый владелец своим разделом: что читает Canvas, что читает Grid.
            foreach (var attached in Common()
                .Where(name => !_offered.Contains(name) && _members[0][name].IsAttached)
                .GroupBy(name => _members[0][name].OwnerTypeName)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                Section(
                    string.Format(CultureInfo.CurrentCulture, owner._strings["inspector.group.parent"], attached.Key),
                    attached.Where(Offerable));
            }

            if (elements.Count == 1)
                Others();

            if (filter.Length > 0)
                Section(owner._strings["inspector.group.more"], Common().Where(name => !_offered.Contains(name)).Order(StringComparer.Ordinal));

            return _items;
        }

        /// <summary>Раздел: заголовок и строки членов, которые есть у всех выбранных; пустой не ставится.</summary>
        private void Section(string title, IEnumerable<string> members)
        {
            var rows = members
                .Where(member => !_offered.Contains(member))
                .Select(Row)
                .OfType<InspectorRow>()
                .Where(row => filter.Length == 0 || row.Member.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (rows.Count == 0)
                return;

            foreach (var row in rows)
                _offered.Add(row.Member);

            _items.Add(new InspectorGroup(title));
            _items.AddRange(rows);
        }

        /// <summary>Всё, что документ написал на элементе и что не встало выше, — «Прочее».</summary>
        private void Others()
        {
            var element = elements[0].Element;
            var rows = new List<InspectorRow>();

            foreach (var attribute in element.Attributes)
            {
                var name = attribute.Name.ToString();

                if (_offered.Contains(name) || IsDeclaration(attribute) || attribute.IsDesignTime
                    || attribute.IsMarkupCompatibility || name == FormEdits.NameDirective)
                    continue;

                if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Директиву и член, который ничто не разрешило, показывают как написано — не правят: x:Class и
                // ключ — не значения, а неразрешённый член, переписанный наугад, стал бы ошибкой загрузки.
                var row = attribute.IsDirective ? null : Row(name);

                rows.Add(row ?? new InspectorRow(
                    name, XamlValueEditor.None, [], attribute.GetValueText(), isMixed: false, hint: null,
                    static _ => null, static _ => Task.CompletedTask));
                _offered.Add(name);
            }

            if (rows.Count == 0)
                return;

            _items.Add(new InspectorGroup(owner._strings["inspector.group.other"]));
            _items.AddRange(rows);
        }

        /// <summary>Строка члена, который есть у всех выбранных; null — нет хоть у одного.</summary>
        private InspectorRow? Row(string member)
        {
            var infos = new List<XamlMemberRow>(elements.Count);

            for (var index = 0; index < elements.Count; index++)
            {
                if (Info(index, member) is not { } info)
                    return null;

                infos.Add(info);
            }

            var written = elements.Select(element => Written(element, member)).ToList();
            var mixed = written.Distinct(StringComparer.Ordinal).Count() > 1;
            var first = infos[0];
            // Умолчание — не новость: подсказка говорит, откуда значение, только когда оно откуда-то.
            var hint = first.ValueText is not { } value ? null
                : first.Origin == XamlValueOrigin.Default ? value
                : $"{value} · {owner.Origin(first.Origin)}";
            var paths = elements.Select(element => element.Path).ToList();

            return new InspectorRow(
                member,
                first.Editor,
                first.Choices,
                mixed ? string.Empty : written[0],
                mixed,
                hint,
                text => paths.Select(path => shown.CheckValue(path, member, text).Error).FirstOrDefault(error => error is not null),
                text => edits.SetAsync(paths, member, text));
        }

        /// <summary>Член элемента: из его списка, а присоединённый стороннего владельца — по имени.</summary>
        private XamlMemberRow? Info(int index, string member) =>
            _members[index].TryGetValue(member, out var known)
                ? known
                : member.Contains('.', StringComparison.Ordinal) ? shown.GetMember(elements[index].Path, member) : null;

        /// <summary>Есть ли член у всех выбранных и не написан ли он элементом-свойством.</summary>
        private bool Offerable(string member) =>
            !_offered.Contains(member)
            && !elements.Any(element => SetAsElement(element.Element, member));

        /// <summary>Члены, которые есть у всех выбранных.</summary>
        private IEnumerable<string> Common() =>
            _members[0].Keys.Where(name => _members.Skip(1).All(members => members.ContainsKey(name)));

        /// <summary>
        /// Что написано у элемента: атрибут, а у корня, назвавшего размер дизайна, — он: его дизайнер и
        /// показывает, и туда же пишет правка.
        /// </summary>
        private static string Written(Picked picked, string member)
        {
            if (picked.Element.GetAttribute(member) is { } attribute)
                return attribute.GetValueText();

            if (picked.Path.Equals(XamlElementPath.Root) && member is "Width" or "Height")
            {
                return picked.Element.DesignTimeAttributes
                    .FirstOrDefault(design => design.Name.LocalName == "Design" + member)?.GetValueText() ?? string.Empty;
            }

            return string.Empty;
        }

        /// <summary>
        /// Задан ли член длинной записью: элементом-свойством или содержимым. Атрибут рядом дал бы загрузке
        /// один член дважды, и она отказала бы всему файлу.
        /// </summary>
        private static bool SetAsElement(XamlElement element, string member)
        {
            if (element.MemberElements.Any(child => child.Name.LocalName.EndsWith("." + member, StringComparison.Ordinal)))
                return true;

            return member is "Content" or "Header" && element.ContentElements.Any();
        }

        private static bool IsDeclaration(XamlAttribute attribute) =>
            attribute.Name.Prefix == "xmlns" || attribute.Name.LocalName == "xmlns";
    }
}
