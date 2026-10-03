using System.Collections.Immutable;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Xaml;
using Avalonia.Threading;

namespace ArxisStudio.Modules.Xaml.Documents;

/// <summary>Аренда документа — то, что получает открывший.</summary>
/// <param name="entry">Документ.</param>
internal sealed class DocumentHandle(DocumentEntry entry) : IXamlDocumentHandle
{
    private bool _released;

    /// <summary>Показ, взятый через эту аренду; отпускается вместе с ней.</summary>
    internal DesignView? View { get; set; }

    /// <inheritdoc/>
    public CanonicalPath Path => entry.Path;

    /// <inheritdoc/>
    public XamlDocument Syntax => entry.Syntax;

    /// <inheritdoc/>
    public XamlDocumentState State => IsClosed ? XamlDocumentState.Detached : DocumentEntry.Map(entry.Live.State);

    /// <inheritdoc/>
    public ImmutableArray<MarkupDiagnostic> Diagnostics => entry.Live.Diagnostics;

    /// <inheritdoc/>
    public bool IsModified => entry.Live.IsDirty;

    /// <inheritdoc/>
    public bool HasConflict => !IsClosed && entry.PendingDiskText is not null;

    /// <inheritdoc/>
    public bool IsDeleted => entry.IsDeleted;

    /// <inheritdoc/>
    public bool IsClosed => _released || entry.IsClosed;

    /// <inheritdoc/>
    public bool CanUndo => !IsClosed && entry.Live.CanUndo;

    /// <inheritdoc/>
    public bool CanRedo => !IsClosed && entry.Live.CanRedo;

    /// <inheritdoc/>
    public string? UndoLabel => IsClosed ? null : entry.Live.UndoDescription;

    /// <inheritdoc/>
    public string? RedoLabel => IsClosed ? null : entry.Live.RedoDescription;

    /// <inheritdoc/>
    public event EventHandler<XamlDocumentChangesEventArgs>? Changed;

    /// <inheritdoc/>
    public event EventHandler<XamlExternalConflictEventArgs>? ExternalConflict;

    /// <inheritdoc/>
    public async Task<XamlEditOutcome> EditAsync(string label, Action<XamlDocumentEditor> edit, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(edit);
        ThrowIfClosed();

        return Outcome(await entry.Live.EditAsync(edit, label, cancellationToken));
    }

    /// <inheritdoc/>
    public async Task<XamlEditOutcome> UndoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();

        return Outcome(await entry.Live.UndoAsync(cancellationToken));
    }

    /// <inheritdoc/>
    public async Task<XamlEditOutcome> RedoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();

        return Outcome(await entry.Live.RedoAsync(cancellationToken));
    }

    /// <inheritdoc/>
    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ThrowIfClosed();

        return entry.SaveAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task ResolveConflictAsync(XamlConflictChoice choice, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ThrowIfClosed();

        return entry.ResolveAsync(choice, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IXamlDesignView> ShowAsync(IXamlRootLender? lender, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ThrowIfClosed();

        return entry.ShowAsync(this, lender, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Отпускается и после того, как служба документ закрыла или остановилась: держатель прощается
    /// своей дорогой и в своё время, а не тогда, когда это удобно службе.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_released)
            return;

        _released = true;

        await entry.ReleaseAsync(this);
    }

    /// <summary>Говорит подписчикам аренды, что документ сдвинулся.</summary>
    internal void Raise(XamlDocumentChanges changes)
    {
        if (!_released)
            entry.Owner.Raise(Changed, this, new XamlDocumentChangesEventArgs(changes));
    }

    /// <summary>Говорит подписчикам аренды о чужой записи поверх несохранённого.</summary>
    internal void RaiseConflict(string diskText)
    {
        if (!_released)
            entry.Owner.Raise(ExternalConflict, this, new XamlExternalConflictEventArgs(entry.Path, diskText));
    }

    private XamlEditOutcome Outcome(XamlLiveEditResult result) =>
        new(result.TextChanged, DocumentEntry.Map(result.State), result.Diagnostics);

    private void ThrowIfClosed()
    {
        if (IsClosed)
            throw new ObjectDisposedException(entry.Path.FileName, entry.Owner.Words.Closed(entry.Path.FileName));
    }
}
