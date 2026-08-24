using System.Collections.ObjectModel;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Design;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using Avalonia.Controls;

namespace ArxisStudio.Services;

/// <summary>
/// Открытый в дизайнере документ: разметка, живые объекты, дерево элементов и
/// правки с историей.
/// </summary>
/// <remarks>
/// Документ владеет четырьмя вещами сразу — рабочей областью разметки, сессией
/// загрузки, поверхностью показа и контекстом сборок проекта, — и разрушать их
/// нужно в обратном порядке, иначе сборки проекта останутся заняты и следующая
/// сборка не запишет файлы. Поэтому создание, правка и закрытие живут в одном
/// типе.
/// <para>
/// История правок — не своя: её ведёт рабочая область разметки, потому что одна
/// правка может затронуть несколько документов, и своя история у панели
/// разошлась бы с общей при первой же такой правке.
/// </para>
/// </remarks>
public sealed class DesignDocument : IAsyncDisposable
{
    private readonly XamlLoadOptions _options = new() { Mode = XamlLoadMode.Design };

    private XamlLoadEnvironment _environment;
    private MarkupDocumentId _documentId;
    private XamlWorkspace? _workspace;
    private XamlLoadSession? _session;
    private ProjectXamlPopulation? _population;
    private ProjectAssemblyContext? _assemblies;

    private DesignDocument(string filePath, XamlDesignSurface surface, XamlLoadEnvironment environment)
    {
        FilePath = filePath;
        Surface = surface;
        _environment = environment;
    }

    /// <summary>Содержимое документа заменилось целиком: дерево нужно перечитать.</summary>
    public event EventHandler? Reloaded;

    /// <summary>Путь к файлу разметки.</summary>
    public string FilePath { get; }

    /// <summary>Имя файла — то, что показывается на вкладке.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Поверхность с живым содержимым документа; её кладут на канву.</summary>
    public XamlDesignSurface Surface { get; }

    /// <summary>Дерево элементов документа; один корень.</summary>
    public ObservableCollection<HierarchyNode> Nodes { get; } = [];

    /// <summary>Документ загрузился, но показывать в нём нечего.</summary>
    public bool IsEmpty => !Surface.HasContent;

    /// <summary>В документе есть правки, не записанные на диск.</summary>
    public bool IsModified { get; private set; }

    /// <summary>Есть что отменить.</summary>
    public bool CanUndo => _workspace?.CanUndo ?? false;

    /// <summary>Есть что вернуть.</summary>
    public bool CanRedo => _workspace?.CanRedo ?? false;

    /// <summary>Сессия загрузки: по ней читаются свойства живых объектов.</summary>
    internal XamlLoadSession? Session => _session;

    /// <summary>Текущий разбор документа.</summary>
    internal XamlDocument? Document => _workspace?.GetDocument(_documentId);

    /// <summary>
    /// Открывает документ проекта: разбирает разметку, поднимает живые объекты
    /// и строит дерево.
    /// </summary>
    /// <param name="filePath">Путь к файлу <c>.axaml</c>.</param>
    /// <param name="snapshot">Снапшот модели решения.</param>
    /// <param name="project">Проект, которому принадлежит файл.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Документ или сообщение, почему открыть не удалось.</returns>
    public static async Task<(DesignDocument? Document, string? Error)> OpenAsync(
        string filePath,
        SolutionSnapshot snapshot,
        ProjectSnapshot project,
        CancellationToken cancellationToken = default)
    {
        var (environment, assemblies) = ProjectXamlEnvironment.CreateFor(snapshot, project.Identity);

        XamlWorkspace? workspace = null;
        ProjectXamlPopulation? population = null;

        try
        {
            var uri = new Uri(filePath);
            var markup = new MarkupWorkspace(environment.SourceProvider);

            workspace = new XamlWorkspace(markup);

            var parsed = await workspace.OpenAsync(uri, cancellationToken);

            if (!markup.TryGetDocumentByUri(uri, out var opened))
            {
                workspace.Dispose();
                assemblies.Dispose();
                return (null, "Рабочая область не приняла документ");
            }

            // Открытие документа для рабочей области — такая же правка, как
            // всякая другая, и лежит в истории первой записью. Отменить её
            // означало бы закрыть документ, которым мы сейчас заняты, поэтому
            // только что открытому документу отменять нечего.
            markup.ClearHistory();

            // Документ с x:Class уже имеет скомпилированный близнец в сборке
            // проекта, и загрузчик выберет именно его — то есть разметку с
            // последней сборки. Регистрация сообщает поколению, что содержимое
            // такого класса берётся из документа, открытого сейчас.
            population = ProjectXamlPopulation.Create(assemblies, environment);
            await population.SetDocumentAsync(parsed, cancellationToken);

            var (session, result) = await XamlLoadSession.TryCreateAsync(
                parsed, environment, new XamlLoadOptions { Mode = XamlLoadMode.Design }, cancellationToken);

            if (session is null)
            {
                population.Dispose();
                workspace.Dispose();
                assemblies.Dispose();
                return (null, Describe(result));
            }

            var surface = new XamlDesignSurface();
            surface.Attach(session);

            var document = new DesignDocument(filePath, surface, environment)
            {
                _documentId = opened.Id,
                _workspace = workspace,
                _session = session,
                _population = population,
                _assemblies = assemblies,
            };

            document.BuildTree(parsed.Root, session);
            return (document, null);
        }
        catch (OperationCanceledException)
        {
            population?.Dispose();
            workspace?.Dispose();
            assemblies.Dispose();
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UriFormatException or InvalidOperationException)
        {
            population?.Dispose();
            workspace?.Dispose();
            assemblies.Dispose();
            return (null, e.Message);
        }
    }

    /// <summary>Задаёт атрибут элемента; пустое значение убирает атрибут.</summary>
    /// <param name="node">Узел дерева, чей элемент правим.</param>
    /// <param name="name">Имя свойства, как оно пишется в разметке.</param>
    /// <param name="text">Новое значение или null/пусто, чтобы сбросить.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>null, если правка применилась, иначе — почему нет.</returns>
    public async Task<string?> SetAttributeAsync(
        HierarchyNode node,
        string name,
        string? text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_workspace is null || _session is null)
            return "Документ закрыт";

        var document = _workspace.GetDocument(_documentId);

        if (node.Path?.Resolve(document) is not { } element)
            return "Элемент больше не существует";

        var qualified = XamlQualifiedName.Parse(name);
        var clearing = string.IsNullOrWhiteSpace(text);
        var current = element.GetAttribute(qualified)?.GetValueText();

        // Правка, ничего не меняющая, не должна попадать в историю: иначе
        // отмена начнёт возвращать шаги, которых человек не делал. А их будет
        // много — поля инспектора отдают своё значение и просто получив фокус.
        if (clearing ? current is null : string.Equals(current, text, StringComparison.Ordinal))
            return null;

        // Значение проверяется до правки: наполовину набранное значение не
        // должно ни попасть в историю, ни сломать живые объекты.
        if (!clearing && Validate(node, name, text!) is { } invalid)
            return invalid;

        var editor = clearing
            ? document.Edit().RemoveAttribute(element, qualified)
            : document.Edit().SetAttribute(element, qualified, text!);

        var edited = _workspace.Apply(editor, $"{element.Name.LocalName}.{name}");

        return await ApplyAsync(edited, _workspace.Undo, cancellationToken);
    }

    /// <summary>
    /// Задаёт несколько атрибутов одного элемента одной записью в истории.
    /// </summary>
    /// <param name="node">Узел дерева, чей элемент правим.</param>
    /// <param name="values">Свойства и их значения; пустое значение убирает атрибут.</param>
    /// <param name="description">Чем эта правка называется в истории.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>null, если правка применилась, иначе — почему нет.</returns>
    /// <remarks>
    /// Один жест на канве меняет и размер, и положение, а отменяться должен
    /// целиком: разбитый на две записи, он потребовал бы двух отмен, из которых
    /// первая оставила бы элемент в состоянии, которого не было никогда.
    /// </remarks>
    public async Task<string?> SetAttributesAsync(
        HierarchyNode node,
        IReadOnlyList<(string Name, string? Text)> values,
        string description,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(values);

        if (_workspace is null || _session is null)
            return "Документ закрыт";

        var document = _workspace.GetDocument(_documentId);

        if (node.Path?.Resolve(document) is not { } element)
            return "Элемент больше не существует";

        var editor = document.Edit();
        var changed = false;

        foreach (var (name, text) in values)
        {
            var qualified = XamlQualifiedName.Parse(name);
            var current = element.GetAttribute(qualified)?.GetValueText();
            var clearing = string.IsNullOrWhiteSpace(text);

            if (clearing ? current is null : string.Equals(current, text, StringComparison.Ordinal))
                continue;

            if (!clearing && Validate(node, name, text!) is { } invalid)
                return invalid;

            editor = clearing
                ? editor.RemoveAttribute(element, qualified)
                : editor.SetAttribute(element, qualified, text!);

            changed = true;
        }

        if (!changed)
            return null;

        return await ApplyAsync(_workspace.Apply(editor, description), _workspace.Undo, cancellationToken);
    }

    /// <summary>Отменяет последнюю правку.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>null, если отмена применилась, иначе — почему нет.</returns>
    public Task<string?> UndoAsync(CancellationToken cancellationToken = default) =>
        StepHistoryAsync(undo: true, cancellationToken);

    /// <summary>Возвращает отменённую правку.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>null, если возврат применился, иначе — почему нет.</returns>
    public Task<string?> RedoAsync(CancellationToken cancellationToken = default) =>
        StepHistoryAsync(undo: false, cancellationToken);

    /// <summary>Записывает текущий текст документа на диск.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (_workspace is null)
            return;

        await File.WriteAllTextAsync(FilePath, _workspace.GetDocument(_documentId).GetText(), cancellationToken);
        IsModified = false;
    }

    /// <summary>Находит узел дерева по живому контролу.</summary>
    /// <param name="control">Контрол на канве.</param>
    /// <remarks>
    /// Клик мог попасть внутрь шаблона, где объекты документом не объявлены,
    /// поэтому поиск поднимается вверх, пока не найдёт объявленный предок.
    /// </remarks>
    public HierarchyNode? FindNode(Control? control)
    {
        for (var current = control; current is not null; current = current.Parent as Control)
        {
            if (Find(Nodes, current) is { } found)
                return found;
        }

        return null;

        static HierarchyNode? Find(IEnumerable<HierarchyNode> nodes, Control control)
        {
            foreach (var node in nodes)
            {
                if (ReferenceEquals(node.Control, control))
                    return node;

                if (Find(node.Children, control) is { } found)
                    return found;
            }

            return null;
        }
    }

    /// <summary>Возвращает путь от корня документа до узла контрола.</summary>
    /// <param name="control">Контрол на канве.</param>
    /// <remarks>
    /// Дереву мало знать сам узел: чтобы показать выделение, нужно раскрыть
    /// всех его предков.
    /// </remarks>
    public IReadOnlyList<HierarchyNode> FindPath(Control? control)
    {
        for (var current = control; current is not null; current = current.Parent as Control)
        {
            var path = new List<HierarchyNode>();

            if (Search(Nodes, current, path))
                return path;
        }

        return [];

        static bool Search(IEnumerable<HierarchyNode> nodes, Control control, List<HierarchyNode> path)
        {
            foreach (var node in nodes)
            {
                path.Add(node);

                if (ReferenceEquals(node.Control, control) || Search(node.Children, control, path))
                    return true;

                path.RemoveAt(path.Count - 1);
            }

            return false;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Surface.Detach();
        Surface.Dispose();

        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }

        if (_population is not null)
        {
            _population.Dispose();
            _population = null;
        }

        if (_workspace is not null)
        {
            _workspace.Dispose();
            _workspace = null;
        }

        if (_assemblies is not null)
        {
            // Контекст сборок выгружается последним: пока живы объекты
            // документа, сборки проекта заняты и перестроить его нельзя.
            _assemblies.Dispose();
            _assemblies = null;
        }
    }

    private async Task<string?> StepHistoryAsync(bool undo, CancellationToken cancellationToken)
    {
        if (_workspace is null)
            return "Документ закрыт";

        if (!(undo ? _workspace.Undo() : _workspace.Redo()))
            return null;

        var workspace = _workspace;

        return await ApplyAsync(
            workspace.GetDocument(_documentId),
            undo ? workspace.Redo : workspace.Undo,
            cancellationToken);
    }

    /// <summary>
    /// Доводит правку до живых объектов: сначала регистрирует новый текст за
    /// классом документа, затем просит сессию догнать его.
    /// </summary>
    /// <param name="edited">Документ после правки.</param>
    /// <param name="stepBack">
    /// Чем откатить историю на шаг, если новый текст не удалось поднять вовсе.
    /// Для правки это отмена, для отмены — возврат, и наоборот.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <remarks>
    /// Догнать документ заплатками получается не всегда: движок обновлений
    /// перестраивает изменённый элемент, а вернуть перестроенный объект на
    /// место умеет не во всякой раскладке. Отказаться от правки в этом случае
    /// значило бы показывать на канве не то, что написано в документе, поэтому
    /// форма поднимается заново — дороже, но честно. Откат остаётся на случай,
    /// когда новый текст не грузится и заново: тогда возвращаемся к тому, что
    /// грузилось.
    /// </remarks>
    private async Task<string?> ApplyAsync(XamlDocument edited, Func<bool> stepBack, CancellationToken cancellationToken)
    {
        if (_session is null || _population is null || _workspace is null)
            return "Документ закрыт";

        await _population.SetDocumentAsync(edited, cancellationToken);

        var result = await _session.ApplyDocumentUpdateAsync(edited, cancellationToken);

        if (result.Outcome == XamlUpdateOutcome.Applied)
        {
            IsModified = true;
            Retarget(edited);
            return null;
        }

        if (await RecreateSessionAsync(edited, cancellationToken) is null)
        {
            IsModified = true;
            return null;
        }

        var error = Describe(result);

        if (!stepBack())
            return error;

        var restored = _workspace.GetDocument(_documentId);

        await _population.SetDocumentAsync(restored, cancellationToken);
        await RecreateSessionAsync(restored, cancellationToken);

        return error;
    }

    private async Task<string?> RecreateSessionAsync(XamlDocument document, CancellationToken cancellationToken)
    {
        Surface.Detach();

        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }

        var (session, result) = await XamlLoadSession.TryCreateAsync(
            document, _environment, _options, cancellationToken);

        if (session is null)
        {
            Nodes.Clear();
            Reloaded?.Invoke(this, EventArgs.Empty);
            return Describe(result);
        }

        _session = session;
        Surface.Attach(session);

        BuildTree(document.Root, session);
        Reloaded?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>
    /// Переводит дерево на новый разбор документа, не пересобирая его.
    /// </summary>
    /// <remarks>
    /// Пересборка стоила бы раскрытых узлов и выделения при каждой правке
    /// свойства, а структура при правке атрибута не меняется: узлы остаются на
    /// своих путях, меняются лишь элементы за ними.
    /// </remarks>
    private void Retarget(XamlDocument document)
    {
        var structural = false;

        foreach (var node in Flatten(Nodes))
        {
            if (node.Path?.Resolve(document) is { } element)
                node.Retarget(element, _session?.GetObject(element) as Control);
            else
                structural = true;
        }

        if (!structural)
            return;

        // Узел не нашёлся по своему пути: документ изменился структурно, и
        // дерево нужно строить заново.
        if (_session is { } session)
            BuildTree(document.Root, session);

        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    private string? Validate(HierarchyNode node, string name, string text)
    {
        if (_session is null || node.Control is not { } target)
            return null;

        // Расширение разметки проверяется загрузкой, а не преобразованием
        // текста: у {Binding} нет значения, пока нет источника.
        if (XamlValue.Parse(text) is XamlMarkupExtensionValue)
            return null;

        var member = _session.GetMember(target, name);

        if (!member.IsResolved)
            return $"У {node.TypeName} нет свойства {name}";

        return member.ConvertFromText(text) is { Succeeded: false } bad
            ? bad.Error ?? $"Значение не подходит свойству {name}"
            : null;
    }

    private void BuildTree(XamlElement? root, XamlLoadSession session)
    {
        Nodes.Clear();

        if (root is null)
            return;

        Nodes.Add(CreateNode(root, session));
    }

    private static HierarchyNode CreateNode(XamlElement element, XamlLoadSession session)
    {
        var node = new HierarchyNode(
            element,
            session.GetObject(element) as Control,
            XamlElementPath.Of(element));

        // ContentElements — только то, что порождает объекты: свойства-элементы
        // вроде <Border.Resources> в дереве контролов показывать незачем.
        foreach (var child in element.ContentElements)
            node.Children.Add(CreateNode(child, session));

        return node;
    }

    private static IEnumerable<HierarchyNode> Flatten(IEnumerable<HierarchyNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var descendant in Flatten(node.Children))
                yield return descendant;
        }
    }

    private static string Describe(XamlLoadResult result) =>
        result.Diagnostics.FirstOrDefault() is { } diagnostic
            ? diagnostic.ToString()
            : "Разметка не дала ни одного объекта";

    private static string Describe(XamlUpdateResult result) =>
        result.Diagnostics.FirstOrDefault() is { } diagnostic
            ? diagnostic.ToString()
            : "Правку не удалось применить к живым объектам";
}
