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
    /// Wait for this backend to become available
    /// </summary>
    /// <param name="timeout"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    internal Task<bool> Wait(TimeSpan timeout, CancellationToken cancellationToken)
    {
        return _semaphore.WaitAsync(timeout, cancellationToken);
    }

    /// <summary>
    /// Release a slot to this backend
    /// </summary>
    internal void Release()
    {
        _semaphore.Release();
    }

    public async Task<bool> CheckHealth(HttpClient http, ILogger logger, CancellationToken cancellation)
    {
        return await _healthChecker.CheckHealth(http, logger, cancellation);
    }
}