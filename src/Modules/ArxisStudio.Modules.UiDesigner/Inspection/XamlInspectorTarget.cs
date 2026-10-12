using System.Globalization;
using ArxisStudio.Icons;
using ArxisStudio.Inspector;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Projects;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia.Threading;

namespace ArxisStudio.Modules.UiDesigner.Inspection;

/// <summary>
/// Цель инспектора — выбранное в форме: элементы её документа по путям, их члены и написанное.
/// </summary>
/// <remarks>
/// <para>
/// <b>Правка — правка формы.</b> Запись идёт через правки документа (<see cref="FormEdits.SetAsync"/>) одним шагом
/// его истории — тот же шаг слышит история доски, — а Ctrl+Z в инспекторе — шаг истории того, кто держит холст
/// (<see cref="IFormCanvasHost.Step"/>): у вкладки — документа, у доски — её общей.
/// </para>
/// <para>
/// <b>Снимок — по требованию.</b> Члены, значения и написанное читаются, когда их спросили (<see cref="XamlInspectorSnapshot"/>),
/// и помнятся до перемены формы: выбор на холсте без открытой панели ничего не стоит. Перемену — текст, показ,
/// состояние — цель объявляет сама, и панель переспрашивает только видные строки.
/// </para>
/// <para>
/// <b>Элементы — путями.</b> Путь переживает замену поколения, но не правку, сдвинувшую соседей: выбранное на
/// холсте холст выбирает заново, а цель, которую держит замок инспектора, проверяет, что на её путях стоят
/// элементы того же типа, — и закрывается, если нет: править чужой элемент хуже, чем показать, что своего нет.
/// </para>
/// <para>
/// <b>Данные и выражения — ответом службы.</b> Что читают привязки и работают ли выражения, служба XAML отвечает
/// асинхронно: снимок читается сразу — вид и запись выражения по одному синтаксису, — а ответ кладётся в него после
/// (<see cref="EnrichAsync"/>), и переспрашиваются только строки данных и выражений. Модели проекта —
/// варианты источника и типа — цель берёт у показа на каждое поколение заранее (<see cref="FetchDataTypesAsync"/>):
/// выбор открывается сразу, без ожидания.
/// </para>
/// </remarks>
internal sealed class XamlInspectorTarget : InspectorTarget
{
    private readonly IStudioContext _context;
    private readonly FormCanvas _canvas;
    private readonly HashSet<string> _included = new(StringComparer.Ordinal);
    private readonly string[] _types;
    private XamlInspectorSnapshot? _snapshot;
    private XamlInspectorSnapshot? _lastLive;
    private bool _rereading;

    // Модели проекта формы — именами; берутся заново, когда сменилось поколение.
    private IReadOnlyList<XamlDataTypeInfo> _dataTypes = [];
    private bool _dataTypesStale = true;

    /// <summary>Заводит цель для выбранного в форме.</summary>
    /// <param name="context">Контекст модуля.</param>
    /// <param name="canvas">Холст, на котором выбрано.</param>
    /// <param name="slot">Форма.</param>
    /// <param name="paths">Пути выбранных, первый главный.</param>
    public XamlInspectorTarget(IStudioContext context, FormCanvas canvas, FormSlot slot, IReadOnlyList<XamlElementPath> paths)
    {
        _context = context;
        _canvas = canvas;
        Slot = slot;
        Paths = paths;
        _types = [.. paths.Select(path => slot.Session.Document is { } document ? path.Resolve(document.Syntax)?.Name.ToString() ?? string.Empty : string.Empty)];
        slot.Session.Changed += OnSessionChanged;
    }

    /// <summary>Форма.</summary>
    public FormSlot Slot { get; }

    /// <summary>Пути выбранных.</summary>
    public IReadOnlyList<XamlElementPath> Paths { get; }

    /// <inheritdoc/>
    public override InspectorHeader Header => Snapshot?.Header ?? new InspectorHeader(string.Empty);

    /// <inheritdoc/>
    public override InspectorTypeInfo Type => Snapshot?.Type ?? new InspectorTypeInfo(string.Empty);

    /// <inheritdoc/>
    public override int Count => Paths.Count;

    /// <inheritdoc/>
    public override IReadOnlyList<InspectorProperty> Properties => Snapshot?.Properties ?? [];

    /// <inheritdoc/>
    public override IReadOnlyCollection<string>? WrittenIds => Snapshot?.Written;

    private IStudioStrings Strings => _context.Strings;

    /// <summary>Снимок формы; null — документа нет.</summary>
    private XamlInspectorSnapshot? Snapshot => _snapshot ??= Read();

    /// <inheritdoc/>
    public override InspectorValue ValueOf(string id) => Snapshot?.ValueOf(id) ?? InspectorValue.Empty;

    /// <inheritdoc/>
    /// <remarks>
    /// Имя элемента становится полем класса формы — значит, это идентификатор, и область имён держит его один раз:
    /// второй элемент с тем же именем форма не загрузит. Значение члена проверяет загрузка — тем же разбором, каким
    /// прочтёт файл.
    /// </remarks>
    public override InspectorCheck Check(string id, string text)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0 || Snapshot is not { } snapshot)
            return InspectorCheck.Valid;

        if (string.Equals(id, FormEdits.NameDirective, StringComparison.Ordinal))
            return CheckName(snapshot, text);

        if (snapshot.DataIds.Contains(id))
            return CheckData(snapshot, id, text);

        if (Slot.Session.Shown is not { } view)
            return InspectorCheck.Valid;

        foreach (var path in snapshot.Paths)
        {
            if (view.CheckValue(path, id, text).Error is { } error)
                return InspectorCheck.Invalid(error);
        }

        return InspectorCheck.Valid;
    }

    /// <inheritdoc/>
    public override Task<bool> WriteAsync(IReadOnlyList<InspectorWrite> writes, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(writes);

        if (Slot.Session.Edits is not { } edits || Snapshot is not { } snapshot || writes.Count == 0)
            return Task.FromResult(false);

        // Только для чтения — не только у панели: пункт меню или раздел плагина пишет той же дорогой.
        if (writes.Any(write => snapshot.Refuses(write.PropertyId)))
            return Task.FromResult(false);

        return edits.SetAsync([.. snapshot.Paths], [.. writes.Select(write => snapshot.Spell(write.PropertyId, write.Text))], label);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Протяжку видно на объектах показа — у каждого выбранного (<see cref="IXamlDesignView.Preview"/>), — а в
    /// документ ложится одна правка при отпускании. Предпросмотр службы держит замену поколения сам. Что показать на
    /// объекте нечем — прямое свойство, свойство CLR, размер корня, имя, член для дизайна, — протяжка пишет при
    /// отпускании.
    /// </remarks>
    public override IInspectorPreview? BeginPreview(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (Slot.Session.Shown is not { } view || Snapshot is not { } snapshot || !snapshot.Previews(id))
            return null;

        var previews = new List<IXamlValuePreview>();

        foreach (var path in snapshot.Paths)
        {
            if (view.Preview(path, id) is not { } preview)
            {
                foreach (var started in previews)
                    started.Dispose();

                return null;
            }

            previews.Add(preview);
        }

        return previews.Count == 0 ? null : new MemberPreview(this, id, previews);
    }

    /// <inheritdoc/>
    /// <remarks>Недописанное значение держит замену поколения: значение, набранное для старых типов, ещё пригодится.</remarks>
    public override IDisposable? Hold(string id) => _context.XamlDesign()?.Defer(Strings["inspector.hold"]);

    /// <inheritdoc/>
    /// <remarks>
    /// Картинке — картинки решения, ставшие ресурсами (<see cref="ProjectImages"/>): своего проекта путём от корня
    /// сборки, чужого — полным адресом. Снимок решения берётся, когда спросили: файл, добавленный в проект, уже в списке.
    /// </remarks>
    public override IReadOnlyList<InspectorChoice> Suggest(string id, string typed)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(typed);

        if (Snapshot is { } snapshot && snapshot.DataIds.Contains(id))
            return DataChoices(snapshot, id, typed);

        return Properties.FirstOrDefault(property => string.Equals(property.Id, id, StringComparison.Ordinal)) is { } property
               && ProjectImages.Types.Contains(property.ValueType.FullName)
               && _context.Projects()?.Current is { } solution
            ? ProjectImages.Choices(solution, Slot.Session.Path, typed)
            : [];
    }

    /// <inheritdoc/>
    public override Task<bool> StepAsync(bool back)
    {
        if (_canvas.IsDisposed)
            return Task.FromResult(false);

        _canvas.Host.Step(back);

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// У разных значений — «Взять у…»: значение одного из выбранных — всем, одной правкой. Это то, что «—» не
    /// говорит: какие значения разные и у кого.
    /// </para>
    /// <para>
    /// «Показать в XAML» ведёт к атрибуту в просмотре текста формы. «Только для дизайна» переносит значение в член для
    /// дизайна (<c>d:Width</c>): его видно в дизайнере, а собранная форма его не знает, — одной правкой с
    /// <c>mc:Ignorable</c>, без которого <c>d:</c> не соберётся.
    /// </para>
    /// </remarks>
    public override void FillMenu(InspectorMenuContext menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        if (menu.Property is not { } property || Snapshot is not { Count: > 0 } snapshot)
            return;

        var id = property.Id;

        if (menu.Value is { IsMixed: true })
        {
            for (var at = 0; at < snapshot.Count; at++)
            {
                var text = snapshot.WrittenAt(id, at);
                var title = string.Format(
                    CultureInfo.CurrentCulture, Strings["inspector.menu.take"], snapshot.NameAt(at), text ?? Strings["inspector.menu.unset"]);

                menu.Add(title, () => _ = WriteAsync([new InspectorWrite(id, text)]), isEnabled: !property.IsReadOnly);
            }

            menu.AddSeparator();
        }

        menu.Add(Strings["inspector.menu.reveal"], () => Reveal(snapshot, id), isEnabled: snapshot.Count == 1 && snapshot.WrittenAt(id, 0) is not null);

        if (string.Equals(id, FormEdits.DataTypeDirective, StringComparison.Ordinal) && SuggestedType(snapshot) is { } suggested)
            menu.Add(string.Format(CultureInfo.CurrentCulture, Strings["inspector.data.setType"], suggested.Title), () => _ = WriteAsync([new InspectorWrite(id, suggested.Value)]));

        if (snapshot.Designs(id))
        {
            var value = menu.Value;
            var text = value is { IsMixed: false, Source: not InspectorValueSource.Expression } ? value.Written ?? value.Text : null;

            menu.Add(
                Strings["inspector.menu.designOnly"],
                () => _ = WriteAsync([new InspectorWrite(FormEdits.DesignPrefix + id, text), new InspectorWrite(id, null)]),
                isEnabled: !string.IsNullOrEmpty(text));
        }
    }

    /// <inheritdoc/>
    public override void Include(IReadOnlyCollection<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var before = _included.Count;

        _included.UnionWith(ids);

        if (_included.Count == before)
            return;

        _snapshot = null;
        Raise(InspectorChanges.Properties);
    }

    /// <summary>
    /// Читает выбранное заново. Пока поколение заменяют, строки остаются с последним живым снимком: замена идёт после
    /// каждой сборки кода, и инспектор, сбрасывающий на это время строки к написанному, терял бы каретку и место в
    /// списке. Снимок держит только имена и текст, и замене он не мешает.
    /// </summary>
    private XamlInspectorSnapshot? Read()
    {
        if (Slot.Session.Document is not { } document)
            return null;

        var snapshot = XamlInspectorSnapshot.Read(Slot.Session.Shown, document, Paths, _included, Strings);

        if (snapshot.IsLive)
        {
            // До нового ответа службы строки показывают прежний: иначе каждая правка мигала бы в них.
            if (_lastLive is { } previous)
                snapshot.Seed(previous);

            _lastLive = snapshot;

            if (Slot.Session.Shown is { } view)
            {
                _ = EnrichAsync(view, snapshot);

                if (snapshot.DataIds.Count > 0 && _dataTypesStale)
                {
                    _dataTypesStale = false;
                    _ = FetchDataTypesAsync(view);
                }
            }

            return snapshot;
        }

        // Текст не читается — показ прежнего текста не годится: видно только написанное. Иначе объекты просто ушли на
        // замену, и строки остаются.
        if (document.State is not (XamlDocumentState.Behind or XamlDocumentState.Broken) && _lastLive is { } last)
            return last.Swapping(Strings["inspector.notice.swap"]);

        _lastLive = null;

        return snapshot;
    }

    /// <summary>Ведёт к записи свойства в XAML формы.</summary>
    private void Reveal(XamlInspectorSnapshot snapshot, string id)
    {
        if (!_canvas.IsDisposed && snapshot.Paths.FirstOrDefault() is { } path)
            _canvas.RevealInCode(Slot, path, snapshot.Locate(id, 0));
    }

    /// <summary>Цель больше не нужна: выбор сменился, форму сняли, холст ушёл.</summary>
    public void Shut()
    {
        Slot.Session.Changed -= OnSessionChanged;
        Close();
    }

    /// <summary>
    /// У формы сменилось то, что инспектор показывает: текст, показ, состояние; закрыта — цели больше нет.
    /// </summary>
    private void OnSessionChanged(object? sender, FormChanges changes)
    {
        if (IsClosed)
            return;

        if (changes.HasFlag(FormChanges.Closed) || changes.HasFlag(FormChanges.Deleted) || Moved())
        {
            Shut();
            return;
        }

        if ((changes & (FormChanges.Text | FormChanges.State | FormChanges.Objects | FormChanges.Root | FormChanges.Opened)) == 0)
            return;

        // Новый корень — новое поколение или новый показ: модели проекта берутся заново.
        if ((changes & (FormChanges.Root | FormChanges.Opened)) != 0)
            _dataTypesStale = true;

        Reread();
    }

    /// <summary>
    /// Переспрашивает выбранное, когда показ успокоился, — один раз на череду перемен.
    /// </summary>
    /// <remarks>
    /// Перемена приходит посреди показа: правка ставит объекту значения на месте, и стили формы на это время
    /// снимаются и встают заново. Прочитанное тут же было бы значением темы, а не стиля формы, — и осталось бы в
    /// строке до следующей перемены. До перечитывания цель отвечает прежним снимком: имена и текст в нём верны.
    /// </remarks>
    private void Reread()
    {
        if (_rereading)
            return;

        _rereading = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rereading = false;

            if (IsClosed)
                return;

            _snapshot = null;
            Raise(InspectorChanges.Properties | InspectorChanges.Values | InspectorChanges.Header);
        }, DispatcherPriority.Background);
    }

    /// <summary>На путях цели стоят уже не её элементы: правка сдвинула соседей или убрала выбранное.</summary>
    private bool Moved()
    {
        if (Slot.Session.Document is not { } document)
            return false;

        var syntax = document.Syntax;

        return Paths.Where((path, at) => path.Resolve(syntax) is not { } element || element.Name.ToString() != _types[at]).Any();
    }

    /// <summary>
    /// Спрашивает у службы, что читают привязки выбранных и работают ли их выражения, и кладёт ответ в снимок — его и
    /// его копию на время замены: ответ — имена, и замену он переживает.
    /// </summary>
    private async Task EnrichAsync(IXamlDesignView view, XamlInspectorSnapshot snapshot)
    {
        var paths = snapshot.Paths.ToList();
        var scopes = new XamlDataScope?[paths.Count];
        var checks = new IReadOnlyList<XamlExpressionCheck>?[paths.Count];

        try
        {
            for (var at = 0; at < paths.Count; at++)
            {
                if (snapshot.DataIds.Count > 0)
                    scopes[at] = await view.DescribeDataAsync(paths[at]).ConfigureAwait(true);

                checks[at] = await view.CheckExpressionsAsync(paths[at]).ConfigureAwait(true);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            // Показ отдал объекты или закрыт, пока служба отвечала: снимок перечитают с новым.
            return;
        }

        snapshot.Describe(scopes);
        snapshot.Check(checks);

        if (!IsClosed && snapshot.Shares(_snapshot))
            Raise(InspectorChanges.Values, [.. snapshot.DataIds.Union(snapshot.CheckedIds, StringComparer.Ordinal)]);
    }

    /// <summary>Берёт модели проекта формы — варианты источника и типа данных.</summary>
    private async Task FetchDataTypesAsync(IXamlDesignView view)
    {
        try
        {
            _dataTypes = await view.GetDataTypesAsync().ConfigureAwait(true);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // Показа уже нет: возьмём у следующего.
            _dataTypesStale = true;
        }
    }

    /// <summary>
    /// Варианты строки данных: у корня с <c>x:Class</c> первой — сама форма, затем собранные модели по пространствам.
    /// Источнику — те, что форма создаст; типу — любые модели. Набранное выражение — не выбор класса: подсказок нет.
    /// </summary>
    private List<InspectorChoice> DataChoices(XamlInspectorSnapshot snapshot, string id, string typed)
    {
        if (string.Equals(id, FormEdits.CompileBindingsDirective, StringComparison.Ordinal) || typed.TrimStart().StartsWith('{'))
            return [];

        var typeOnly = string.Equals(id, FormEdits.DataTypeDirective, StringComparison.Ordinal);
        var choices = new List<InspectorChoice>();

        if (!string.Equals(id, FormEdits.DesignDataMember, StringComparison.Ordinal) && snapshot.FormClass is { } formClass &&
            FormEdits.ClassOf(formClass) is { } self)
        {
            choices.Add(new InspectorChoice(typeOnly ? self.Text : "{Binding $self}")
            {
                Title = string.Format(CultureInfo.CurrentCulture, Strings["inspector.data.self"], self.Name),
                Description = Strings["inspector.data.self.description"],
                Icon = AxIcons.Window,
            });
        }

        choices.AddRange(_dataTypes
            .Where(type => DataEditor.Refusal(type, typeOnly, Strings) is null)
            .Where(type => typed.Length == 0 || type.Name.Contains(typed, StringComparison.CurrentCultureIgnoreCase) ||
                           type.FullName.Contains(typed, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(static type => type.ClrNamespace, StringComparer.Ordinal)
            .ThenBy(static type => type.Name, StringComparer.Ordinal)
            .Select(type => DataEditor.ChoiceOf(type, Strings)));

        return choices;
    }

    /// <summary>
    /// Прочтётся ли запись данных: класс — собранная модель, которую форма создаст; выражение — да; иное — нет.
    /// Тип данных пишется классом или именем под префиксом, который форма знает; компиляция привязок — да или нет.
    /// </summary>
    private InspectorCheck CheckData(XamlInspectorSnapshot snapshot, string id, string text)
    {
        var trimmed = text.Trim();

        if (string.Equals(id, FormEdits.CompileBindingsDirective, StringComparison.Ordinal))
            return bool.TryParse(trimmed, out _) ? InspectorCheck.Valid : InspectorCheck.Invalid(Strings["inspector.data.compile.invalid"]);

        var typeOnly = string.Equals(id, FormEdits.DataTypeDirective, StringComparison.Ordinal);

        if (DataClass.Parse(trimmed) is { } written)
        {
            // Пока моделей не знаем — не знаем и отказа: запись проверит загрузка.
            if (_dataTypes.Count == 0)
                return InspectorCheck.Valid;

            var known = _dataTypes.FirstOrDefault(type => string.Equals(type.Name, written.Name, StringComparison.Ordinal) &&
                                                          (string.Equals(type.XmlNamespace, written.XmlNamespace, StringComparison.Ordinal) ||
                                                           string.Equals(type.FullName, written.ClrName, StringComparison.Ordinal)));

            // Класс формы — тип данных самой формы, когда она свои же данные.
            return known is null
                ? InspectorCheck.Invalid(string.Format(CultureInfo.CurrentCulture, Strings["inspector.data.unknown"], written.Name))
                : typeOnly && known.IsFormClass || DataEditor.Refusal(known, typeOnly, Strings) is not { } refusal
                    ? InspectorCheck.Valid
                    : InspectorCheck.Invalid(refusal);
        }

        if (!typeOnly)
            return trimmed.StartsWith('{') ? InspectorCheck.Valid : InspectorCheck.Invalid(Strings["inspector.data.invalid"]);

        // Тип данных именем: префикс должен быть объявлен там, где его прочтут, — у корня или у самого элемента.
        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);

        if (trimmed.StartsWith('{') || colon < 0)
            return InspectorCheck.Valid;

        var prefix = trimmed[..colon];

        return snapshot.Elements.All(element => element.NamespaceContext.LookupNamespace(prefix) is not null)
            ? InspectorCheck.Valid
            : InspectorCheck.Invalid(string.Format(CultureInfo.CurrentCulture, Strings["inspector.data.prefix"], prefix));
    }

    /// <summary>
    /// Тип данных, который стоит предложить одному выбранному: тип его данных сейчас, если это собранная модель, а
    /// тип данных не написан ни у него, ни выше.
    /// </summary>
    private InspectorChoice? SuggestedType(XamlInspectorSnapshot snapshot)
    {
        if (snapshot.Count != 1 || snapshot.DataAt(0) is not { WrittenDataType: null, SourceType: { } current })
            return null;

        return _dataTypes.FirstOrDefault(type => string.Equals(type.FullName, current.FullName, StringComparison.Ordinal)) is { } known &&
               DataEditor.Refusal(known, typeOnly: true, Strings) is null
            ? DataEditor.ChoiceOf(known, Strings)
            : null;
    }

    private InspectorCheck CheckName(XamlInspectorSnapshot snapshot, string text)
    {
        if (!(char.IsLetter(text[0]) || text[0] == '_') || !text.All(static letter => char.IsLetterOrDigit(letter) || letter == '_'))
            return InspectorCheck.Invalid(Strings["inspector.name.invalid"]);

        if (Slot.Session.Document?.Syntax.Root is not { } root)
            return InspectorCheck.Valid;

        var self = snapshot.Elements.ToHashSet();
        // Занятым считается и имя, написанное свойством Name: Avalonia читает его так же (запись 390).
        var taken = root.DescendantElements().Prepend(root)
            .Any(other => !self.Contains(other) && string.Equals(other.Identity, text, StringComparison.Ordinal));

        return taken
            ? InspectorCheck.Invalid(string.Format(CultureInfo.CurrentCulture, Strings["inspector.name.taken"], text))
            : InspectorCheck.Valid;
    }

    /// <summary>Предпросмотр члена у каждого выбранного; при отпускании — одна правка формы.</summary>
    private sealed class MemberPreview(XamlInspectorTarget target, string id, List<IXamlValuePreview> previews) : IInspectorPreview
    {
        private bool _ended;

        public InspectorCheck Show(string text)
        {
            ArgumentNullException.ThrowIfNull(text);

            if (_ended)
                return InspectorCheck.Valid;

            foreach (var preview in previews)
            {
                if (preview.Show(text).Error is { } error)
                    return InspectorCheck.Invalid(error);
            }

            return InspectorCheck.Valid;
        }

        public async Task<bool> CommitAsync(string text)
        {
            ArgumentNullException.ThrowIfNull(text);

            if (_ended)
                return false;

            // Записанное приходит объектам правкой текста, и предпросмотр службы на ней кончается сам; не записалось —
            // объекты возвращаются к тому, что было.
            try
            {
                return await target.WriteAsync([new InspectorWrite(id, text)]).ConfigureAwait(true);
            }
            finally
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            if (_ended)
                return;

            _ended = true;

            foreach (var preview in previews)
                preview.Dispose();
        }
    }
}
