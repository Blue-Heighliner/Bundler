namespace BlueHeighliner.Bundler.Tests;

public sealed class MarketplaceTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IHttpFetcher> http = new();
    private readonly List<string> downloaded = [];
    private readonly Marketplace marketplace;

    public MarketplaceTests()
    {
        http.Setup(h => h.Download(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<HashSpec?>(), It.IsAny<CancellationToken>())).Returns((string url, string path, HashSpec? _, CancellationToken _) =>
        {
            downloaded.Add(url);
            using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using StreamWriter writer = new(zip.CreateEntry("extension/package.json").Open());
            writer.Write("{}");
            return Task.CompletedTask;
        });
        marketplace = new(http.Object, new Versions(), Mock.Of<ILog>());
    }

    public void Dispose()
        => directory.Dispose();

    private void Offer(params string[] versions)
        => http.Setup(h => h.PostJson("https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery", It.IsAny<JsonNode>(), It.IsAny<CancellationToken>())).ReturnsAsync(JsonNode.Parse(
            $$"""
            { "results": [ { "extensions": [ { "versions": [ {{string.Join(',', versions.Select(version => $$"""{ "version": "{{version}}", "files": [ { "assetType": "Microsoft.VisualStudio.Services.VSIXPackage", "source": "https://example.test/{{version}}.vsix" } ] }"""))}} ] } ] } ] }
            """)!);

    [Fact]
    public async Task Fetch_TakesTheHighestRelease()
    {
        Offer("1.5.0", "2.1.0", "2.0.5", "10.0.0");

        IReadOnlyList<ExtensionPackage> packages = await marketplace.Fetch(directory.Combine("ext"), ["a.b"]);

        Assert.Equal("10.0.0", Assert.Single(packages).Version);
    }

    [Fact]
    public async Task Fetch_DownloadsTheRelease()
    {
        Offer("2.0.0");

        IReadOnlyList<ExtensionPackage> packages = await marketplace.Fetch(directory.Combine("ext"), ["a.b"]);

        Assert.Equal(["https://example.test/2.0.0.vsix"], downloaded);
        Assert.Equal("a.b", Assert.Single(packages).Id);
    }
}
