namespace BlueHeighliner.Bundler.Tests;

public sealed class BundleCatalogTests
{
    private static IBundle Bundle(string name, int order, params string[] requires)
    {
        Mock<IBundle> bundle = new();
        bundle.SetupGet(b => b.Name).Returns(name);
        bundle.SetupGet(b => b.Order).Returns(order);
        bundle.SetupGet(b => b.Requires).Returns(requires);
        return bundle.Object;
    }

    [Fact]
    public void All_IsOrderedByOrder()
        => Assert.Equal(["a", "b", "c"], new BundleCatalog([Bundle("c", 30), Bundle("a", 10), Bundle("b", 20)]).All.Select(bundle => bundle.Name));

    [Fact]
    public void Get_UnknownName_Throws()
        => Assert.Throws<ArgumentException>(() => new BundleCatalog([Bundle("a", 1)]).Get("zzz"));

    [Fact]
    public void RealBundles_AreRegisteredWithUniqueNamesAndKnownRequirements()
    {
        using ServiceProvider services = new ServiceCollection()
            .AddBundler(_ => new ObservableLog())
            .BuildServiceProvider();

        IBundleCatalog catalog = services.GetRequiredService<IBundleCatalog>();

        Assert.Equal(catalog.All.Count, catalog.All.Select(bundle => bundle.Name).Distinct().Count());
        Assert.Equal(12, catalog.All.Count);
        Assert.All(catalog.All, bundle => Assert.All(bundle.Requires, required => Assert.Contains(catalog.All, other => other.Name == required)));
        Assert.All(catalog.All, bundle => Assert.NotEmpty(bundle.Description));
        Assert.All(catalog.All, bundle => Assert.Equal("Ubuntu 26.04", bundle.Target));
    }

    [Fact]
    public void Requirements_AreInstalledBeforeTheBundlesThatNeedThem()
    {
        using ServiceProvider services = new ServiceCollection()
            .AddBundler(_ => new ObservableLog())
            .BuildServiceProvider();

        IBundleCatalog catalog = services.GetRequiredService<IBundleCatalog>();

        foreach (IBundle bundle in catalog.All)
        {
            Assert.All(bundle.Requires, required => Assert.True(catalog.Get(required).Order < bundle.Order));
        }
    }
}
