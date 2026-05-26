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
    private readonly IReadOnlyList<Backend<TBackend>> _backends;

    /// <summary>
    /// Create a new provider
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="filter"></param>
    /// <param name="selector"></param>
    /// <param name="backends">Backends, in order of preference</param>
    public MultiBackendServiceProvider(ILogger logger, IBackendFilter<TBackend> filter, IBackendSelector<TBackend> selector, params BackendConfig[] backends)
    {
        _logger = logger;
        _filter = filter;
        _selector = selector;
        _backends = backends.Select(a => new Backend<TBackend>(a.Backend, a.Slots, a.HealthChecker)).ToArray();
    }

    #region GetBackend
    /// <summary>
    /// Get an available backend which is healthy and has slots.
    /// </summary>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    public Task<Backend<TBackend>.IScope?> Acquire(CancellationToken cancellation)
    {
        return Acquire([], cancellation);
    }

    /// <summary>
    /// Get an available backend which is healthy and has an available slot. Filters out backends based on provided string tags.
    /// </summary>
    /// <param name="tags">Strings that will be passed into backend filters</param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    public async Task<Backend<TBackend>.IScope?> Acquire(IReadOnlyCollection<string> tags, CancellationToken cancellation)
    {
        // If none are available give up
        if (_backends.Count == 0)
            return null;

        cancellation.ThrowIfCancellationRequested();

        // Backends that have been filtered out. It's assumed this won't change so we
        // cache the filter result.
        var filterCache = new HashSet<Backend<TBackend>>();
        
        // Get valid backends (healthy + filtered)
        var backends = await GetHealthyBackends(cancellation);
        await FilterBackends(backends, _filter, tags, filterCache, cancellation);

        // If none are available give up
        if (backends.Count == 0)
            return null;

        // Try to select a backend from this set
        while (backends.Count > 0)
        {
            // Choose one; if none are selected, give up entirely
            var result = await _selector.Select(backends, cancellation);
            if (result == null)
                return null;

            // Check that it's still healthy, if not remove it from the set and retry
            var health = await result.CheckHealth(cancellation);
            if (!health)
            {
                backends.Remove(result);
                continue;
            }

            // Try to acquire a scope
            var scope = await result.Acquire(TimeSpan.FromMilliseconds(50), cancellation);
            if (scope != null)
                return scope;

            // Backend is busy! Remove it.
            backends.Remove(result);
        }
        
        // We only get here if:
        // - Some backends were healthy
        // - They were not filtered
        // - One was selected but it was unhealthy or had no slots
        //   - This applied to all backends that were initially chosen
        return null;
    }

    /// <summary>
    /// Get a list of all backends which succeed the health check
    /// </summary>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    private async Task<List<Backend<TBackend>>> GetHealthyBackends(CancellationToken cancellation)
    {
        // Start a simultaneous health check on every backend
        var pending = (
            from backend in _backends
            let task = backend.CheckHealth(cancellation)
            select (backend, task)
        ).ToList();

        // Find valid+live backends
        var live = new List<Backend<TBackend>>();
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
    /// <param name="excluded"></param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    private static async Task FilterBackends(List<Backend<TBackend>> backends, IBackendFilter<TBackend> filter, IReadOnlyCollection<string> tags, HashSet<Backend<TBackend>> excluded, CancellationToken cancellation)
    {
        // Remove all backends that were previously filtered out
        backends.RemoveAll(excluded.Contains);
        
        // Start a simultaneous filter check on every backend
        var pending = (
            from backend in backends
#pragma warning disable CA2012 // It's ok to store a ValueTask here, we're only going to await it once
            let task = filter.Filter(backend.Value, tags)
#pragma warning restore CA2012
            select task
        ).ToList();

        // Filter out, working backwards so indices remain valid
        for (var i = backends.Count - 1; i >= 0; i--)
        {
            cancellation.ThrowIfCancellationRequested();

            var ok = await pending[i];
            if (!ok)
            {
                excluded.Add(backends[i]);
                backends.RemoveAt(i);
            }
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
        /// Configuration for a backend
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
                result = await backend.CheckHealth(cancellation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check exception");
                result = false;
            }

            timer.Stop();
            return new Status(backend.Value, backend.AvailableSlots, backend.TotalSlots, result, timer.Elapsed);
        }).ToList();

        return await Task.WhenAll(pending);
    }

    /// <summary>
    /// Status info for a backend
    /// </summary>
    /// <param name="Backend"></param>
    /// <param name="AvailableSlots"></param>
    /// <param name="TotalSlots"></param>
    /// <param name="IsHealthy"></param>
    /// <param name="Latency"></param>
    public record Status(TBackend Backend, int AvailableSlots, int TotalSlots, bool IsHealthy, TimeSpan Latency);
    #endregion
}
