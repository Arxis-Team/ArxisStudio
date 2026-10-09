using System.Globalization;
using ArxisStudio.Inspector;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Documents;
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
            _lastLive = snapshot;
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

    private InspectorCheck CheckName(XamlInspectorSnapshot snapshot, string text)
    {
        if (!(char.IsLetter(text[0]) || text[0] == '_') || !text.All(static letter => char.IsLetterOrDigit(letter) || letter == '_'))
            return InspectorCheck.Invalid(Strings["inspector.name.invalid"]);

        if (Slot.Session.Document?.Syntax.Root is not { } root)
            return InspectorCheck.Valid;

        var self = snapshot.Elements.ToHashSet();
        var taken = root.DescendantElements().Prepend(root)
            .Any(other => !self.Contains(other) && string.Equals(other.GetDirective("Name"), text, StringComparison.Ordinal));

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
