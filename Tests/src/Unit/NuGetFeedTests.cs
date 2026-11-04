namespace BlueHeighliner.Bundler.Tests;

public sealed class NuGetFeedTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly NuGetFeed feed;

    public NuGetFeedTests()
    {
        Mock<IPaths> paths = new();
        paths.SetupGet(p => p.NuGetFeed).Returns(directory.Combine("feed"));
        feed = new(paths.Object, Mock.Of<IUsers>(), new Versions());
    }

    public void Dispose()
        => directory.Dispose();

    private string Source(string name, params string[] packages)
    {
        foreach (string package in packages)
        {
            directory.Write($"{name}/{package}/{package.Split('/')[0]}.nupkg", "x");
        }

        return directory.Combine(name);
    }

    private BundleState State(string bundle)
        => new(directory.Combine("state", bundle));

    [Fact]
    public void Add_RecordsNewVersionsAsAddedAndExistingOnesAsShared()
    {
        BundleState first = State("xunit");
        BundleState second = State("avalonia");

        feed.Add(Source("a", "moq/4.0", "common/1.0"), first);
        (int added, int total) = feed.Add(Source("b", "common/1.0", "avalonia/12.0"), second);

        Assert.Equal((1, 2), (added, total));
        Assert.Equal(["common/1.0", "moq/4.0"], first.Lines("nuget-added").Order());
        Assert.Equal(["avalonia/12.0"], second.Lines("nuget-added"));
        Assert.Equal(["common/1.0"], second.Lines("nuget-shared"));
    }

    [Fact]
    public void Remove_KeepsAVersionAnotherBundleSharesUntilThatBundleIsRemoved()
    {
        BundleState first = State("xunit");
        BundleState second = State("avalonia");
        feed.Add(Source("a", "moq/4.0", "common/1.0"), first);
        feed.Add(Source("b", "common/1.0", "avalonia/12.0"), second);

        feed.Remove(first);

        Assert.False(Directory.Exists(directory.Combine("feed", "moq")));
        Assert.True(Directory.Exists(directory.Combine("feed", "common", "1.0")));
        Assert.Equal(["avalonia/12.0", "common/1.0"], second.Lines("nuget-added").Order());
        Assert.Empty(second.Lines("nuget-shared"));

        feed.Remove(second);

        Assert.False(Directory.Exists(directory.Combine("feed", "common")));
        Assert.False(Directory.Exists(directory.Combine("feed", "avalonia")));
    }

    [Fact]
    public void Remove_VersionSharedByTwoOthers_PassesThroughBoth()
    {
        BundleState a = State("a");
        BundleState b = State("b");
        BundleState c = State("c");
        feed.Add(Source("sa", "common/1.0"), a);
        feed.Add(Source("sb", "common/1.0"), b);
        feed.Add(Source("sc", "common/1.0"), c);

        feed.Remove(a);
        feed.Remove(b);
        Assert.True(Directory.Exists(directory.Combine("feed", "common", "1.0")));

        feed.Remove(c);
        Assert.False(Directory.Exists(directory.Combine("feed", "common")));
    }
}
