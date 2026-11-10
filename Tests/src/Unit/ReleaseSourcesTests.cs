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

    private static JsonNode Release(bool draft = false)
        => JsonNode.Parse($$"""
            {
              "tag_name": "1.2.0",
              "draft": {{(draft ? "true" : "false")}},
              "prerelease": false,
              "assets": [
                { "name": "AutoRemote-linux", "browser_download_url": "https://example.com/client", "digest": "sha256:aaaa" },
                { "name": "AutoRemote-Server-linux", "browser_download_url": "https://example.com/server", "digest": "sha256:bbbb" },
                { "name": "AutoRemote-Server-win.exe", "browser_download_url": "https://example.com/win", "digest": "sha256:cccc" }
              ]
            }
            """)!;

    [Fact]
    public async Task GitHubTag_ReturnsTheAssetsInTheOrderOfThePatterns()
    {
        http.Setup(h => h.GetJson("https://api.github.com/repos/o/r/releases/tags/1.2.0", null, It.IsAny<CancellationToken>())).ReturnsAsync(Release());

        GitHubRelease release = await sources.GitHubTag("o/r", "1.2.0", ["^AutoRemote-Server-linux$", "^AutoRemote-linux$"]);

        Assert.Equal("1.2.0", release.Tag);
        Assert.Equal(["AutoRemote-Server-linux", "AutoRemote-linux"], release.Assets.Select(asset => asset.FileName));
        Assert.Equal("https://example.com/server", release.Assets[0].Url);
        Assert.Equal("bbbb", release.Assets[0].Hash!.Hex);
        Assert.Equal("sha256", release.Assets[0].Hash!.Algorithm);
    }

    [Fact]
    public async Task GitHubTag_MissingAsset_Throws()
    {
        http.Setup(h => h.GetJson(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(Release());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => sources.GitHubTag("o/r", "1.2.0", ["^nothing$"]));

        Assert.Contains("no asset matches", error.Message);
    }

    [Fact]
    public async Task GitHubTag_Draft_Throws()
    {
        http.Setup(h => h.GetJson(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(Release(draft: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sources.GitHubTag("o/r", "1.2.0", ["^AutoRemote-linux$"]));
    }

    private static JsonNode Releases(params (string Tag, bool Prerelease, bool Draft)[] releases)
        => new JsonArray(releases.Select(release => (JsonNode)new JsonObject
        {
            ["tag_name"] = release.Tag,
            ["prerelease"] = release.Prerelease,
            ["draft"] = release.Draft,
            ["assets"] = new JsonArray(new JsonObject { ["name"] = "tool.tar.gz", ["browser_download_url"] = $"https://example.com/{release.Tag}", ["digest"] = "sha256:aaaa" }),
        }).ToArray());

    [Fact]
    public async Task NewestGitHub_TakesTheHighestStableVersionThatMatchesTheTagPattern()
    {
        http.Setup(h => h.GetJson("https://api.github.com/repos/o/r/releases?per_page=100", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Releases(("v1.19.5", false, false), ("v2.2.4", false, false), ("v2.10.0", false, false), ("v2.11.0", true, false), ("v2.12.0", false, true), ("daemon-v9.0.0", false, false), ("v3.0.0", false, false)));

        GitHubRelease release = await sources.NewestGitHub("o/r", @"^v(2\.\d+\.\d+)$", ["^tool\\.tar\\.gz$"]);

        Assert.Equal("v2.10.0", release.Tag);
        Assert.Equal("https://example.com/v2.10.0", release.Assets[0].Url);
    }

    [Fact]
    public async Task NewestGitHub_NothingMatches_Throws()
    {
        http.Setup(h => h.GetJson(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(Releases(("v1.0.0", false, false), ("v2.0.0", true, false)));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => sources.NewestGitHub("o/r", @"^v(2\.\d+\.\d+)$", ["^tool$"]));

        Assert.Contains("no stable release", error.Message);
    }

    [Fact]
    public async Task NewestGitHub_MissingAsset_Throws()
    {
        http.Setup(h => h.GetJson(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(Releases(("v2.0.0", false, false)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sources.NewestGitHub("o/r", @"^v(2\.\d+\.\d+)$", ["^nothing$"]));
    }

    private void DotNetIndex()
    {
        http.Setup(h => h.GetJson("https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json", null, It.IsAny<CancellationToken>())).ReturnsAsync(JsonNode.Parse("""
            { "releases-index": [
              { "channel-version": "12.0", "support-phase": "preview", "releases.json": "https://example.com/12.json" },
              { "channel-version": "11.0", "support-phase": "active", "releases.json": "https://example.com/11.json" },
              { "channel-version": "10.0", "support-phase": "active", "releases.json": "https://example.com/10.json" },
              { "channel-version": "8.0", "support-phase": "eol", "releases.json": "https://example.com/8.json" } ] }
            """)!);
        foreach ((string channel, string sdk) in new[] { ("11", "11.0.100"), ("10", "10.0.404") })
        {
            http.Setup(h => h.GetJson($"https://example.com/{channel}.json", null, It.IsAny<CancellationToken>())).ReturnsAsync(JsonNode.Parse($$"""
                { "latest-sdk": "{{sdk}}", "releases": [ { "release-version": "{{channel}}.0.1", "sdk": { "version": "{{sdk}}", "files": [
                  { "name": "dotnet-sdk-linux-x64.tar.gz", "url": "https://example.com/{{sdk}}.tar.gz", "hash": "ABCD" } ] } } ] }
                """)!);
        }
    }

    [Fact]
    public async Task LatestDotNet_WithoutAChannel_TakesTheNewestSupportedOne()
    {
        DotNetIndex();

        DotNetRelease release = await sources.LatestDotNet();

        Assert.Equal("11.0", release.Channel);
        Assert.Equal("11.0.100", release.Version);
    }

    [Fact]
    public async Task LatestDotNet_WithAChannel_TakesThatChannelEvenWhenANewerOneIsSupported()
    {
        DotNetIndex();

        DotNetRelease release = await sources.LatestDotNet("10.0");

        Assert.Equal("10.0", release.Channel);
        Assert.Equal("10.0.404", release.Version);
        Assert.Equal("abcd", release.Hash.Hex);
    }

    [Theory]
    [InlineData("12.0")]
    [InlineData("8.0")]
    [InlineData("99.0")]
    public async Task LatestDotNet_ChannelThatIsNotSupported_Throws(string channel)
    {
        DotNetIndex();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sources.LatestDotNet(channel));
    }

    private void NuGetIndex()
        => http.Setup(h => h.GetJson("https://api.nuget.org/v3-flatcontainer/some.package/index.json", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonNode.Parse("""{ "versions": [ "9.0.5", "10.0.14", "10.0.15", "10.1.0-preview.1", "11.0.0" ] }""")!);

    [Fact]
    public async Task LatestNuGet_WithoutARange_TakesTheHighestStableVersion()
    {
        NuGetIndex();

        Assert.Equal("11.0.0", await sources.LatestNuGet("Some.Package"));
    }

    [Fact]
    public async Task LatestNuGet_WithARange_TakesTheHighestStableVersionInIt()
    {
        NuGetIndex();

        Assert.Equal("10.0.15", await sources.LatestNuGet("Some.Package", "10.*"));
    }

    [Fact]
    public async Task LatestNuGet_RangeWithoutAStableVersion_ReturnsNull()
    {
        NuGetIndex();

        Assert.Null(await sources.LatestNuGet("Some.Package", "12.*"));
    }

    [Fact]
    public async Task LatestNuGet_RangeDoesNotMatchAHigherMajorThatStartsWithTheSameDigits()
    {
        http.Setup(h => h.GetJson("https://api.nuget.org/v3-flatcontainer/some.package/index.json", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonNode.Parse("""{ "versions": [ "1.2.0", "10.0.0", "100.0.0" ] }""")!);

        Assert.Equal("1.2.0", await sources.LatestNuGet("Some.Package", "1.*"));
    }
}
