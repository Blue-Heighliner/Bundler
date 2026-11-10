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
        Assert.Equal(
            ["git", "a2000", "dotnet", "ollama", "vs-code", "gemma", "open-code", "xunit", "avalonia", "protobuf", "sqlite", "godot", "podman", "gimp", "krita", "blender", "auto-remote"],
            catalog.All.Select(bundle => bundle.Name));
        Assert.All(catalog.All, bundle => Assert.All(bundle.Requires, required => Assert.Contains(catalog.All, other => other.Name == required)));
        Assert.All(catalog.All, bundle => Assert.NotEmpty(bundle.Description));
        Assert.All(catalog.All, bundle => Assert.Equal("Ubuntu 24.04", bundle.Target));
    }

    [Theory]
    [InlineData("dotnet", new string[0])]
    [InlineData("ollama", new string[0])]
    [InlineData("open-code", new[] { "git" })]
    [InlineData("vs-code", new[] { "git", "dotnet", "ollama" })]
    [InlineData("gemma", new[] { "ollama" })]
    [InlineData("xunit", new[] { "dotnet" })]
    [InlineData("godot", new[] { "dotnet" })]
    [InlineData("gimp", new string[0])]
    [InlineData("krita", new string[0])]
    [InlineData("blender", new string[0])]
    [InlineData("auto-remote", new[] { "git", "dotnet", "ollama", "gemma", "open-code" })]
    public void RealBundles_NeedExactlyTheBundlesTheyUse(string name, string[] requires)
    {
        using ServiceProvider services = new ServiceCollection()
            .AddBundler(_ => new ObservableLog())
            .BuildServiceProvider();

        Assert.Equal(requires, services.GetRequiredService<IBundleCatalog>().Get(name).Requires);
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
