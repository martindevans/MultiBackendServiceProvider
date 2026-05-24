using Microsoft.Extensions.Logging;

namespace MultiBackendServiceProvider;

public sealed class BackendState<TBackend>
{
    private readonly SemaphoreSlim _semaphore;
    private readonly IBackendHealthChecker _healthChecker;

    /// <summary>
    /// Get the backend object
    /// </summary>
    public TBackend Backend { get; }

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
    /// <param name="backend"></param>
    /// <param name="concurrentAccess"></param>
    /// <param name="healthChecker"></param>
    public BackendState(TBackend backend, int concurrentAccess, IBackendHealthChecker healthChecker)
    {
        _semaphore = new(concurrentAccess);

        Backend = backend;
        _healthChecker = healthChecker;
        TotalSlots = concurrentAccess;
    }

    /// <summary>
    /// Wait to acquire a slot from this backend
    /// </summary>
    /// <param name="timeout"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<IScope?> Acquire(TimeSpan timeout, CancellationToken cancellationToken)
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

    public Task<bool> CheckHealth(HttpClient http, ILogger logger, CancellationToken cancellation)
    {
        return _healthChecker.CheckHealth(http, logger, cancellation).AsTask();
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
        private readonly BackendState<TBackend> _backend;
        private int _released;

        /// <summary>
        /// Get the backend associated with this scope
        /// </summary>
        public TBackend Backend => _backend.Backend;

        /// <summary>
        /// Create a new scope. <b>Must acquire a semaphore slot **before** calling this!</b>
        /// </summary>
        /// <param name="backend"></param>
        internal Scope(BackendState<TBackend> backend)
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