using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Modules.Xaml.Session;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.Xaml.Documents;

/// <summary>
/// Открытый документ сессии: живой документ хоста, его аренды, показ и вопрос о чужой записи.
/// </summary>
/// <remarks>
/// <para>
/// Живой документ (<see cref="XamlLiveDocument"/>) один на файл и держит текст, историю и сессию
/// загрузки; запись о нём здесь — то, что у документа своё в студии: кто его арендовал, кто показывает
/// и о чём человека надо спросить.
/// </para>
/// <para>Всё — в потоке интерфейса: живой документ говорит там же.</para>
/// </remarks>
internal sealed class DocumentEntry
{
    private readonly XamlDesignSession _session;
    private readonly List<DocumentHandle> _handles = [];

    // Текст на миг закрытия: аренда, пережившая документ, отвечает им, а не обращением к отпущенному.
    private XamlDocument? _last;

    public DocumentEntry(XamlDesignSession session, CanonicalPath path, XamlLiveDocument live, RootLending lending)
    {
        _session = session;
        Path = path;
        Live = live;
        Lending = lending;

        live.Changed += OnLiveChanged;
        live.SessionReplaced += OnSessionReplaced;
    }

    /// <summary>Файл документа; переносят — запись идёт за ним.</summary>
    public CanonicalPath Path { get; private set; }

    /// <summary>Живой документ хоста.</summary>
    public XamlLiveDocument Live { get; }

    /// <summary>Возврат корня на время записи.</summary>
    public RootLending Lending { get; }

    /// <summary>Сессия.</summary>
    public XamlDesignSession Session => _session;

    /// <summary>Служба.</summary>
    public XamlService Owner => _session.Owner;

    /// <summary>Показ; null — документ не показан.</summary>
    public DesignView? View { get; private set; }

    /// <summary>Что в файле после чужой записи поверх несохранённого; null — вопроса нет.</summary>
    public string? PendingDiskText { get; private set; }

    /// <summary>Файл удалён.</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>Документ закрыт службой: решение кончилось.</summary>
    public bool IsClosed { get; private set; }

    /// <summary>Аренды.</summary>
    public int Leases => _handles.Count;

    /// <summary>Текст документа, а после закрытия — последний, какой у него был.</summary>
    public XamlDocument Syntax => _last ?? Live.Document;

    /// <summary>Новая аренда.</summary>
    public DocumentHandle Lease()
    {
        var handle = new DocumentHandle(this);

        _handles.Add(handle);
        Owner.CancelIdle();

        return handle;
    }

    /// <summary>Аренда отпущена; последняя закрывает документ.</summary>
    /// <param name="handle">Аренда.</param>
    public async Task ReleaseAsync(DocumentHandle handle)
    {
        if (!_handles.Remove(handle))
            return;

        handle.View?.Dispose();

        if (_handles.Count == 0 && !IsClosed)
            await _session.CloseAsync(this);
    }

    /// <summary>Пишет несохранённое: хост — службой файлов, со сверкой ожидаемого.</summary>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!Live.IsDirty)
            return;

        await _session.Host.SaveAsync(Live, cancellationToken);

        // Оставленные правки записаны поверх файла: спрашивать больше не о чем.
        if (PendingDiskText is not null)
        {
            PendingDiskText = null;
            Raise(XamlDocumentChanges.Conflict);
        }
    }

    /// <summary>Отвечает на вопрос о чужой записи.</summary>
    /// <param name="choice">Ответ.</param>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    /// <remarks>
    /// Текст из файла берётся в кодировке сохранённого: так он прочитан, так и запишется, и сверка
    /// следующего сохранения сойдётся с диском.
    /// </remarks>
    public async Task ResolveAsync(XamlConflictChoice choice, CancellationToken cancellationToken)
    {
        if (PendingDiskText is not { } disk)
            return;

        var saved = Live.SavedText;
        var policy = choice == XamlConflictChoice.TakeTheirs ? XamlExternalTextPolicy.TakeTheirs : XamlExternalTextPolicy.KeepMine;

        await Live.AcceptExternalTextAsync(
            SourceText.From(disk, saved.Encoding, saved.HasByteOrderMark),
            Owner.Words.ChangedOutside,
            policy,
            cancellationToken);

        if (ReferenceEquals(PendingDiskText, disk))
        {
            PendingDiskText = null;
            Raise(XamlDocumentChanges.Conflict);
        }
    }

    /// <summary>Показывает документ: один показ на документ.</summary>
    /// <param name="handle">Аренда, через которую показывают.</param>
    /// <param name="lender">Кто одалживает части корня.</param>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    public async Task<IXamlDesignView> ShowAsync(DocumentHandle handle, IXamlRootLender? lender, CancellationToken cancellationToken)
    {
        if (View is not null)
            throw new InvalidOperationException(Owner.Words.AlreadyShown(Path.FileName));

        var view = new DesignView(this, handle);

        View = view;
        handle.View = view;
        Lending.Lender = lender;
        _session.UpdateVisible();

        try
        {
            // Документ, который никто не показывал, после замены поколения стоит откреплённым: показ
            // прикрепляет его и пересобирает то, что сменилось, пока на него не смотрели.
            await _session.Host.EnsureLiveAsync(Live, cancellationToken);
        }
        catch
        {
            view.Dispose();
            throw;
        }

        view.Follow(Live.Session);
        _ = view.OpenApplicationAsync();

        return view;
    }

    /// <summary>Показ отпущен.</summary>
    /// <param name="view">Показ.</param>
    public void ViewDisposed(DesignView view)
    {
        if (!ReferenceEquals(View, view))
            return;

        View = null;
        Lending.Lender = null;
        _session.UpdateVisible();
    }

    /// <summary>Файл переписали мимо документа поверх несохранённого.</summary>
    /// <param name="diskText">Что в файле теперь.</param>
    public void Conflict(string diskText)
    {
        PendingDiskText = diskText;

        foreach (var handle in _handles.ToArray())
            handle.RaiseConflict(diskText);

        Raise(XamlDocumentChanges.Conflict);
    }

    /// <summary>Файл перенесли; документ идёт за ним.</summary>
    /// <param name="path">Новое место.</param>
    public void Moved(CanonicalPath path)
    {
        Path = path;
        Raise(XamlDocumentChanges.Path);
    }

    /// <summary>Файл удалили.</summary>
    public void Deleted()
    {
        IsDeleted = true;
        Raise(XamlDocumentChanges.Deleted);
    }

    /// <summary>
    /// Пачка перемен разобрана: вопрос о файле, который потом вернули как был, больше ни о чём.
    /// </summary>
    public void Settle()
    {
        if (PendingDiskText is not null && !Live.IsDirty)
        {
            PendingDiskText = null;
            Raise(XamlDocumentChanges.Conflict);
        }
    }

    /// <summary>Перестаёт слушать живой документ и отпускает показ.</summary>
    public void Detach()
    {
        Live.Changed -= OnLiveChanged;
        Live.SessionReplaced -= OnSessionReplaced;

        View?.Dispose();
    }

    /// <summary>Служба закрывает документ: аренды узнают об этом и отвечают последним состоянием.</summary>
    public void Close()
    {
        if (IsClosed)
            return;

        _last = Live.Document;
        IsClosed = true;
        Detach();
        Raise(XamlDocumentChanges.Closed);
    }

    /// <summary>Что сдвинулось у живого документа — словами контракта.</summary>
    /// <param name="changes">Перемены хоста.</param>
    internal static XamlDocumentChanges Map(XamlLiveDocumentChanges changes)
    {
        var mapped = XamlDocumentChanges.None;

        if (changes.HasFlag(XamlLiveDocumentChanges.Text))
            mapped |= XamlDocumentChanges.Text;

        if (changes.HasFlag(XamlLiveDocumentChanges.Saved))
            mapped |= XamlDocumentChanges.Saved;

        if (changes.HasFlag(XamlLiveDocumentChanges.State))
            mapped |= XamlDocumentChanges.State;

        if (changes.HasFlag(XamlLiveDocumentChanges.Objects))
            mapped |= XamlDocumentChanges.Objects;

        return mapped;
    }

    /// <summary>Состояние живого документа словами контракта.</summary>
    /// <param name="state">Состояние хоста.</param>
    internal static XamlDocumentState Map(XamlLiveDocumentState state) => state switch
    {
        XamlLiveDocumentState.Live => XamlDocumentState.Live,
        XamlLiveDocumentState.Behind => XamlDocumentState.Behind,
        XamlLiveDocumentState.Broken => XamlDocumentState.Broken,
        _ => XamlDocumentState.Detached,
    };

    private void Raise(XamlDocumentChanges changes)
    {
        foreach (var handle in _handles.ToArray())
            handle.Raise(changes);
    }

    private void OnLiveChanged(object? sender, XamlLiveDocumentChangedEventArgs e)
    {
        // Правка, дошедшая далеко, строит корень заново, не меняя сессии.
        View?.Follow(Live.Session);

        if (Map(e.Changes) is var changes and not XamlDocumentChanges.None)
            Raise(changes);
    }

    /// <summary>
    /// Сессию сменили: прежний корень снимают, пока она ещё открыта, — после обработчиков её закроют.
    /// </summary>
    private void OnSessionReplaced(object? sender, XamlSessionReplacedEventArgs e) => View?.Follow(e.Current);
}
