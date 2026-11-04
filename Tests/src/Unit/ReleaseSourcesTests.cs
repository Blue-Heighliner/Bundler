namespace BlueHeighliner.Bundler.Tests;

public sealed class ReleaseSourcesTests
{
    private readonly Mock<IHttpFetcher> http = new();
    private readonly ReleaseSources sources;

    public ReleaseSourcesTests()
        => sources = new(http.Object, new Versions());

    private void Page(string url, string text)
        => http.Setup(h => h.GetString(url, null, It.IsAny<CancellationToken>())).ReturnsAsync(text);

    [Fact]
    public async Task LatestBlender_TakesTheNewestVersionOfTheNewestSeries()
    {
        Page("https://download.blender.org/release/", "<a href=\"Blender4.5/\">x</a><a href=\"Blender5.2/\">x</a><a href=\"Blender5.10/\">x</a><a href=\"Blender5.9/\">x</a>");
        Page("https://download.blender.org/release/Blender5.10/", "<a href=\"blender-5.10.1-linux-x64.tar.xz\">a</a><a href=\"blender-5.10.0-linux-x64.tar.xz\">a</a><a href=\"blender-5.10.9-windows-x64.zip\">a</a>");
        Page("https://download.blender.org/release/Blender5.10/blender-5.10.1.sha256", "AAAA  blender-5.10.1-windows-x64.zip\nABCDEF  blender-5.10.1-linux-x64.tar.xz\n");

        BlenderRelease release = await sources.LatestBlender();

        Assert.Equal("5.10.1", release.Version);
        Assert.Equal("blender-5.10.1-linux-x64.tar.xz", release.FileName);
        Assert.Equal("https://download.blender.org/release/Blender5.10/blender-5.10.1-linux-x64.tar.xz", release.Url);
        Assert.Equal("abcdef", release.Hash.Hex);
    }

    [Fact]
    public async Task LatestBlender_SeriesWithoutALinuxArchive_FallsBackToTheOlderOne()
    {
        Page("https://download.blender.org/release/", "<a href=\"Blender5.1/\">x</a><a href=\"Blender5.2/\">x</a>");
        Page("https://download.blender.org/release/Blender5.2/", "<a href=\"blender-5.2.0-windows-x64.zip\">a</a>");
        Page("https://download.blender.org/release/Blender5.1/", "<a href=\"blender-5.1.3-linux-x64.tar.xz\">a</a>");
        Page("https://download.blender.org/release/Blender5.1/blender-5.1.3.sha256", "0f  blender-5.1.3-linux-x64.tar.xz");

        Assert.Equal("5.1.3", (await sources.LatestBlender()).Version);
    }

    [Fact]
    public async Task LatestBlender_MissingChecksum_Throws()
    {
        Page("https://download.blender.org/release/", "<a href=\"Blender5.2/\">x</a>");
        Page("https://download.blender.org/release/Blender5.2/", "<a href=\"blender-5.2.0-linux-x64.tar.xz\">a</a>");
        Page("https://download.blender.org/release/Blender5.2/blender-5.2.0.sha256", "ff  something-else.zip");

        await Assert.ThrowsAsync<InvalidOperationException>(() => sources.LatestBlender());
    }

    [Fact]
    public async Task LatestBlender_NothingAtAll_Throws()
    {
        Page("https://download.blender.org/release/", "empty");

        await Assert.ThrowsAsync<InvalidOperationException>(() => sources.LatestBlender());
    }
}
