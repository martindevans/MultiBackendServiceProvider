namespace MultiBackendServiceProvider;

public sealed class Backend<TBackend>
{
    private readonly SemaphoreSlim _semaphore;
    private readonly IBackendHealthChecker _healthChecker;

    /// <summary>
    /// Get the backend object
    /// </summary>
    public TBackend Value { get; }

    /// <summary>
    /// Number of slots available for use
    /// </summary>
    public int AvailableSlots => _semaphore.CurrentCount;

    /// <summary>
    /// Number of slots available for use
    /// </summary>
    public int TotalSlots { get; }

    /// <summary>
    /// Create a new backend
    /// </summary>
    /// <param name="value"></param>
    /// <param name="concurrentAccess"></param>
    /// <param name="healthChecker"></param>
    public Backend(TBackend value, int concurrentAccess, IBackendHealthChecker healthChecker)
    {
        _semaphore = new(concurrentAccess);

        Value = value;
        _healthChecker = healthChecker;
        TotalSlots = concurrentAccess;
    }

    /// <summary>
    /// Wait to acquire a slot from this backend
    /// </summary>
    /// <param name="timeout"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    internal async Task<IScope?> Acquire(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var acquired = await _semaphore.WaitAsync(timeout, cancellationToken);
        if (!acquired)
            return null;

        return new Scope(this);
    }

    /// <summary>
    /// Release a slot to this backend
    /// </summary>
    private void Release()
    {
        _semaphore.Release();
    }

    public Task<bool> CheckHealth(CancellationToken cancellation)
    {
        return _healthChecker.CheckHealth(cancellation).AsTask();
    }

    #region scope
    /// <summary>
    /// A scope of backend usage, while this is held a slot is consumed on the backend
    /// </summary>
    public interface IScope
        : IDisposable
    {
        /// <summary>
        /// Get the backend associated with this scope
        /// </summary>
        public TBackend Backend { get; }
    }

    /// <summary>
    /// A scope of backend usage, while this is held a slot is consumed on the backend
    /// </summary>
    private sealed class Scope
        : IScope
    {
        private readonly Backend<TBackend> _backend;
        private int _released;

        /// <summary>
        /// Get the backend associated with this scope
        /// </summary>
        public TBackend Backend => _backend.Value;

        /// <summary>
        /// Create a new scope. <b>Must acquire a semaphore slot **before** calling this!</b>
        /// </summary>
        /// <param name="backend"></param>
        internal Scope(Backend<TBackend> backend)
        {
            _backend = backend;
            _released = 0;
        }

        ~Scope()
        {
            Release();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Release();
            GC.SuppressFinalize(this);
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _backend.Release();
        }
    }
    #endregion
}

public static class BackendExtensions
{
    /// <summary>
    /// Wait to acquire a slot from a backend. If the backend is null, returns a null scope.
    /// </summary>
    /// <param name="backend">The backend</param>
    /// <param name="timeout">Maximum time to wait for a slot</param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    public static async Task<Backend<TBackend>.IScope?> Acquire<TBackend>(this Backend<TBackend>? backend, TimeSpan timeout, CancellationToken cancellation)
    {
        if (backend is null)
            return null;

        return await backend.Acquire(timeout, cancellation);
    }
}