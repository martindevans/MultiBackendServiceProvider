using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MultiBackendServiceProvider;

/// <summary>
/// Provide multiple API endpoints for fault tolerance and load balancing. When acquiring a backend a "slot" is booked for the
/// duration the backend is in use, so slot limits can be set per backend.
/// </summary>
public sealed class MultiBackendServiceProvider<TEndpoint>
{
    private readonly ILogger _logger;
    private readonly IEndpointFilter<TEndpoint> _filter;
    private readonly IReadOnlyList<Backend> _backends;
    private readonly HttpClient _healthCheckClient;

    /// <summary>
    /// Create a new provider
    /// </summary>
    /// <param name="http"></param>
    /// <param name="logger"></param>
    /// <param name="filter"></param>
    /// <param name="endpoints">Endpoints, in order of preference</param>
    public MultiBackendServiceProvider(IHttpClientFactory http, ILogger logger, IEndpointFilter<TEndpoint> filter, params EndpointConfig[] endpoints)
    {
        _logger = logger;
        _filter = filter;
        _backends = endpoints.Select(a => new Backend(a.Endpoint, a.Slots, a.HealthChecker)).ToArray();

        // Create a client with a short timeout, for health checks
        _healthCheckClient = http.CreateClient();
        _healthCheckClient.Timeout = TimeSpan.FromSeconds(0.5f);
    }

    /// <summary>
    /// Filter endpoints based on string filters.
    /// </summary>
    /// <param name="endpoint"></param>
    /// <param name="filters"></param>
    /// <returns>true, to allow endpoint.</returns>
    private ValueTask<bool> FilterEndpoint(TEndpoint endpoint, IReadOnlyList<string> filters)
    {
        return _filter.FilterEndpoint(endpoint, filters);
    }

    /// <summary>
    /// Get an available endpoint which is healthy and take an available slot.
    /// </summary>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    public Task<IScope?> GetEndpoint(CancellationToken cancellation)
    {
        return GetEndpoint([], cancellation);
    }

    /// <summary>
    /// Get an available endpoint which is healthy and take an available slot. Filters out endpoints based on provided strings.
    /// </summary>
    /// <param name="filters">Strings that will be passed into endpoint filters</param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    public async Task<IScope?> GetEndpoint(IReadOnlyList<string> filters, CancellationToken cancellation)
    {
        // Start a health check on every backend
        var pending = (from backend in _backends
                       let task = backend.CheckHealth(_healthCheckClient, _logger, cancellation)
                       select (backend, task)).ToList();

        // Live backends
        var live = new List<Backend>();

        // Check all backends and add them to the live list. The first healthy backend in the
        // list that has capacity will be used.
        foreach (var (backend, task) in pending)
        {
            // Ignore items that fail the health check
            var response = await task;
            if (!response)
                continue;

            // Check if the backend is suitable
            var ok = await FilterEndpoint(backend.Endpoint, filters);
            if (!ok)
                continue;

            // Backend is alive!
            live.Add(backend);

            // Backend has capacity, try to use this one
            if (backend.AvailableSlots > 0)
            {
                var scope = await CreateScope(backend, TimeSpan.FromSeconds(0.1f), cancellation);
                if (scope != null)
                    return scope;
            }
        }

        // Immediately fail if every backend failed the health check
        if (live.Count == 0)
            return null;

        // Cycle through backends trying to acquire a slot
        var remove = new List<Backend>();
        while (live.Count > 0 && !cancellation.IsCancellationRequested)
        {
            foreach (var backend in live)
            {
                // Do another health check
                if (!await backend.CheckHealth(_healthCheckClient, _logger, cancellation))
                {
                    remove.Add(backend);
                    continue;
                }

                // Try to acquire a scope for this backend
                if (backend.AvailableSlots > 0)
                {
                    var scope = await CreateScope(backend, TimeSpan.FromSeconds(0.1f), cancellation);
                    if (scope != null)
                        return scope;
                }
            }

            // Remove dead backends
            foreach (var item in remove)
                live.Remove(item);
            remove.Clear();

            // We checked every backend, and didn't acquire a slot from any of them! Wait a bit and try again
            await Task.Delay(TimeSpan.FromSeconds(0.1f), cancellation);
        }

        // No live backends available
        return default;

        static async ValueTask<Scope?> CreateScope(Backend backend, TimeSpan timeout, CancellationToken cancellation)
        {
            var acquired = await backend.Wait(timeout, cancellation);
            if (!acquired)
                return null;

            return new Scope(backend);
        }
    }

    /// <summary>
    /// An API backend
    /// </summary>
    private class Backend
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly IEndpointHealthChecker _healthChecker;

        /// <summary>
        /// Get the backend object
        /// </summary>
        public TEndpoint Endpoint { get; }

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
        /// <param name="endpoint"></param>
        /// <param name="concurrentAccess"></param>
        /// <param name="healthChecker"></param>
        public Backend(TEndpoint endpoint, int concurrentAccess, IEndpointHealthChecker healthChecker)
        {
            _semaphore = new(concurrentAccess);

            Endpoint = endpoint;
            _healthChecker = healthChecker;
            TotalSlots = concurrentAccess;
        }

        /// <summary>
        /// Wait for this backend to become available
        /// </summary>
        /// <param name="timeout"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task<bool> Wait(TimeSpan timeout, CancellationToken cancellationToken)
        {
            return _semaphore.WaitAsync(timeout, cancellationToken);
        }

        /// <summary>
        /// Release a slot to this backend
        /// </summary>
        public void Release()
        {
            _semaphore.Release();
        }

        public async Task<bool> CheckHealth(HttpClient http, ILogger logger, CancellationToken cancellation)
        {
            return await _healthChecker.CheckHealth(http, logger, cancellation);
        }
    }

    /// <summary>
    /// Configuration for an endpoint
    /// </summary>
    public sealed record EndpointConfig
    {
        /// <summary>
        /// Gets the endpoint associated with this configuration.
        /// </summary>
        public TEndpoint Endpoint { get; init; }

        /// <summary>
        /// Gets the maximum number of concurrent accesses allowed for the endpoint.
        /// </summary>
        /// <remarks>
        /// This property defines the total number of "slots" available for concurrent usage of the endpoint.
        /// It is used to limit the number of simultaneous operations that can be performed on the endpoint.
        /// </remarks>
        public int Slots { get; init; }

        /// <summary>
        /// Gets the health checker instance responsible for monitoring the health of the endpoint.
        /// </summary>
        /// <value>
        /// An implementation of <see cref="IEndpointHealthChecker"/> used to determine the availability of the endpoint.
        /// </value>
        public IEndpointHealthChecker HealthChecker { get; init; }

        /// <summary>
        /// Create endpoint config using default HTTP health checker.
        /// </summary>
        /// <param name="Endpoint"></param>
        /// <param name="Slots"></param>
        /// <param name="HealthCheck"></param>
        public EndpointConfig(TEndpoint Endpoint, int Slots, Uri HealthCheck)
            : this(Endpoint, Slots, new HttpHealthChecker(HealthCheck))
        {
        }

        /// <summary>
        /// Configuration for an endpoint
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="slots"></param>
        /// <param name="healthChecker"></param>
        public EndpointConfig(TEndpoint endpoint, int slots, IEndpointHealthChecker healthChecker)
        {
            Endpoint = endpoint;
            Slots = slots;
            HealthChecker = healthChecker;
        }
    }

    /// <summary>
    /// A scope of backend usage, while this is held a slot is consumed on the backend
    /// </summary>
    public interface IScope
        : IDisposable
    {
        /// <summary>
        /// Get the endpoint associated with this scope
        /// </summary>
        public TEndpoint Endpoint { get; }
    }

    /// <summary>
    /// A scope of backend usage, while this is held a slot is consumed on the backend
    /// </summary>
    private sealed class Scope
        : IScope
    {
        private readonly Backend _backend;
        private int _released;

        /// <summary>
        /// Get the endpoint associated with this scope
        /// </summary>
        public TEndpoint Endpoint => _backend.Endpoint;

        /// <summary>
        /// Create a new scope. <b>Must acquire a semaphore slot **before** calling this!</b>
        /// </summary>
        /// <param name="backend"></param>
        public Scope(Backend backend)
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
            return new Status(backend.Endpoint, backend.AvailableSlots, backend.TotalSlots, result, timer.Elapsed);
        }).ToList();

        return await Task.WhenAll(pending);
    }

    /// <summary>
    /// Status info for a backend
    /// </summary>
    /// <param name="Endpoint"></param>
    /// <param name="AvailableSlots"></param>
    /// <param name="MaxSlots"></param>
    /// <param name="Healthy"></param>
    /// <param name="Latency"></param>
    public record Status(TEndpoint Endpoint, int AvailableSlots, int MaxSlots, bool Healthy, TimeSpan Latency);
}
