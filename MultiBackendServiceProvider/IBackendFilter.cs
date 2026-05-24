namespace MultiBackendServiceProvider;

/// <summary>
/// Filters backends based on string tags. Strings could be a list of capabilities being requested, it's up to the filter
/// to determine how to interpret the string and if the given backend is suitable for them.
/// </summary>
public interface IBackendFilter<in TBackend>
{
    /// <summary>
    /// Given and backend and a set of filters, return if the backend is allowed
    /// </summary>
    /// <param name="backend"></param>
    /// <param name="tags"></param>
    /// <returns></returns>
    ValueTask<bool> Filter(TBackend backend, IReadOnlyCollection<string> tags);
}

/// <summary>
/// Always accepts
/// </summary>
public class AcceptFilter<TService>
    : IBackendFilter<TService>
{
    /// <inheritdoc />
    public ValueTask<bool> Filter(TService backend, IReadOnlyCollection<string> tags)
    {
        return new ValueTask<bool>(true);
    }
}

/// <summary>
/// Always denys
/// </summary>
public class DenyFilter<TService>
    : IBackendFilter<TService>
{
    /// <inheritdoc />
    public ValueTask<bool> Filter(TService backend, IReadOnlyCollection<string> tags)
    {
        return new ValueTask<bool>(false);
    }
}

/// <summary>
/// Combine two filters with logical AND
/// </summary>
/// <typeparam name="TService"></typeparam>
public class AndFilter<TService>
    : IBackendFilter<TService>
{
    private readonly IBackendFilter<TService> _a;
    private readonly IBackendFilter<TService> _b;

    public AndFilter(IBackendFilter<TService> a, IBackendFilter<TService> b)
    {
        _a = a;
        _b = b;
    }
    
    public async ValueTask<bool> Filter(TService backend, IReadOnlyCollection<string> tags)
    {
        return await _a.Filter(backend, tags)
            && await _b.Filter(backend, tags);
    }
}

/// <summary>
/// Combine two filters with logical OR
/// </summary>
/// <typeparam name="TService"></typeparam>
public class OrFilter<TService>
    : IBackendFilter<TService>
{
    private readonly IBackendFilter<TService> _a;
    private readonly IBackendFilter<TService> _b;

    public OrFilter(IBackendFilter<TService> a, IBackendFilter<TService> b)
    {
        _a = a;
        _b = b;
    }

    public async ValueTask<bool> Filter(TService backend, IReadOnlyCollection<string> tags)
    {
        return await _a.Filter(backend, tags)
            || await _b.Filter(backend, tags);
    }
}

/// <summary>
/// Combine two filters with logical XOR
/// </summary>
/// <typeparam name="TService"></typeparam>
public class XorFilter<TService>
    : IBackendFilter<TService>
{
    private readonly IBackendFilter<TService> _a;
    private readonly IBackendFilter<TService> _b;

    public XorFilter(IBackendFilter<TService> a, IBackendFilter<TService> b)
    {
        _a = a;
        _b = b;
    }

    public async ValueTask<bool> Filter(TService backend, IReadOnlyCollection<string> tags)
    {
        return await _a.Filter(backend, tags)
             ^ await _b.Filter(backend, tags);
    }
}