namespace MultiBackendServiceProvider;

/// <summary>
/// Represents a request to acquire a backend from a <see cref="MultiBackendServiceProvider{TBackend}"/>.
/// Re-uses previous backend for as long as it remains healthy and available.
/// </summary>
/// <typeparam name="TBackend">
/// The type of the backend being requested.
/// </typeparam>
public class BackendRequest<TBackend>
{
    private readonly TimeSpan _timeout;
    private readonly string[] _tags;

    private MultiBackendServiceProvider<TBackend>? _provider;
    private Backend<TBackend>? _backend;

    private SemaphoreSlim _lock = new(1, 1);

    public BackendRequest(TimeSpan timeout, params string[] tags)
    {
        _timeout = timeout;
        _tags = tags;
    }

    public BackendRequest(params string[] tags)
        : this(TimeSpan.FromMilliseconds(100), tags)
    {
    }

    public async Task<Backend<TBackend>.IScope?> Acquire(MultiBackendServiceProvider<TBackend> provider, CancellationToken cancellation)
    {
        await _lock.WaitAsync(_timeout, cancellation);
        try
        {
            // Clear previous if the provider has changed
            if (!ReferenceEquals(provider, _provider))
            {
                _provider = null;
                _backend = null;
            }

            // Check if the backend we used last time is healthy
            if (_backend != null)
            {
                var healthy = await _backend.CheckHealth(cancellation);
                if (!healthy)
                {
                    _provider = null;
                    _backend = null;
                }
            }

            // Try to acquire a scope from the backend we used last time
            if (_backend != null)
            {
                var scope = await _backend.Acquire(_timeout, cancellation);
                if (scope != null)
                    return scope;
            }

            // Acquire a new scope from a fresh backend
            var scope2 = await provider.Acquire(_tags, cancellation);
            if (scope2 != null)
            {
                _provider = provider;
                _backend = scope2.Backend;
                return scope2;
            }

            return null;
        }
        finally
        {
            _lock.Release();
        }
    }
}