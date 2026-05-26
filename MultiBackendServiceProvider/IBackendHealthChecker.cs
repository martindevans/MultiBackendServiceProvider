namespace MultiBackendServiceProvider;

/// <summary>
/// Pluggable health-check implementation for a backend.
/// </summary>
public interface IBackendHealthChecker
{
    /// <summary>
    /// Check backend health and return true when backend should be considered available.
    /// </summary>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    ValueTask<bool> CheckHealth(CancellationToken cancellation);
}

/// <summary>
/// Default health checker that performs an HTTP GET against a configured URL. Backend is considered healthy if the
/// request returns a success (200-299) status code.
/// </summary>
public sealed class HttpHealthChecker
    : IBackendHealthChecker
{
    private static readonly HttpClient _staticClient;
    
    private readonly Uri _healthCheck;
    private readonly HttpClient? _client;

    static HttpHealthChecker()
    {
        _staticClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(0.5f)
        };
    }

    /// <summary>
    /// Default health checker that performs an HTTP GET against a configured URL. Backend is considered healthy if the
    /// request returns a success (200-299) status code.
    /// </summary>
    /// <param name="healthCheck">URL to check</param>
    /// <param name="client">HTTP client to use</param>
    public HttpHealthChecker(Uri healthCheck, HttpClient? client = null)
    {
        _healthCheck = healthCheck;
        _client = client;
    }

    public async ValueTask<bool> CheckHealth(CancellationToken cancellation)
    {
        var client = _client ?? _staticClient;

        try
        {
            using var result = await client.GetAsync(_healthCheck, cancellation);
            return result.IsSuccessStatusCode;
        }
        catch (TaskCanceledException)
        {
            // This might be a timeout or a genuine cancellation.
            
            // Check for cancellation
            cancellation.ThrowIfCancellationRequested();

            // Just a timeout
            return false;
        }
        catch
        {
            return false;
        }
    }
}