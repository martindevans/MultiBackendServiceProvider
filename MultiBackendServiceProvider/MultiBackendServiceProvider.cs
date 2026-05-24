using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MultiBackendServiceProvider;

/// <summary>
/// Provide multiple API backends for fault tolerance and load balancing. When acquiring a backend a "slot" is booked for the
/// duration the backend is in use, so slot limits can be set per backend.
/// </summary>
public sealed class MultiBackendServiceProvider<TBackend>
{
    private readonly ILogger _logger;
    private readonly IBackendFilter<TBackend> _filter;
    private readonly IBackendSelector<TBackend> _selector;
    private readonly IReadOnlyList<BackendState<TBackend>> _backends;
    private readonly HttpClient _healthCheckClient;

    /// <summary>
    /// Create a new provider
    /// </summary>
    /// <param name="http"></param>
    /// <param name="logger"></param>
    /// <param name="filter"></param>
    /// <param name="selector"></param>
    /// <param name="backends">Backends, in order of preference</param>
    public MultiBackendServiceProvider(IHttpClientFactory http, ILogger logger, IBackendFilter<TBackend> filter, IBackendSelector<TBackend> selector, params BackendConfig[] backends)
    {
        _logger = logger;
        _filter = filter;
        _selector = selector;
        _backends = backends.Select(a => new BackendState<TBackend>(a.Backend, a.Slots, a.HealthChecker)).ToArray();

        // Create a client with a short timeout, for health checks
        _healthCheckClient = http.CreateClient();
        _healthCheckClient.Timeout = TimeSpan.FromSeconds(0.5f);
    }

    #region GetBackend
    /// <summary>
    /// Get an available backend which is healthy and take an available slot.
    /// </summary>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    public Task<IScope?> GetBackend(CancellationToken cancellation)
    {
        return GetBackend([], cancellation);
    }

    /// <summary>
    /// Get an available backend which is healthy and take an available slot. Filters out backends based on provided string tags.
    /// </summary>
    /// <param name="tags">Strings that will be passed into backend filters</param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    public async Task<IScope?> GetBackend(IReadOnlyCollection<string> tags, CancellationToken cancellation)
    {
        if (_backends.Count == 0)
            return null;
        
        // Try to acquire a slot
        while (!cancellation.IsCancellationRequested)
        {
            // Get valid backends (healthy + filtered)
            var backends = await GetHealthyBackends(cancellation);
            await FilterHealthyBackends(backends, _filter, tags, cancellation);

            // If none are available give up
            if (backends.Count == 0)
                return null;
            
            // Choose one
            var result = await _selector.Select(backends, cancellation);
            if (result == null)
                return null;

            // Check that it's still healthy, if not retry the whole thing
            var health = await result.CheckHealth(_healthCheckClient, _logger, cancellation);
            if (!health)
                continue;

            // Try to acquire a slot
            var scope = await Scope.TryCreate(result, TimeSpan.FromSeconds(0.1f), cancellation);
            if (scope != null)
                return scope;
        }
        
        // Fail :(
        cancellation.ThrowIfCancellationRequested();
        return null;
    }

    /// <summary>
    /// Get a list of all backends which succeed the health check
    /// </summary>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    private async Task<List<BackendState<TBackend>>> GetHealthyBackends(CancellationToken cancellation)
    {
        // Start a simultaneous health check on every backend
        var pending = (
            from backend in _backends
            let task = backend.CheckHealth(_healthCheckClient, _logger, cancellation)
            select (backend, task)
        ).ToList();

        // Find valid+live backends
        var live = new List<BackendState<TBackend>>();
        foreach (var (backend, task) in pending)
        {
            // Ignore items that fail the health check
            var response = await task;
            if (!response)
                continue;

            // Backend is alive!
            live.Add(backend);
        }

        return live;
    }

    /// <summary>
    /// Filter out backends based on tags
    /// </summary>
    /// <param name="backends"></param>
    /// <param name="filter"></param>
    /// <param name="tags"></param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    private static async Task FilterHealthyBackends(List<BackendState<TBackend>> backends, IBackendFilter<TBackend> filter, IReadOnlyCollection<string> tags, CancellationToken cancellation)
    {
        // Start a simultaneous filter check on every backend
        var pending = (
            from backend in backends
#pragma warning disable CA2012 // It's ok to store a ValueTask here, we're only going to await it once
            let task = filter.Filter(backend.Backend, tags)
#pragma warning restore CA2012
            select task
        ).ToList();

        // Filter out, working backwards so indices remain valid
        for (var i = backends.Count - 1; i >= 0; i--)
        {
            cancellation.ThrowIfCancellationRequested();

            var ok = await pending[i];
            if (!ok)
                backends.RemoveAt(i);
        }
    }
    #endregion

    /// <summary>
    /// Configuration for a backend
    /// </summary>
    public sealed record BackendConfig
    {
        /// <summary>
        /// Gets the backend associated with this configuration.
        /// </summary>
        public TBackend Backend { get; init; }

        /// <summary>
        /// Gets the maximum number of concurrent accesses allowed for the backend.
        /// </summary>
        /// <remarks>
        /// This property defines the total number of "slots" available for concurrent usage of the backend.
        /// It is used to limit the number of simultaneous operations that can be performed on the backend.
        /// </remarks>
        public int Slots { get; init; }

        /// <summary>
        /// Gets the health checker instance responsible for monitoring the health of the backend.
        /// </summary>
        /// <value>
        /// An implementation of <see cref="IBackendHealthChecker"/> used to determine the availability of the backend.
        /// </value>
        public IBackendHealthChecker HealthChecker { get; init; }

        /// <summary>
        /// Create backend config using default HTTP health checker.
        /// </summary>
        /// <param name="Backend"></param>
        /// <param name="Slots"></param>
        /// <param name="HealthCheck"></param>
        public BackendConfig(TBackend Backend, int Slots, Uri HealthCheck)
            : this(Backend, Slots, new HttpHealthChecker(HealthCheck))
        {
        }

        /// <summary>
        /// Configuration for an backend
        /// </summary>
        /// <param name="backend"></param>
        /// <param name="slots"></param>
        /// <param name="healthChecker"></param>
        public BackendConfig(TBackend backend, int slots, IBackendHealthChecker healthChecker)
        {
            Backend = backend;
            Slots = slots;
            HealthChecker = healthChecker;
        }
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
        private Scope(BackendState<TBackend> backend)
        {
            _backend = backend;
            _released = 0;
        }

        public static async ValueTask<Scope?> TryCreate(BackendState<TBackend> backend, TimeSpan timeout, CancellationToken cancellation)
        {
            var acquired = await backend.Wait(timeout, cancellation);
            if (!acquired)
                return null;

            return new Scope(backend);
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

    #region status
    /// <summary>
    /// Get the status of all backends
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public async Task<Status[]> GetStatus(CancellationToken cancellation)
    {
        var pending = _backends.Select(async backend =>
        {
            var timer = Stopwatch.StartNew();

            bool result;
            try
            {
                result = await backend.CheckHealth(_healthCheckClient, _logger, cancellation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check exception");
                result = false;
            }

            timer.Stop();
            return new Status(backend.Backend, backend.AvailableSlots, backend.TotalSlots, result, timer.Elapsed);
        }).ToList();

        return await Task.WhenAll(pending);
    }

    /// <summary>
    /// Status info for a backend
    /// </summary>
    /// <param name="Backend"></param>
    /// <param name="AvailableSlots"></param>
    /// <param name="MaxSlots"></param>
    /// <param name="IsHealthy"></param>
    /// <param name="Latency"></param>
    public record Status(TBackend Backend, int AvailableSlots, int MaxSlots, bool IsHealthy, TimeSpan Latency);
    #endregion
}
