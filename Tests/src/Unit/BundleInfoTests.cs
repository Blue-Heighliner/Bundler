namespace BlueHeighliner.Bundler.Tests;

public sealed class BundleInfoTests
{
    [Fact]
    public void Format_ThenParse_RoundTrips()
    {
        BundleInfo info = new() { Name = "git", Built = "2026-01-01", BuiltOn = "Ubuntu", Components = ["git 2.56.0", "gitk 2.56.0"] };

        BundleInfo? parsed = BundleInfo.Parse(info.Format());

        Assert.NotNull(parsed);
        Assert.Equal("git", parsed.Name);
        Assert.Equal("2026-01-01", parsed.Built);
        Assert.Equal(info.Components, parsed.Components);
        Assert.DoesNotContain("version", info.Format());
    }

    [Fact]
    public void Parse_FileThatStillHasRevisionAndVersionLines_StillReads()
        => Assert.Equal("git", BundleInfo.Parse("name=git\nrevision=3\nversion=1\n")!.Name);

    [Fact]
    public void Parse_WithoutName_ReturnsNull()
        => Assert.Null(BundleInfo.Parse("built=today\n"));
}
