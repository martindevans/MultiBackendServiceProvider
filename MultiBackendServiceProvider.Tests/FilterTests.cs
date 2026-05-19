namespace MultiBackendServiceProvider.Tests;

[TestClass]
public class FilterTests
{
    private static readonly string[] NoFilters = [];
    private static readonly string[] SomeFilters = ["a", "b"];

    // AcceptFilter

    [TestMethod]
    public async Task AcceptFilter_ReturnsTrue_WhenNoFilters()
    {
        var filter = new AcceptFilter<string>();
        Assert.IsTrue(await filter.FilterEndpoint("endpoint", NoFilters));
    }

    [TestMethod]
    public async Task AcceptFilter_ReturnsTrue_WhenFiltersProvided()
    {
        var filter = new AcceptFilter<string>();
        Assert.IsTrue(await filter.FilterEndpoint("endpoint", SomeFilters));
    }

    // DenyFilter

    [TestMethod]
    public async Task DenyFilter_ReturnsFalse_WhenNoFilters()
    {
        var filter = new DenyFilter<string>();
        Assert.IsFalse(await filter.FilterEndpoint("endpoint", NoFilters));
    }

    [TestMethod]
    public async Task DenyFilter_ReturnsFalse_WhenFiltersProvided()
    {
        var filter = new DenyFilter<string>();
        Assert.IsFalse(await filter.FilterEndpoint("endpoint", SomeFilters));
    }

    // AndFilter

    [TestMethod]
    public async Task AndFilter_TrueAndTrue_ReturnsTrue()
    {
        var filter = new AndFilter<string>(new AcceptFilter<string>(), new AcceptFilter<string>());
        Assert.IsTrue(await filter.FilterEndpoint("endpoint", NoFilters));
    }

    [TestMethod]
    public async Task AndFilter_TrueAndFalse_ReturnsFalse()
    {
        var filter = new AndFilter<string>(new AcceptFilter<string>(), new DenyFilter<string>());
        Assert.IsFalse(await filter.FilterEndpoint("endpoint", NoFilters));
    }

    [TestMethod]
    public async Task AndFilter_FalseAndTrue_ReturnsFalse()
    {
        var filter = new AndFilter<string>(new DenyFilter<string>(), new AcceptFilter<string>());
        Assert.IsFalse(await filter.FilterEndpoint("endpoint", NoFilters));
    }

    [TestMethod]
    public async Task AndFilter_FalseAndFalse_ReturnsFalse()
    {
        var filter = new AndFilter<string>(new DenyFilter<string>(), new DenyFilter<string>());
        Assert.IsFalse(await filter.FilterEndpoint("endpoint", NoFilters));
    }

    // OrFilter

    [TestMethod]
    public async Task OrFilter_TrueOrTrue_ReturnsTrue()
    {
        var filter = new OrFilter<string>(new AcceptFilter<string>(), new AcceptFilter<string>());
        Assert.IsTrue(await filter.FilterEndpoint("endpoint", NoFilters));
    }

    [TestMethod]
    public async Task OrFilter_TrueOrFalse_ReturnsTrue()
    {
        var filter = new OrFilter<string>(new AcceptFilter<string>(), new DenyFilter<string>());
        Assert.IsTrue(await filter.FilterEndpoint("endpoint", NoFilters));
    }

    [TestMethod]
    public async Task OrFilter_FalseOrTrue_ReturnsTrue()
    {
        var filter = new OrFilter<string>(new DenyFilter<string>(), new AcceptFilter<string>());
        Assert.IsTrue(await filter.FilterEndpoint("endpoint", NoFilters));
    }

    [TestMethod]
    public async Task OrFilter_FalseOrFalse_ReturnsFalse()
    {
        var filter = new OrFilter<string>(new DenyFilter<string>(), new DenyFilter<string>());
        Assert.IsFalse(await filter.FilterEndpoint("endpoint", NoFilters));
    }

    // XorFilter

    [TestMethod]
    public async Task XorFilter_TrueXorTrue_ReturnsFalse()
    {
        var filter = new XorFilter<string>(new AcceptFilter<string>(), new AcceptFilter<string>());
        Assert.IsFalse(await filter.FilterEndpoint("endpoint", NoFilters));
    }

    [TestMethod]
    public async Task XorFilter_TrueXorFalse_ReturnsTrue()
    {
        var filter = new XorFilter<string>(new AcceptFilter<string>(), new DenyFilter<string>());
        Assert.IsTrue(await filter.FilterEndpoint("endpoint", NoFilters));
    }

    [TestMethod]
    public async Task XorFilter_FalseXorTrue_ReturnsTrue()
    {
        var filter = new XorFilter<string>(new DenyFilter<string>(), new AcceptFilter<string>());
        Assert.IsTrue(await filter.FilterEndpoint("endpoint", NoFilters));
    }

    [TestMethod]
    public async Task XorFilter_FalseXorFalse_ReturnsFalse()
    {
        var filter = new XorFilter<string>(new DenyFilter<string>(), new DenyFilter<string>());
        Assert.IsFalse(await filter.FilterEndpoint("endpoint", NoFilters));
    }
}
