namespace BlueHeighliner.Bundler.Tests;

public sealed class NuGetFeedTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IUsers> users = new();
    private readonly NuGetFeed feed;

    public NuGetFeedTests()
    {
        Mock<IPaths> paths = new();
        paths.SetupGet(p => p.NuGetFeed).Returns(directory.Combine("feed"));
        feed = new(paths.Object, users.Object, new Versions());
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

    [Fact]
    public async Task InstallTemplate_InstallsFromACopyTheUserCanReadAndRemovesIt()
    {
        string nupkg = directory.Write("state/pack.1.0.nupkg", "pack");
        string? seen = null;
        bool readable = false;
        users.Setup(u => u.RunAs("alice", false, "dotnet", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(
            (string _, bool _, string _, IReadOnlyList<string> arguments, CancellationToken _) =>
            {
                seen = arguments[2];
                readable = File.Exists(seen) && File.GetUnixFileMode(seen).HasFlag(UnixFileMode.OtherRead) && File.GetUnixFileMode(Path.GetDirectoryName(seen)!).HasFlag(UnixFileMode.OtherExecute);
                return new ProcessResult(0, string.Empty, string.Empty);
            });

        bool installed = await feed.InstallTemplate("alice", nupkg);

        Assert.True(installed);
        Assert.NotEqual(nupkg, seen);
        Assert.True(readable);
        Assert.EndsWith("pack.1.0.nupkg", seen);
        Assert.False(File.Exists(seen));
    }
}
