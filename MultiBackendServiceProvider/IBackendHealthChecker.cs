using Microsoft.Extensions.Logging;

namespace MultiBackendServiceProvider;

/// <summary>
/// Pluggable health-check implementation for a backend.
/// </summary>
public interface IBackendHealthChecker
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
/// Default health checker that performs an HTTP GET against a configured URL. Backend is considered healthy if the
/// request returns a success (200-299) status code.
/// </summary>
public sealed class HttpHealthChecker
    : IBackendHealthChecker
{
    private readonly Uri _healthCheck;

    /// <summary>
    /// Default health checker that performs an HTTP GET against a configured URL. Backend is considered healthy if the
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