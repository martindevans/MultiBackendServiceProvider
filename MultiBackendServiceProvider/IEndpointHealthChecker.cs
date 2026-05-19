using Microsoft.Extensions.Logging;

namespace MultiBackendServiceProvider;

/// <summary>
/// Pluggable health-check implementation for an endpoint backend.
/// </summary>
public interface IEndpointHealthChecker
{
    /// <summary>
    /// Check backend health and return true when backend should be considered available.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="cancellation"></param>
    /// <param name="http"></param>
    /// <returns></returns>
    ValueTask<bool> CheckHealth(HttpClient http, ILogger logger, CancellationToken cancellation);
}

/// <summary>
/// Default health checker that performs an HTTP GET against a configured URL. Endpoint is considered healthy if the
/// request returns a success (200-299) status code.
/// </summary>
public sealed class HttpHealthChecker
    : IEndpointHealthChecker
{
    private readonly Uri _healthCheck;

    /// <summary>
    /// Default health checker that performs an HTTP GET against a configured URL. Endpoint is considered healthy if the
    /// request returns a success (200-299) status code.
    /// </summary>
    /// <param name="healthCheck"></param>
    public HttpHealthChecker(Uri healthCheck)
    {
        _healthCheck = healthCheck;
    }

    public async ValueTask<bool> CheckHealth(HttpClient http, ILogger logger, CancellationToken cancellation)
    {
        try
        {
            var result = await http.GetAsync(_healthCheck, cancellation);
            return result.IsSuccessStatusCode;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check exception");
            return false;
        }
    }
}