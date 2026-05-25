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
        // Clear previous if the provider has changed
        if (!ReferenceEquals(provider, _provider))
        {
            _provider = null;
            _backend = null;
        }

        // If we've got a backend we previously used, check if it's healthy
        if (_backend != null)
        {
            var healthy = await _backend.CheckHealth(cancellation);
            if (!healthy)
            {
                _provider = null;
                _backend = null;
            }
        }
        
        // If we've still got a healthy backend we previously used, try to acquire a slot now
        if (_backend != null)
        {
            var scope = await _backend.Acquire(_timeout, cancellation);
            if (scope != null)
                return scope;
        }

        // Choose a new backend
        var backend = await provider.GetBackend(_tags, cancellation);
        if (backend == null)
            return null;
        
        // Try to acquire a scope, if it succeeds save this backend for next time
        var scope2 = await backend.Acquire(_timeout, cancellation);
        if (scope2 != null)
        {
            _provider = provider;
            _backend = backend;
            return scope2;
        }

        return null;
    }
}