namespace MultiBackendServiceProvider;

/// <summary>
/// Filters endpoints based on string tags
/// </summary>
public interface IEndpointFilter<in TEndpoint>
{
    /// <summary>
    /// Given and endpoint and a set of filters, return if the endpoint is allowed
    /// </summary>
    /// <param name="endpoint"></param>
    /// <param name="filters"></param>
    /// <returns></returns>
    ValueTask<bool> FilterEndpoint(TEndpoint endpoint, IReadOnlyList<string> filters);
}

/// <summary>
/// Always accepts
/// </summary>
public class AcceptFilter<TService>
    : IEndpointFilter<TService>
{
    /// <inheritdoc />
    public ValueTask<bool> FilterEndpoint(TService endpoint, IReadOnlyList<string> filters)
    {
        return new ValueTask<bool>(true);
    }
}

/// <summary>
/// Always denys
/// </summary>
public class DenyFilter<TService>
    : IEndpointFilter<TService>
{
    /// <inheritdoc />
    public ValueTask<bool> FilterEndpoint(TService endpoint, IReadOnlyList<string> filters)
    {
        return new ValueTask<bool>(false);
    }
}

/// <summary>
/// Combine two filters with logical AND
/// </summary>
/// <typeparam name="TService"></typeparam>
public class AndFilter<TService>
    : IEndpointFilter<TService>
{
    private readonly IEndpointFilter<TService> _a;
    private readonly IEndpointFilter<TService> _b;

    public AndFilter(IEndpointFilter<TService> a, IEndpointFilter<TService> b)
    {
        _a = a;
        _b = b;
    }
    
    public async ValueTask<bool> FilterEndpoint(TService endpoint, IReadOnlyList<string> filters)
    {
        return await _a.FilterEndpoint(endpoint, filters)
            && await _b.FilterEndpoint(endpoint, filters);
    }
}

/// <summary>
/// Combine two filters with logical OR
/// </summary>
/// <typeparam name="TService"></typeparam>
public class OrFilter<TService>
    : IEndpointFilter<TService>
{
    private readonly IEndpointFilter<TService> _a;
    private readonly IEndpointFilter<TService> _b;

    public OrFilter(IEndpointFilter<TService> a, IEndpointFilter<TService> b)
    {
        _a = a;
        _b = b;
    }

    public async ValueTask<bool> FilterEndpoint(TService endpoint, IReadOnlyList<string> filters)
    {
        return await _a.FilterEndpoint(endpoint, filters)
            || await _b.FilterEndpoint(endpoint, filters);
    }
}

/// <summary>
/// Combine two filters with logical XOR
/// </summary>
/// <typeparam name="TService"></typeparam>
public class XorFilter<TService>
    : IEndpointFilter<TService>
{
    private readonly IEndpointFilter<TService> _a;
    private readonly IEndpointFilter<TService> _b;

    public XorFilter(IEndpointFilter<TService> a, IEndpointFilter<TService> b)
    {
        _a = a;
        _b = b;
    }

    public async ValueTask<bool> FilterEndpoint(TService endpoint, IReadOnlyList<string> filters)
    {
        return await _a.FilterEndpoint(endpoint, filters)
             ^ await _b.FilterEndpoint(endpoint, filters);
    }
}