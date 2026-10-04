using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArxisStudio.Modules.Xaml.Documents;

/// <summary>Показ документа: корень, приложение формы и дорога между объектами и путями.</summary>
/// <remarks>
/// <para>
/// <b>Переживает замены поколения.</b> На замену показ отдаёт корень и приложение после участников
/// (<see cref="LetGo"/>) и берёт новые раньше них (<see cref="TakeUp"/>); пока отдал — объектов
/// поколения у него нет, и ни <see cref="ObjectAt"/>, ни <see cref="PathOf"/> их не трогают.
/// </para>
/// <para>
/// <b>Приложение формы</b> берётся у хоста его очередью, поэтому не внутри замены — хост держит её до
/// конца участников, — а после неё. Ответ, пришедший к уже отпущенному или заново запрошенному показу,
/// закрывается, а не ставится.
/// </para>
/// </remarks>
internal sealed class DesignView : IXamlDesignView
{
    private readonly DocumentEntry _entry;
    private readonly DocumentHandle _handle;

    private Control? _root;
    private ProjectDesignApplication? _application;
    private int _applicationTurn;
    private bool _released;
    private bool _disposed;

    public DesignView(DocumentEntry entry, DocumentHandle handle)
    {
        _entry = entry;
        _handle = handle;
    }

    /// <inheritdoc/>
    public IXamlDocumentHandle Document => _handle;

    /// <inheritdoc/>
    public Control? Root => _root;

    /// <inheritdoc/>
    public event EventHandler? RootChanged;

    /// <inheritdoc/>
    public Application? Application => _application?.Application;

    /// <inheritdoc/>
    public CanonicalPath ApplicationFile => _application?.File ?? default;

    /// <inheritdoc/>
    public event EventHandler? ApplicationChanged;

    /// <inheritdoc/>
    public XamlElementPath? PathOf(object live)
    {
        ArgumentNullException.ThrowIfNull(live);

        if (Shown() is not { } session)
            return null;

        return session.Objects.GetElement(live) is { } element ? XamlElementPath.Of(element) : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Путь ищется в тексте, который показан, а не в нынешнем: у документа, отставшего от текста
    /// (<see cref="XamlDocumentState.Behind"/>), объекты построены прежним, и элементы у них — его.
    /// </remarks>
    public object? ObjectAt(XamlElementPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (Shown() is not { } session)
            return null;

        return path.Resolve(session.Document) is { } element ? session.Objects.GetObject(element) : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Проверка — в обе стороны: у объекта есть элемент, и элемент ведёт к нему же. Метку внутри
    /// кнопки её шаблон построил без элемента, а объект, приписанный к элементу владельца, обратно к
    /// себе не приведёт; объявленное документом проходит обе половины.
    /// </remarks>
    public IReadOnlyList<object> GetDeclaredObjects()
    {
        if (Shown() is not { } session)
            return [];

        var map = session.Objects;

        return
        [
            .. map.Objects.Where(produced =>
                !ReferenceEquals(produced, session.RootObject)
                && map.GetElement(produced) is { } element
                && ReferenceEquals(map.GetObject(element), produced)),
        ];
    }

    /// <inheritdoc/>
    /// <remarks>Событий не поднимает: тот, кто отпускает показ, их уже не ждёт.</remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _root = null;
        _applicationTurn++;

        if (_application is { } application)
        {
            _application = null;
            _ = CloseAsync(application);
        }

        _entry.ViewDisposed(this);
    }

    /// <summary>Ставит корень сессии, если он сменился.</summary>
    /// <param name="session">Сессия документа сейчас; null — её нет.</param>
    internal void Follow(XamlLoadSession? session)
    {
        var root = _released || _disposed ? null : session?.RootObject as Control;

        if (ReferenceEquals(root, _root))
            return;

        _root = root;
        _entry.Owner.Raise(RootChanged, this);
    }

    /// <summary>Замена: отдаёт корень и приложение.</summary>
    /// <remarks>
    /// Приложение не закрывается здесь: хост закроет все открытые сразу после участников, а ждать его
    /// внутри замены нельзя.
    /// </remarks>
    internal void LetGo()
    {
        _released = true;
        Follow(null);

        _applicationTurn++;

        if (_application is not null)
        {
            _application = null;
            _entry.Owner.Raise(ApplicationChanged, this);
        }
    }

    /// <summary>Замена кончилась: берёт новый корень, а приложение — когда хост освободит очередь.</summary>
    internal void TakeUp()
    {
        _released = false;
        Follow(_entry.Live.Session);

        Dispatcher.UIThread.Post(() => _ = OpenApplicationAsync());
    }

    /// <summary>Файл приложения сохранили: приложение формы строится заново.</summary>
    /// <param name="file">Файл приложения.</param>
    internal void ApplicationSaved(CanonicalPath file)
    {
        if (_application is { } application && application.File == file)
            _ = OpenApplicationAsync();
    }

    /// <summary>Берёт приложение формы у хоста и ставит его вместо прежнего.</summary>
    internal async Task OpenApplicationAsync()
    {
        if (_released || _disposed)
            return;

        var turn = ++_applicationTurn;
        ProjectDesignApplication? opened;

        try
        {
            opened = await _entry.Session.Host.OpenApplicationAsync(_entry.Live, _entry.Session.Lifetime);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or ArgumentException)
        {
            // Сессия кончилась или документ закрыли, пока ждали: ставить некуда.
            return;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _entry.Owner.Log(StudioLogLevel.Warning, $"{_entry.Path.FileName}: приложение формы не построилось — {e.Message}");
            return;
        }

        if (turn != _applicationTurn || _released || _disposed)
        {
            if (opened is not null)
                await CloseAsync(opened);

            return;
        }

        var previous = _application;

        _application = opened;

        foreach (var diagnostic in opened?.Diagnostics.Where(diagnostic => diagnostic.IsError) ?? [])
            _entry.Owner.Log(StudioLogLevel.Warning, $"{opened!.File.FileName}: {diagnostic.Message}");

        if (!ReferenceEquals(previous, opened))
            _entry.Owner.Raise(ApplicationChanged, this);

        if (previous is not null)
            await CloseAsync(previous);
    }

    /// <summary>Объекты показа сейчас; null — показ отдал их или отпущен.</summary>
    private XamlLoadSession? Shown() => _released || _disposed ? null : _entry.Live.Session;

    private async Task CloseAsync(ProjectDesignApplication application)
    {
        try
        {
            await application.DisposeAsync();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _entry.Owner.Log(StudioLogLevel.Warning, $"{application.File.FileName}: приложение формы закрылось со сбоем — {e.Message}");
        }
    }
}
