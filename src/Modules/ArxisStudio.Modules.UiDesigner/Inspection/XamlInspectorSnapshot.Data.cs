using System.Globalization;
using ArxisStudio.Inspector;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Inspection;

// Раздел «Данные»: что читают привязки выбранных и чем их задать.
// Часть XamlInspectorSnapshot; общее описание типа — в XamlInspectorSnapshot.cs.
internal sealed partial class XamlInspectorSnapshot
{
    /// <summary>Раздел данных — первый после шапки.</summary>
    public const string DataCategory = "data";

    /// <summary>Объект: данные бывают чем угодно.</summary>
    private static readonly InspectorTypeInfo ObjectType = InspectorTypeInfo.Of(typeof(object));

    /// <summary>
    /// Компиляция привязок — тремя сегментами: «По умолчанию», «Да», «Нет». Имя своё, а не <c>System.Boolean</c>: флажок
    /// двух состояний не скажет «не задано».
    /// </summary>
    private static readonly InspectorTypeInfo CompileType = new("ArxisStudio.Xaml.CompileBindings") { EnumNames = ["True", "False"] };

    /// <summary>Строки данных — их значения приходят ответом службы.</summary>
    public IReadOnlyCollection<string> DataIds { get; }

    /// <summary>Класс формы, если выбран один корень с <c>x:Class</c>: сама форма бывает своими данными.</summary>
    public string? FormClass =>
        _picked is [{ } only] && only.Path.Equals(XamlElementPath.Root) ? only.Element.GetDirective("Class")?.Trim() : null;

    /// <summary>Кладёт ответы службы о данных выбранных — по номеру выбранного.</summary>
    /// <param name="scopes">Ответы; null у выбранного — службе нечего сказать.</param>
    public void Describe(IReadOnlyList<XamlDataScope?> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        for (var at = 0; at < _data.Length && at < scopes.Count; at++)
            _data[at] = scopes[at];
    }

    /// <summary>Ответы службы о данных выбранных — по номеру выбранного; null у выбранного — ещё не ответила.</summary>
    public IReadOnlyList<XamlDataScope?> Data => _data;

    /// <summary>Что читают привязки выбранного по номеру; null — служба ещё не ответила.</summary>
    /// <param name="index">Номер выбранного.</param>
    public XamlDataScope? DataAt(int index) => index >= 0 && index < _data.Length ? _data[index] : null;

    /// <summary>Тот ли это снимок или его копия на время замены — ответы у них общие.</summary>
    /// <param name="other">Другой снимок.</param>
    public bool Shares(XamlInspectorSnapshot? other) => other is not null && ReferenceEquals(other._data, _data);

    /// <summary>
    /// Раздел данных: источник — член <c>DataContext</c> со своим редактором, тип данных, компиляция привязок, а у корня —
    /// данные для дизайна.
    /// </summary>
    /// <remarks>
    /// Источник пишется классом или выражением, и длинная запись его не запирает: инспектор сам снимает её, заменяя
    /// (<see cref="FormEdits"/>). Компиляция привязок нужна редко — дополнительное, пока её не написали.
    /// </remarks>
    private static void AddData(List<Picked> picked, Dictionary<string, Spec> specs, IStudioStrings strings)
    {
        if (!specs.TryGetValue(FormEdits.DataContextMember, out var source) || source.Kind != Kind.Member || source.Member is not { } member)
            return;

        specs[FormEdits.DataContextMember] = new Spec(Kind.Data, member, source.Property with
        {
            DisplayName = strings["inspector.data.source"],
            Description = strings["inspector.data.source.description"],
            Category = DataCategory,
            Order = 0,
            IsReadOnly = !member.CanWrite,
            IsHidden = false,
            IsAdvanced = false,
            AcceptsText = true,
            Editor = DataEditor.SourceHint,
        });

        specs[FormEdits.DataTypeDirective] = new Spec(Kind.Data, null, new InspectorProperty(FormEdits.DataTypeDirective, Text)
        {
            DisplayName = strings["inspector.data.type"],
            Description = strings["inspector.data.type.description"],
            Category = DataCategory,
            Order = 1,
            Kind = InspectorPropertyKind.Directive,
            Editor = DataEditor.TypeHint,
        });

        if (picked is [{ } only] && only.Path.Equals(XamlElementPath.Root))
        {
            specs[FormEdits.DesignDataMember] = new Spec(Kind.Data, null, new InspectorProperty(FormEdits.DesignDataMember, ObjectType)
            {
                DisplayName = strings["inspector.data.design"],
                Description = strings["inspector.data.design.description"],
                Category = DataCategory,
                Order = 2,
                Kind = InspectorPropertyKind.Attached,
                OwnerTypeName = "Avalonia.Controls.Design",
                Editor = DataEditor.SourceHint,
            });
        }

        specs[FormEdits.CompileBindingsDirective] = new Spec(Kind.Data, null, new InspectorProperty(FormEdits.CompileBindingsDirective, CompileType)
        {
            DisplayName = strings["inspector.data.compile"],
            Description = strings["inspector.data.compile.description"],
            Category = DataCategory,
            Order = 3,
            Kind = InspectorPropertyKind.Directive,
            IsAdvanced = !picked.Any(static item => item.Element.GetDirectiveAttribute("CompileBindings") is not null),
            Constraints = new InspectorConstraints
            {
                ChoicesOnly = true,
                Choices =
                [
                    new InspectorChoice(string.Empty) { Title = strings["inspector.data.compile.default"] },
                    new InspectorChoice("True") { Title = strings["inspector.data.compile.yes"] },
                    new InspectorChoice("False") { Title = strings["inspector.data.compile.no"] },
                ],
            },
        });
    }

    /// <summary>Что написано у элемента для строки данных; null — не написано.</summary>
    private static string? DataWrittenAt(Picked item, Spec spec)
    {
        var element = item.Element;

        return spec.Property.Id switch
        {
            FormEdits.DataContextMember => element.GetAttribute(FormEdits.DataContextMember)?.GetValueText() ?? Instance(LongForm(item, spec)),
            FormEdits.DesignDataMember => element.Attributes
                                              .FirstOrDefault(static attribute => !attribute.IsDirective && !attribute.IsDesignTime &&
                                                                                  attribute.Name.LocalName == FormEdits.DesignDataMember)
                                              ?.GetValueText()
                                          ?? Instance(LongForm(item, spec))
                                          ?? element.GetDesignTimeAttribute(FormEdits.DataContextMember),
            FormEdits.DataTypeDirective => element.GetDirective("DataType"),
            _ => element.GetDirective("CompileBindings"),
        };
    }

    /// <summary>
    /// Что создаёт элемент свойства — записью, которая стоит сама по себе: пустой элемент класса — со своим объявлением,
    /// иначе — фрагментом разметки с объявлениями, которые он называет.
    /// </summary>
    private static string? Instance(XamlElement? written)
    {
        if (written is null)
            return null;

        if (written.Elements.ToList() is [var only])
            return DataClass.Of(only)?.Text ?? XamlFragment.From(only).ToXamlText();

        return written.Elements.Any() ? OneLine(written) : null;
    }

    /// <summary>Несколько элементов внутри элемента свойства — одной строкой: загрузка их не примет, но показать надо.</summary>
    private static string OneLine(XamlElement written)
    {
        var children = written.Elements.ToList();
        var text = written.Document.SourceText.GetText(TextSpan.FromBounds(children[0].Span.Start, children[^1].Span.End));

        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Значение строки данных у выбранного по номеру.</summary>
    private InspectorValue DataValueAt(int index, Spec spec)
    {
        var item = _picked[index];
        var scope = _data[index];
        var written = DataWrittenAt(item, spec);

        return spec.Property.Id switch
        {
            FormEdits.DataContextMember => SourceValue(item, scope, written),
            FormEdits.DesignDataMember => DesignValue(written),
            FormEdits.DataTypeDirective => TypeValue(item, scope, written),
            _ => CompileValue(item, scope, written),
        };
    }

    /// <summary>
    /// Источник данных: написанное — классом, самой формой или выражением; ненаписанное — откуда данные пришли, по
    /// ответу службы: от предка, из кода, из данных для дизайна.
    /// </summary>
    private InspectorValue SourceValue(Picked item, XamlDataScope? scope, string? written)
    {
        if (written is not null)
        {
            if (item.Path.Equals(XamlElementPath.Root) && FormEdits.IsSelf(written))
            {
                var name = item.Element.GetDirective("Class") is { } formClass ? XamlTypeName(formClass.Trim()) : item.Element.Name.LocalName;

                return new InspectorValue(Format("inspector.data.self", name)) { Written = written, Source = InspectorValueSource.Local };
            }

            if (IsExpression(written))
                return new InspectorValue(scope?.SourceType?.Name) { Written = written, Source = InspectorValueSource.Expression };

            return new InspectorValue(ClassName(written)) { Written = written, Source = InspectorValueSource.Local };
        }

        if (scope is null)
            return new InspectorValue((string?)null);

        var type = scope.SourceType?.Name ?? _strings["inspector.data.null"];
        var from = scope.SourceElement is { } path && !path.Equals(item.Path) && path.Resolve(item.Element.Document) is { } owner
            ? Titled(owner)
            : null;

        return scope.Source switch
        {
            XamlDataSource.Code => new InspectorValue(Format("inspector.data.code", type)) { Source = InspectorValueSource.Code },
            XamlDataSource.Design => new InspectorValue(Format("inspector.data.fromDesign", type))
            {
                Source = from is null ? InspectorValueSource.Default : InspectorValueSource.Inherited,
                SourceDetail = from,
            },
            XamlDataSource.None => new InspectorValue((string?)null),
            _ => new InspectorValue(from is null ? type : Format("inspector.data.inherited", type, from))
            {
                Source = InspectorValueSource.Inherited,
                SourceDetail = from,
            },
        };
    }

    /// <summary>Данные для дизайна у корня: класс, выражение или текст.</summary>
    private static InspectorValue DesignValue(string? written) =>
        written is null
            ? new InspectorValue((string?)null)
            : IsExpression(written)
                ? new InspectorValue((string?)null) { Written = written, Source = InspectorValueSource.Expression }
                : new InspectorValue(ClassName(written)) { Written = written, Source = InspectorValueSource.Design };

    /// <summary>
    /// Тип данных: написанный здесь или у предка — коротким именем; не разрешился — заметка. Не написан нигде, а привязки к
    /// данным здесь компилируются — заметка, что без него проект не соберётся.
    /// </summary>
    private InspectorValue TypeValue(Picked item, XamlDataScope? scope, string? written)
    {
        // Служба ответила про тип, который в области, — тот, что видит этот элемент.
        var missing = scope is { WrittenDataType: { } named, DataType: null }
            ? Format("inspector.data.typeMissing", named.Trim())
            : null;

        if (written is not null)
            return new InspectorValue(TypeName(written)) { Written = written, Source = InspectorValueSource.Local, Error = missing };

        for (var at = OwnerOf(item.Element); at is not null; at = OwnerOf(at))
        {
            if (at.GetDirective("DataType") is not { } inherited)
                continue;

            var from = Titled(at);

            return new InspectorValue(Format("inspector.data.inherited", TypeName(inherited), from))
            {
                Source = InspectorValueSource.Inherited,
                SourceDetail = from,
                Error = missing,
            };
        }

        var needed = scope is { NeedsDataType: true } && CompilesDataBindings(item.Element, scope.CompilesBindings);

        return new InspectorValue((string?)null) { Error = needed ? _strings["inspector.data.needsType"] : null };
    }

    /// <summary>
    /// Компиляция привязок: написанная здесь — сегментом; иначе «По умолчанию», а откуда она берётся — у предка или у
    /// проекта — говорит подсказка источника.
    /// </summary>
    private InspectorValue CompileValue(Picked item, XamlDataScope? scope, string? written)
    {
        // Сегменты зовутся True и False, а документ мог написать «true».
        if (written is not null)
        {
            var text = bool.TryParse(written.Trim(), out var set) ? set ? "True" : "False" : written.Trim();

            return new InspectorValue(text) { Written = written, Source = InspectorValueSource.Local };
        }

        for (var at = OwnerOf(item.Element); at is not null; at = OwnerOf(at))
        {
            if (at.GetDirective("CompileBindings") is { } inherited && bool.TryParse(inherited.Trim(), out var compiles))
            {
                return new InspectorValue(string.Empty)
                {
                    Source = InspectorValueSource.Inherited,
                    SourceDetail = Format("inspector.data.compile.from", Titled(at), YesNo(compiles)),
                };
            }
        }

        return scope is null
            ? new InspectorValue(string.Empty)
            : new InspectorValue(string.Empty)
            {
                Source = InspectorValueSource.Inherited,
                SourceDetail = Format("inspector.data.compile.project", YesNo(scope.CompilesByProjectDefault)),
            };
    }

    /// <summary>
    /// Есть ли у элемента и его потомков привязки к данным, которые соберёт компилятор: <c>{CompiledBinding}</c> всегда,
    /// <c>{Binding}</c> — там, где привязки компилируются. Привязки к элементу (<c>#name</c>, <c>$parent</c>,
    /// <c>ElementName</c>, <c>RelativeSource</c>, <c>Source</c>) типа данных не ждут; шаблон и элемент со своим
    /// <c>x:DataType</c> — своя область.
    /// </summary>
    /// <param name="element">Элемент.</param>
    /// <param name="compiles">Компилируются ли <c>{Binding}</c> у элемента.</param>
    internal static bool CompilesDataBindings(XamlElement element, bool compiles)
    {
        if (element.GetDirective("CompileBindings") is { } written && bool.TryParse(written.Trim(), out var set))
            compiles = set;

        if (element.Attributes.Any(attribute => attribute.GetValue() is XamlMarkupExtensionValue extension && ReadsData(extension, compiles)))
            return true;

        foreach (var child in element.Elements)
        {
            if (child.Name.LocalName.EndsWith("Template", StringComparison.Ordinal) || child.GetDirectiveAttribute("DataType") is not null)
                continue;

            if (CompilesDataBindings(child, compiles))
                return true;
        }

        return false;
    }

    /// <summary>Привязка читает данные и компилируется.</summary>
    private static bool ReadsData(XamlMarkupExtensionValue extension, bool compiles)
    {
        var compiled = extension.TypeName.LocalName switch
        {
            "CompiledBinding" => true,
            "Binding" => compiles,
            _ => false,
        };

        if (!compiled || extension.GetArgument("ElementName") is not null || extension.GetArgument("RelativeSource") is not null ||
            extension.GetArgument("Source") is not null)
        {
            return false;
        }

        return (extension.PositionalArguments.FirstOrDefault() ?? extension.GetArgument("Path"))?.Value is not XamlLiteralValue { Text: { } path }
               || path.TrimStart() is not ['#' or '$', ..];
    }

    /// <summary>
    /// Что создаёт запись — именем класса: <c>Main</c> и у пустого элемента, и у элемента со свойствами; текст —
    /// как есть.
    /// </summary>
    private static string ClassName(string written) =>
        DataClass.Parse(written)?.Name ??
        (written.TrimStart().StartsWith('<') && XamlFragment.Parse(written).Root is { IsPropertyElementSyntax: false } root
            ? root.Name.LocalName
            : written);

    /// <summary>Тип данных коротким именем: <c>vm:Main</c> и <c>{x:Type vm:Main}</c> — <c>Main</c>.</summary>
    private static string TypeName(string written)
    {
        var name = written.Trim();

        if (XamlValue.Parse(name) is XamlMarkupExtensionValue extension &&
            (extension.PositionalArguments.FirstOrDefault() ?? extension.GetArgument("TypeName"))?.Value is XamlLiteralValue { Text: { } argument })
        {
            name = argument.Trim();
        }

        var colon = name.IndexOf(':', StringComparison.Ordinal);

        return colon < 0 ? name : name[(colon + 1)..];
    }

    /// <summary>Текст значения — выражение разметки, а не значение: <c>{Binding …}</c>, но не экранированное <c>{}…</c>.</summary>
    private static bool IsExpression(string text)
    {
        var trimmed = text.TrimStart();

        return trimmed.StartsWith('{') && !trimmed.StartsWith("{}", StringComparison.Ordinal);
    }

    /// <summary>Элемент-владелец: родитель в документе, минуя элемент свойства.</summary>
    private static XamlElement? OwnerOf(XamlElement element)
    {
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is XamlElement { IsPropertyElementSyntax: false } owner)
                return owner;
        }

        return null;
    }

    private string YesNo(bool value) => _strings[value ? "inspector.data.compile.yes" : "inspector.data.compile.no"].ToLower(CultureInfo.CurrentCulture);

    private string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, _strings[key], arguments);
}
