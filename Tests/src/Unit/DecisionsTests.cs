namespace BlueHeighliner.Bundler.Tests;

public sealed class DecisionsTests
{
    private readonly Mock<ILog> log = new();
    private readonly Decisions decisions;

    public DecisionsTests()
        => decisions = new(new Versions(), log.Object);

    [Theory]
    [InlineData(null, "2.0", true, "not on this computer")]
    [InlineData("1.0", "2.0", true, "replacing 1.0 with the newer 2.0")]
    [InlineData("2.0", "2.0", false, "already installed, skipped")]
    [InlineData("3.0", "2.0", false, "newer than the 2.0 in the bundle, kept")]
    public void Install_DecidesAndLogs(string? have, string bundle, bool installs, string logged)
    {
        Assert.Equal(installs, decisions.Install("git", have, bundle));

        log.Verify(l => l.Write(LogLevel.Info, It.Is<string>(text => text.Contains(logged))), Times.Once);
    }
}
