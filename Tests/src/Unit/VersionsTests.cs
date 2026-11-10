namespace BlueHeighliner.Bundler.Tests;

public sealed class VersionsTests
{
    private readonly Versions versions = new();

    [Theory]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("1.2.10", "1.2.9", 1)]
    [InlineData("2.0", "10.0", -1)]
    [InlineData("1.2", "1.2.1", -1)]
    public void Compare_OrdersNumbersByValue(string left, string right, int sign)
        => Assert.Equal(sign, Math.Sign(versions.Compare(left, right)));

    [Theory]
    [InlineData("1.0-1", "1.0-2", -1)]
    [InlineData("1:1.0", "2.0", 1)]
    [InlineData("1.0~rc1", "1.0", -1)]
    [InlineData("2.56.0-0ppa1", "2.56.0-0ppa1", 0)]
    [InlineData("1.0+b1", "1.0", 1)]
    public void CompareDebian_FollowsDpkgRules(string left, string right, int sign)
        => Assert.Equal(sign, Math.Sign(versions.CompareDebian(left, right)));

    [Theory]
    [InlineData("1:2.56.0-0ppa1~ubuntu24.04.2", "2.56.0")]
    [InlineData("5.7.0+ds1-2", "5.7.0")]
    [InlineData("3.0", "3.0")]
    public void Upstream_CutsEpochAndPackaging(string version, string expected)
        => Assert.Equal(expected, versions.Upstream(version, '-', '+', '~'));

    [Theory]
    [InlineData(null, "1.0", "Install")]
    [InlineData("", "1.0", "Install")]
    [InlineData("1.0", "1.0", "Same")]
    [InlineData("1.0", "1.1", "Upgrade")]
    [InlineData("1.2", "1.1", "Newer")]
    public void Decide_PicksTheAction(string? have, string bundle, string expected)
        => Assert.Equal(Enum.Parse<VersionAction>(expected), versions.Decide(have, bundle));

    [Theory]
    [InlineData("git version 2.56.0", "2.56.0")]
    [InlineData("Docker 28.1 build", "28.1")]
    [InlineData("no number", "")]
    public void Find_ReturnsTheFirstVersion(string text, string expected)
        => Assert.Equal(expected, versions.Find(text));
}
