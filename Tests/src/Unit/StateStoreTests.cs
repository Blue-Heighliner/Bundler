namespace BlueHeighliner.Bundler.Tests;

public sealed class StateStoreTests
{
    private static IBundleState Create(TempDirectory directory)
    {
        Mock<IPaths> paths = new();
        paths.SetupGet(p => p.State).Returns(directory.Path);
        return new StateStore(paths.Object).Get("git");
    }

    [Fact]
    public void NewState_IsNotInstalled()
    {
        using TempDirectory directory = new();

        IBundleState state = Create(directory);

        Assert.False(state.IsInstalled);
        Assert.Empty(state.Users);
    }

    [Fact]
    public void Begin_RecordsVersionAndUsers()
    {
        using TempDirectory directory = new();
        IBundleState state = Create(directory);

        state.Begin(["alice", "bob"]);

        Assert.True(state.IsInstalled);
        Assert.Equal(["alice", "bob"], state.Users);
    }

    [Fact]
    public void End_ForgetsEverything()
    {
        using TempDirectory directory = new();
        IBundleState state = Create(directory);
        state.Begin([]);
        state.MarkOwned("tool");

        state.End();

        Assert.False(state.IsInstalled);
        Assert.False(state.IsOwned("tool"));
    }

    [Fact]
    public void MarkOwned_IsRememberedPerKey()
    {
        using TempDirectory directory = new();
        IBundleState state = Create(directory);

        state.MarkOwned("tool");

        Assert.True(state.IsOwned("tool"));
        Assert.False(state.IsOwned("other"));
    }

    [Fact]
    public void Add_DoesNotRepeatALine()
    {
        using TempDirectory directory = new();
        IBundleState state = Create(directory);

        state.Add("apt-new", "git");
        state.Add("apt-new", "git");
        state.Add("apt-new", "curl");

        Assert.Equal(["git", "curl"], state.Lines("apt-new"));
    }

    [Fact]
    public void WriteAndRead_RoundTripText_AndMissingFileIsNull()
    {
        using TempDirectory directory = new();
        IBundleState state = Create(directory);

        Assert.Null(state.Read("model"));
        state.Write("model", "gemma4:26b");

        Assert.Equal("gemma4:26b", state.Read("model"));
        Assert.Empty(state.Lines("nothing"));
    }
}
