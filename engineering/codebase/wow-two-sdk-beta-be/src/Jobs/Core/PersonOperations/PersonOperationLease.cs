namespace WoW.Two.Sdk.Beta.Be.Jobs.Core.PersonOperations;

/// <summary>
/// Provides a running operation's hold on the tracker: pass <see cref="CancellationToken"/> to the work and dispose the
/// lease when it ends, which stops signals from reaching it.
/// </summary>
public sealed class PersonOperationLease : IDisposable
{
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _source;
    private readonly Action<PersonOperationLease> _release;

    /// <summary>Keeps the reason a signal cancelled this lease; set before the token cancels, so a token callback reads it.</summary>
    private string? _cancelReason;

    /// <summary>Keeps whether a signal is running the token's callbacks; a dispose meanwhile leaves the source to that signal.</summary>
    private bool _cancelling;

    /// <summary>Keeps whether the lease was disposed; a signal no longer reaches it.</summary>
    private bool _disposed;

    /// <summary>Creates the lease, linked to the caller's token.</summary>
    /// <param name="declaration">The operation and its reactions.</param>
    /// <param name="release">Removes the lease from its tracker.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    internal PersonOperationLease(PersonOperationDeclaration declaration, Action<PersonOperationLease> release, CancellationToken cancellationToken)
    {
        Declaration = declaration;
        _source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken = _source.Token;
        _release = release;
    }

    /// <summary>Gets the operation and its reactions.</summary>
    public PersonOperationDeclaration Declaration { get; }

    /// <summary>Gets the token to pass to the work: cancelled by a signal or by the caller's token.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets why a signal cancelled the lease (<see cref="PersonOperationCancelReasonConstants"/>); null when none did,
    /// including when the caller's token cancelled it first.
    /// </summary>
    public string? CancelReason
    {
        get
        {
            lock (_lock)
                return _cancelReason;
        }
    }

    /// <summary>Removes the lease from its tracker; the token keeps its state.</summary>
    public void Dispose()
    {
        bool disposeSource;
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            disposeSource = !_cancelling;
        }

        _release(this);
        if (disposeSource)
            _source.Dispose();
    }

    /// <summary>Cancels the lease for a signal, unless it is disposed or already cancelled. Callbacks run outside the lock.</summary>
    /// <param name="reason">The signal's reason.</param>
    /// <returns><see langword="true"/> when this call cancelled the lease.</returns>
    /// <exception cref="AggregateException">A token callback threw; the lease is cancelled regardless.</exception>
    internal bool TryCancel(string reason)
    {
        lock (_lock)
        {
            if (_disposed || _source.IsCancellationRequested)
                return false;
            _cancelReason = reason;
            _cancelling = true;
        }

        try
        {
            _source.Cancel();
        }
        finally
        {
            bool disposeSource;
            lock (_lock)
            {
                _cancelling = false;
                disposeSource = _disposed;
            }

            if (disposeSource)
                _source.Dispose();
        }

        return true;
    }
}
