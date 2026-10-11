using System.Globalization;
using ArxisStudio.Inspector;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Inspection;

/// <summary>
/// Что инспектор видит у выбранных элементов формы: свойства, значения и написанное — на один текст и один показ.
/// </summary>
/// <remarks>
/// <para>
/// <b>Свойства — члены, которые есть у всех выбранных</b>: свои и присоединённые, которые читает родитель или уже
/// написал документ (<see cref="IXamlDesignView.GetMembers"/>), и те, что таблицы метаданных просят показывать
/// всегда (<c>ToolTip.Tip</c>, — <see cref="IXamlDesignView.GetMember"/>). Член, заданный элементом-свойством или
/// содержимым, — только для чтения: атрибут рядом дал бы загрузке один член дважды, и она отказала бы файлу.
/// Скрытым он становится по слову автора — <c>[Browsable(false)]</c>, <c>[EditorBrowsable(Never)]</c>,
/// <c>[PrivateApi]</c>, устаревший, — дополнительным — по <c>[EditorBrowsable(Advanced)]</c> и <c>[Unstable]</c>.
/// </para>
/// <para>
/// <b>Всё, что документ написал, — видно</b>: инспектор, прячущий то, что говорит файл, хуже показывающего лишнее.
/// Член для дизайна — в разделе дизайна, директива и атрибут, которого ничто не разрешило, — в «Прочем», только
/// чтением: <c>x:Class</c> и ключ — не значения, а неразрешённый член, переписанный наугад, стал бы ошибкой
/// загрузки. Имя (<c>x:Name</c>) у одного выбранного — первой строкой общего раздела.
/// </para>
/// <para>
/// <b>Значение — по написанному.</b> У нескольких выбранных «—» ставит разное написанное, а не разное действующее:
/// правят текст, и одинаково написанное — одно значение, как бы его ни переиграл стиль. Размер корня — из
/// <c>d:DesignWidth</c> и <c>d:DesignHeight</c>, если документ их назвал: в режиме дизайна побеждают они, и пишет
/// инспектор туда же. Без показа — тип не загружен, текст не читается — видно только написанное, и шапка
/// говорит почему.
/// </para>
/// <para>
/// Объектов поколения снимок не держит: сведения о членах и типах — имена, значения — текст и неизменные значения
/// общих сборок.
/// </para>
/// </remarks>
internal sealed class XamlInspectorSnapshot
{
    /// <summary>Раздел общего: имя элемента.</summary>
    private const string CommonCategory = "common";

    /// <summary>Раздел членов для дизайна.</summary>
    private const string DesignCategory = "design";

    /// <summary>Раздел всего остального, что документ написал.</summary>
    private const string OtherCategory = "other";

    /// <summary>Раздел присоединённых, которые читает родитель: «В Grid».</summary>
    private const string ParentCategory = "parent/";

    /// <summary>Флажок шапки: видим ли элемент.</summary>
    private const string VisibleMember = "IsVisible";

    /// <summary>Классы стиля — жетонами в шапке.</summary>
    private const string ClassesMember = "Classes";

    /// <summary>Элемент стиля: у него есть классы.</summary>
    private const string StyledElementType = "Avalonia.StyledElement";

    /// <summary>Окно верхнего уровня: на холсте оно стоит заменителем, и видимость его не правят.</summary>
    private const string TopLevelType = "Avalonia.Controls.TopLevel";

    private static readonly InspectorTypeInfo Text = InspectorTypeInfo.Of(typeof(string));

    private static readonly InspectorTypeInfo ClassesType = InspectorTypeInfo.Of(typeof(Avalonia.Controls.Classes));

    private readonly List<Picked> _picked;
    private readonly Dictionary<string, Spec> _specs;

    private XamlInspectorSnapshot(
        List<Picked> picked, Dictionary<string, Spec> specs, InspectorTypeInfo type, InspectorHeader header, bool live)
    {
        _picked = picked;
        _specs = specs;
        Type = type;
        Header = header;
        IsLive = live;
        Properties = specs.Values.Select(static spec => spec.Property).ToArray();
        Written = specs.Values
            .Where(spec => picked.Any(item => WrittenAt(item, spec) is not null))
            .Select(static spec => spec.Property.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Тип выбранного — общий у всех.</summary>
    public InspectorTypeInfo Type { get; }

    /// <summary>Шапка.</summary>
    public InspectorHeader Header { get; }

    /// <summary>Свойства.</summary>
    public IReadOnlyList<InspectorProperty> Properties { get; }

    /// <summary>Свойства, написанные хотя бы у одного выбранного.</summary>
    public IReadOnlyCollection<string> Written { get; }

    /// <summary>Остались ли выбранные элементы в тексте.</summary>
    public bool IsEmpty => _picked.Count == 0;

    /// <summary>Снят с живого показа: члены и значения — от объектов, а не только написанное.</summary>
    public bool IsLive { get; }

    /// <summary>
    /// Тот же снимок на время замены поколения: строки остаются с последними известными значениями, а шапка говорит,
    /// что типы перезагружаются.
    /// </summary>
    /// <param name="notice">Что сказать в шапке.</param>
    public XamlInspectorSnapshot Swapping(string notice) =>
        new(_picked, _specs, Type, Header with { Notice = notice }, live: false);

    /// <summary>Читает выбранное.</summary>
    /// <param name="view">Показ формы; null — форма не показана.</param>
    /// <param name="document">Документ формы.</param>
    /// <param name="paths">Пути выбранных, первый главный.</param>
    /// <param name="included">Члены, которые просят показывать всегда.</param>
    /// <param name="strings">Словарь модуля.</param>
    public static XamlInspectorSnapshot Read(
        IXamlDesignView? view,
        IXamlDocumentHandle document,
        IReadOnlyList<XamlElementPath> paths,
        IReadOnlyCollection<string> included,
        IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(included);
        ArgumentNullException.ThrowIfNull(strings);

        var syntax = document.Syntax;
        var live = view is not null && document.State == XamlDocumentState.Live;
        var picked = new List<Picked>(paths.Count);

        foreach (var path in paths)
        {
            if (path.Resolve(syntax) is not { } element)
                continue;

            var info = live ? view!.DescribeElement(path) : null;
            var members = new Dictionary<string, XamlMemberInfo>(StringComparer.Ordinal);

            if (info is not null)
            {
                foreach (var member in view!.GetMembers(path))
                    members.TryAdd(member.Name, member);

                foreach (var name in included)
                {
                    if (!members.ContainsKey(name) && view.GetMember(path, name) is { } asked)
                        members[name] = asked;
                }
            }

            picked.Add(new Picked(path, element, info, members));
        }

        var specs = new Dictionary<string, Spec>(StringComparer.Ordinal);
        var type = TypeOf(picked);
        var shown = live;

        // Живой — тот, чьи члены пришли от объектов: показ может быть, а объектов в нём уже нет — их отдали на замену.
        live = live && picked.Count > 0 && picked.All(static item => item.Info is not null);

        if (picked.Count == 0)
            return new XamlInspectorSnapshot(picked, specs, type, HeaderOf(picked, document.State, shown, strings, specs), live);

        if (live)
            AddMembers(picked, specs);

        AddClasses(picked, specs, live);

        if (picked.Count == 1)
            AddWritten(picked[0], specs, live);
        else if (!live)
            AddCommonWritten(picked, specs);

        return new XamlInspectorSnapshot(picked, specs, type, HeaderOf(picked, document.State, live, strings, specs), live);
    }

    /// <summary>Значение свойства; у нескольких выбранных — первого, с «—», если написано разное.</summary>
    /// <param name="id">Свойство.</param>
    public InspectorValue ValueOf(string id)
    {
        if (!_specs.TryGetValue(id, out var spec) || _picked.Count == 0)
            return InspectorValue.Empty;

        var first = ValueAt(_picked[0], spec);
        var mixed = _picked.Skip(1).Any(item => !string.Equals(WrittenAt(item, spec), first.Written, StringComparison.Ordinal));

        return mixed ? first with { IsMixed = true } : first;
    }

    /// <summary>Как написать свойство в документ.</summary>
    /// <param name="id">Свойство.</param>
    /// <param name="text">Значение; null — снять.</param>
    public FormWrite Spell(string id, string? text) =>
        _specs.TryGetValue(id, out var spec) && spec.Member is { IsAttached: true } member
            ? new FormWrite(id, text, member.OwnerXmlNamespace, member.OwnerPrefix)
            : new FormWrite(id, text);

    /// <summary>Есть ли такое свойство и пишется ли оно.</summary>
    /// <param name="id">Свойство.</param>
    public bool Writes(string id) => _specs.TryGetValue(id, out var spec) && !spec.Property.IsReadOnly;

    /// <summary>
    /// Свойство известно и только читается: задано элементом, содержимым — или его не задаст компилятор XAML
    /// (<c>ActualThemeVariant</c>), и записанное в форму уронило бы сборку проекта.
    /// </summary>
    /// <param name="id">Свойство.</param>
    public bool Refuses(string id) => _specs.TryGetValue(id, out var spec) && spec.Property.IsReadOnly;

    /// <summary>Сколько выбранных осталось в тексте.</summary>
    public int Count => _picked.Count;

    /// <summary>Что написано у выбранного по номеру.</summary>
    /// <param name="id">Свойство.</param>
    /// <param name="index">Номер выбранного.</param>
    /// <returns>Написанное; null — не написано или такого свойства нет.</returns>
    public string? WrittenAt(string id, int index) =>
        _specs.TryGetValue(id, out var spec) && index >= 0 && index < _picked.Count ? WrittenAt(_picked[index], spec) : null;

    /// <summary>Как назвать выбранного по номеру: тип и имя — <c>Button «Go»</c>.</summary>
    /// <param name="index">Номер выбранного.</param>
    public string NameAt(int index) => Titled(_picked[index].Element);

    /// <summary>
    /// Где в тексте выбранного написано свойство: размер корня — в размере дизайна, если документ его назвал.
    /// </summary>
    /// <param name="id">Свойство.</param>
    /// <param name="index">Номер выбранного.</param>
    public FormWrite Locate(string id, int index)
    {
        var item = _picked[index];

        return item.Path.Equals(XamlElementPath.Root) && id is "Width" or "Height" &&
               item.Element.GetDesignTimeAttribute("Design" + id) is not null
            ? new FormWrite(FormEdits.DesignPrefix + "Design" + id, null)
            : Spell(id, null);
    }

    /// <summary>
    /// Можно ли перенести свойство в члены для дизайна (<c>d:</c>): свой член объекта, который пишется, — не
    /// присоединённый и не размер корня, он и так размер дизайна.
    /// </summary>
    /// <param name="id">Свойство.</param>
    public bool Designs(string id) =>
        _specs.TryGetValue(id, out var spec) && spec.Kind == Kind.Member && !spec.Property.IsReadOnly &&
        spec.Member is { IsAttached: false } &&
        !(id is "Width" or "Height" && _picked.Any(static item => item.Path.Equals(XamlElementPath.Root)));

    /// <summary>
    /// Можно ли показать значение свойства на объектах, не трогая документ: член объекта, который пишется, — но не
    /// размер корня, его дизайнер берёт из размера дизайна, а не с объекта.
    /// </summary>
    /// <param name="id">Свойство.</param>
    public bool Previews(string id) =>
        IsLive && _specs.TryGetValue(id, out var spec) && spec.Kind == Kind.Member && !spec.Property.IsReadOnly &&
        !(id is "Width" or "Height" && _picked.Any(static item => item.Path.Equals(XamlElementPath.Root)));

    /// <summary>Элементы выбранных — тем, кто проверяет имя.</summary>
    public IEnumerable<XamlElement> Elements => _picked.Select(static item => item.Element);

    /// <summary>Пути выбранных, которые остались в тексте.</summary>
    public IEnumerable<XamlElementPath> Paths => _picked.Select(static item => item.Path);

    /// <summary>Члены, которые есть у всех выбранных, — строками.</summary>
    private static void AddMembers(List<Picked> picked, Dictionary<string, Spec> specs)
    {
        var first = picked[0];

        foreach (var (name, member) in first.Members)
        {
            // Имя элемента — его x:Name, а свойство CLR с тем же именем — то же самое: строка одна.
            if (name == "Name" || !picked.Skip(1).All(item => item.Members.ContainsKey(name)))
                continue;

            var setAsElement = picked.Any(item => SetAsElement(item.Element, name, item.Info?.ContentMember));
            var attributes = member.Attributes;

            specs[name] = new Spec(Kind.Member, member, new InspectorProperty(name, TypeOf(member.ValueType))
            {
                DisplayName = attributes.DisplayName,
                Category = member.ParentReads
                    ? ParentCategory + XamlTypeName(member.OwnerTypeFullName)
                    : attributes.Category,
                Description = attributes.Description,
                Kind = member.Kind switch
                {
                    XamlPropertyKind.Styled => InspectorPropertyKind.Styled,
                    XamlPropertyKind.Direct => InspectorPropertyKind.Direct,
                    XamlPropertyKind.Attached => InspectorPropertyKind.Attached,
                    _ => InspectorPropertyKind.Clr,
                },
                OwnerTypeName = member.OwnerTypeFullName,
                IsReadOnly = !member.CanWrite || attributes.IsReadOnly || setAsElement,
                IsHidden = !attributes.Browsable || attributes.EditorBrowsable == XamlEditorBrowsable.Never ||
                           attributes.IsPrivateApi || attributes.ObsoleteMessage is not null,
                IsAdvanced = attributes.EditorBrowsable == XamlEditorBrowsable.Advanced || attributes.IsUnstable,
                AcceptsText = member.AcceptsText,
                DefaultText = attributes.DefaultValueText,
                Constraints = attributes.RangeMinimum is null && attributes.RangeMaximum is null
                    ? InspectorConstraints.None
                    : new InspectorConstraints { Minimum = attributes.RangeMinimum, Maximum = attributes.RangeMaximum },
            });
        }
    }

    /// <summary>
    /// Классы стиля — жетонами в шапке. Среди членов их нет: это список, а не значение, — но разметка пишет их одним
    /// атрибутом, <c>Classes="primary large"</c>, и так их и правят. Есть они у всякого элемента стиля, а без показа,
    /// когда типов не видно, — там, где написаны.
    /// </summary>
    private static void AddClasses(List<Picked> picked, Dictionary<string, Spec> specs, bool live)
    {
        var styled = live
            ? picked.All(static item => item.Info is { } info &&
                                        (info.TypeFullName == StyledElementType || info.BaseTypes.Contains(StyledElementType, StringComparer.Ordinal)))
            : picked.All(static item => item.Element.GetAttribute(ClassesMember) is not null);

        if (styled)
            specs[ClassesMember] = new Spec(Kind.Other, null, new InspectorProperty(ClassesMember, ClassesType) { Category = CommonCategory });
    }

    /// <summary>Написанное у одного выбранного, чего нет среди членов: имя, члены для дизайна, директивы и прочее.</summary>
    private static void AddWritten(Picked item, Dictionary<string, Spec> specs, bool live)
    {
        var element = item.Element;
        var root = item.Path.Equals(XamlElementPath.Root);

        specs[FormEdits.NameDirective] = new Spec(Kind.Name, null, new InspectorProperty(FormEdits.NameDirective, Text)
        {
            Category = CommonCategory,
            Order = int.MinValue,
            Kind = InspectorPropertyKind.Directive,
        });

        foreach (var attribute in element.Attributes)
        {
            if (attribute is XamlNamespaceDeclaration || attribute.IsMarkupCompatibility)
                continue;

            if (attribute.IsDesignTime)
            {
                var local = attribute.Name.LocalName;

                // Размер дизайна корня — его ширина и высота: дизайнер берёт их оттуда, и строки у них свои.
                if (root && local is "DesignWidth" or "DesignHeight")
                    continue;

                var id = FormEdits.DesignPrefix + local;

                specs.TryAdd(id, new Spec(Kind.Design, null, new InspectorProperty(id, Text) { Category = DesignCategory }));
                continue;
            }

            if (attribute.IsDirective)
            {
                if (attribute.Name.LocalName == "Name")
                    continue;

                var directive = "x:" + attribute.Name.LocalName;

                specs.TryAdd(directive, new Spec(Kind.Other, null, new InspectorProperty(directive, Text)
                {
                    Category = OtherCategory,
                    Kind = InspectorPropertyKind.Directive,
                    IsReadOnly = true,
                }));
                continue;
            }

            var name = attribute.Name.Prefix is null ? attribute.Name.LocalName : attribute.Name.ToString();

            if (specs.ContainsKey(name) || specs.ContainsKey(attribute.Name.LocalName))
                continue;

            // Без показа написанное правят текстом — загрузка проверит его, когда форма встанет; при показе то, что
            // ничто не разрешило, только читают.
            specs[name] = new Spec(Kind.Other, null, new InspectorProperty(name, Text)
            {
                Category = OtherCategory,
                IsReadOnly = live,
            });
        }
    }

    /// <summary>Без показа у нескольких выбранных — то, что написано у всех.</summary>
    private static void AddCommonWritten(List<Picked> picked, Dictionary<string, Spec> specs)
    {
        var names = picked
            .Select(static item => item.Element.Attributes
                .Where(static attribute => attribute is not XamlNamespaceDeclaration &&
                                           !attribute.IsMarkupCompatibility && !attribute.IsDesignTime && !attribute.IsDirective &&
                                           attribute.Name.Prefix is null)
                .Select(static attribute => attribute.Name.LocalName)
                .ToHashSet(StringComparer.Ordinal))
            .Aggregate(static (all, next) =>
            {
                all.IntersectWith(next);
                return all;
            });

        foreach (var name in names.Order(StringComparer.Ordinal))
            specs[name] = new Spec(Kind.Other, null, new InspectorProperty(name, Text) { Category = OtherCategory });
    }

    /// <summary>Значение у одного выбранного.</summary>
    private static InspectorValue ValueAt(Picked item, Spec spec)
    {
        var written = WrittenAt(item, spec);

        if (spec.Kind != Kind.Member || !item.Members.TryGetValue(spec.Property.Id, out var member))
        {
            var source = written is null ? InspectorValueSource.Default
                : spec.Kind == Kind.Design ? InspectorValueSource.Design
                : InspectorValueSource.Local;

            return new InspectorValue(written ?? string.Empty) { Written = written, Source = source };
        }

        var value = member.Value;
        var origin = value.Origin switch
        {
            XamlValueOrigin.Document => InspectorValueSource.Local,
            XamlValueOrigin.Design => InspectorValueSource.Design,
            XamlValueOrigin.Expression => InspectorValueSource.Expression,
            XamlValueOrigin.Style => InspectorValueSource.Style,
            XamlValueOrigin.Template => InspectorValueSource.Template,
            XamlValueOrigin.Inherited => InspectorValueSource.Inherited,
            XamlValueOrigin.Animation => InspectorValueSource.Animation,
            XamlValueOrigin.Code => InspectorValueSource.Code,
            _ => InspectorValueSource.Default,
        };

        // Написанное — значением, как его написал документ: «Auto», а не «NaN»; выражение показывают выражением.
        if (written is not null && origin is not (InspectorValueSource.Expression or InspectorValueSource.Design))
            origin = InspectorValueSource.Local;

        return new InspectorValue(origin is InspectorValueSource.Local or InspectorValueSource.Design ? written ?? value.Text : value.Text)
        {
            Written = written,
            Source = origin,
            SourceDetail = value.OriginDetail,
            Shared = value.Shared,
        };
    }

    /// <summary>Что написано у элемента для свойства; null — не написано.</summary>
    private static string? WrittenAt(Picked item, Spec spec)
    {
        var element = item.Element;
        var id = spec.Property.Id;

        switch (spec.Kind)
        {
            case Kind.Name:
                return element.GetDirective("Name");
            case Kind.Design:
                return element.GetDesignTimeAttribute(id[FormEdits.DesignPrefix.Length..]);
            case Kind.Other:
                return id.StartsWith("x:", StringComparison.Ordinal)
                    ? element.GetDirective(id[2..])
                    : element.Attributes.FirstOrDefault(attribute =>
                        attribute is not XamlNamespaceDeclaration &&
                        (attribute.Name.Prefix is null ? attribute.Name.LocalName : attribute.Name.ToString()) == id)?.GetValueText();
        }

        // Размер корня — из размера дизайна, если документ его назвал: дизайнер показывает его и пишет туда же.
        if (item.Path.Equals(XamlElementPath.Root) && id is "Width" or "Height" &&
            element.GetDesignTimeAttribute("Design" + id) is { } design)
        {
            return design;
        }

        if (spec.Member is { IsAttached: true } member)
        {
            return element.Attributes.FirstOrDefault(attribute =>
                attribute.Name.LocalName == id &&
                (member.OwnerXmlNamespace is null ||
                 string.Equals(element.NamespaceContext.LookupNamespace(attribute.Name.Prefix), member.OwnerXmlNamespace, StringComparison.Ordinal)))
                ?.GetValueText();
        }

        return element.GetAttribute(id)?.GetValueText();
    }

    /// <summary>
    /// Задан ли член длинной записью — элементом-свойством или содержимым: атрибут рядом дал бы загрузке один член
    /// дважды.
    /// </summary>
    private static bool SetAsElement(XamlElement element, string member, string? contentMember) =>
        element.MemberElements.Any(child => child.Name.MemberName == member) ||
        (member == contentMember && element.ContentElements.Any());

    /// <summary>Тип выбранного: общий у всех — сам тип, а у разных — ближайшая общая база.</summary>
    private static InspectorTypeInfo TypeOf(List<Picked> picked)
    {
        if (picked.Count == 0)
            return new InspectorTypeInfo(string.Empty);

        if (picked.Any(static item => item.Info is null))
        {
            var names = picked.Select(static item => item.Element.Name.ToString()).Distinct(StringComparer.Ordinal).ToList();

            return new InspectorTypeInfo(names.Count == 1 ? names[0] : string.Empty);
        }

        var chains = picked
            .Select(static item => (IReadOnlyList<string>)[item.Info!.TypeFullName, .. item.Info.BaseTypes])
            .ToList();
        var common = chains[0].FirstOrDefault(name => chains.Skip(1).All(chain => chain.Contains(name, StringComparer.Ordinal)))
                     ?? string.Empty;
        var at = chains[0].ToList().IndexOf(common);

        return new InspectorTypeInfo(common) { BaseTypes = at < 0 ? [] : chains[0].Skip(at + 1).ToArray() };
    }

    private static InspectorTypeInfo TypeOf(XamlTypeInfo type) => new(type.FullName)
    {
        BaseTypes = type.BaseTypes,
        Interfaces = type.Interfaces,
        EnumNames = type.EnumNames,
        IsFlags = type.IsFlags,
        IsNullable = type.IsNullable,
    };

    /// <summary>
    /// Шапка: тип и имя или число выбранных, пространство и сборка; флажок видимости, имя и классы — свойства, которые
    /// шапка правит сама, если они есть.
    /// </summary>
    private static InspectorHeader HeaderOf(
        List<Picked> picked, XamlDocumentState state, bool live, IStudioStrings strings, Dictionary<string, Spec> specs)
    {
        var notice = live ? null : strings[state == XamlDocumentState.Behind ? "inspector.notice.behind" : "inspector.notice.written"];

        // Окно на холсте — не окно на экране: его видимость в дизайнере ничего не значит, и флажка у него нет.
        var visible = specs.TryGetValue(VisibleMember, out var spec) && spec.Kind == Kind.Member &&
                      !picked.Any(static item => item.Info is { } info &&
                                                 (info.TypeFullName == TopLevelType || info.BaseTypes.Contains(TopLevelType, StringComparer.Ordinal)))
            ? VisibleMember
            : null;
        var name = specs.ContainsKey(FormEdits.NameDirective) ? FormEdits.NameDirective : null;
        var classes = specs.ContainsKey(ClassesMember) ? ClassesMember : null;

        if (picked.Count != 1)
        {
            return new InspectorHeader(string.Format(CultureInfo.CurrentCulture, strings["inspector.many"], picked.Count))
            {
                Notice = notice,
                EnabledProperty = visible,
                NameProperty = name,
                TagsProperty = classes,
            };
        }

        var item = picked[0];
        var title = Titled(item.Element);

        // Подзаголовок — тип, и его ставит панель; пространство и сборка — подсказкой, как у Unity (запись 377).
        var detail = item.Info is { } info
            ? $"{Namespace(info.TypeFullName)} · {info.AssemblyName}"
            : null;

        return new InspectorHeader(title)
        {
            Detail = detail,
            Notice = notice,
            EnabledProperty = visible,
            NameProperty = name,
            TagsProperty = classes,
        };
    }

    /// <summary>Тип и имя элемента: <c>Button «Go»</c>, без имени — тип.</summary>
    private static string Titled(XamlElement element) =>
        element.Identity is { Length: > 0 } identity ? $"{element.Name.LocalName} «{identity}»" : element.Name.LocalName;

    private static string Namespace(string fullName)
    {
        var dot = fullName.LastIndexOf('.');

        return dot < 0 ? string.Empty : fullName[..dot];
    }

    /// <summary>Короткое имя типа: <c>Grid</c>.</summary>
    private static string XamlTypeName(string fullName)
    {
        var generic = fullName.IndexOf('`', StringComparison.Ordinal);
        var plain = generic < 0 ? fullName : fullName[..generic];
        var dot = plain.LastIndexOf('.');

        return dot < 0 ? plain : plain[(dot + 1)..];
    }

    /// <summary>Чем свойство написано.</summary>
    private enum Kind
    {
        /// <summary>Член объекта.</summary>
        Member,

        /// <summary>Имя элемента — <c>x:Name</c>.</summary>
        Name,

        /// <summary>Член для дизайна — <c>d:</c>.</summary>
        Design,

        /// <summary>Прочее написанное: директива, неразрешённый атрибут, написанное без показа.</summary>
        Other,
    }

    /// <summary>Свойство и то, чем оно написано.</summary>
    private sealed record Spec(Kind Kind, XamlMemberInfo? Member, InspectorProperty Property);

    /// <summary>Выбранный элемент: путь, элемент текста, что он построил и его члены.</summary>
    private sealed record Picked(
        XamlElementPath Path, XamlElement Element, XamlElementInfo? Info, Dictionary<string, XamlMemberInfo> Members);
}
