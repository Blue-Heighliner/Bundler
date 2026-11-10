namespace BlueHeighliner.Bundler.Tests;

public sealed class AptInstallerTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IProcessRunner> runner = new();
    private readonly HashSet<string> installed = [];
    private readonly HashSet<string> removable = [];
    private readonly List<string[]> removed = [];
    private readonly AptInstaller installer;

    public AptInstallerTests()
    {
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ProcessCommand command, CancellationToken _) =>
        {
            switch (command.FileName)
            {
                case "dpkg-query":
                    return new ProcessResult(0, installed.Contains(command.Arguments[^1]) ? "installed 1.0\n" : "not-installed \n", string.Empty);
                case "apt-get" when command.Arguments[0] == "-s":
                    return new ProcessResult(0, string.Concat(removable.Where(installed.Contains).Select(name => $"Remv {name} [1.0]\n")), string.Empty);
                case "apt-get" when command.Arguments[0] == "remove":
                    string[] names = [.. command.Arguments.Skip(3)];
                    removed.Add(names);
                    installed.ExceptWith(names);
                    return new ProcessResult(0, string.Empty, string.Empty);
                default:
                    return new ProcessResult(0, string.Empty, string.Empty);
            }
        });
        installer = new(runner.Object, Mock.Of<IDebIndex>(), new Versions(), Mock.Of<ILog>());
    }

    private string Leftover => directory.Combine("apt-leftover");

    private BundleState State(string bundle)
        => new(directory.Combine(bundle));

    public void Dispose()
        => directory.Dispose();

    [Fact]
    public async Task RemoveAdded_RemovesWhatNothingElseNeeds()
    {
        installed.UnionWith(["liba", "libb"]);
        removable.UnionWith(["liba", "libb"]);
        BundleState state = State("blender");
        state.Add("apt-new", "liba");
        state.Add("apt-new", "libb");

        await installer.RemoveAdded(state);

        Assert.Equal(["liba", "libb"], Assert.Single(removed));
        Assert.Empty(installed);
        Assert.False(File.Exists(Leftover));
    }

    [Fact]
    public async Task RemoveAdded_KeepsWhatAnotherBundleStillNeedsAndRemembersIt()
    {
        installed.UnionWith(["liba", "libb"]);
        removable.Add("liba");
        BundleState state = State("blender");
        state.Add("apt-new", "liba");
        state.Add("apt-new", "libb");

        await installer.RemoveAdded(state);

        Assert.Equal(["liba"], Assert.Single(removed));
        Assert.Equal(["libb"], File.ReadAllLines(Leftover));
    }

    [Fact]
    public async Task RemoveAdded_LaterUninstall_RemovesTheLeftoversOfAnEarlierOne()
    {
        installed.UnionWith(["libb", "gimp"]);
        BundleState first = State("blender");
        first.Add("apt-new", "libb");
        BundleState second = State("gimp");
        second.Add("apt-new", "gimp");

        await installer.RemoveAdded(first);
        Assert.Equal(["libb"], File.ReadAllLines(Leftover));

        removable.UnionWith(["libb", "gimp"]);
        await installer.RemoveAdded(second);

        Assert.Empty(installed);
        Assert.False(File.Exists(Leftover));
    }

    [Fact]
    public async Task RemoveAdded_NothingRecorded_DoesNothing()
    {
        await installer.RemoveAdded(State("git"));

        Assert.Empty(removed);
        Assert.False(File.Exists(Leftover));
    }

    [Fact]
    public async Task RemoveInstalled_RemovesTheInstalledPackagesThatMatchAndNothingElse()
    {
        Mock<IProcessRunner> own = new();
        List<string[]> removals = [];
        own.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ProcessCommand command, CancellationToken _) =>
        {
            if (command.FileName == "dpkg-query")
            {
                return new ProcessResult(0, "installed\tnvidia-driver-580-open\ninstalled\tlibnvidia-gl-580:amd64\nconfig-files\tnvidia-utils-580\ninstalled\tnvidia-driver-615-open\ninstalled\tgit\n", string.Empty);
            }

            removals.Add([.. command.Arguments]);
            return new ProcessResult(0, string.Empty, string.Empty);
        });
        AptInstaller own1 = new(own.Object, Mock.Of<IDebIndex>(), new Versions(), Mock.Of<ILog>());

        IReadOnlyList<string> removed = await own1.RemoveInstalled(@"(?:lib)?nvidia-[a-z0-9-]*-580(?:-.*)?");

        Assert.Equal(["libnvidia-gl-580:amd64", "nvidia-driver-580-open"], removed);
        Assert.Equal(["remove", "-y", "--purge", "libnvidia-gl-580:amd64", "nvidia-driver-580-open"], Assert.Single(removals));
    }

    [Fact]
    public async Task RemoveInstalled_NothingMatches_RunsNoRemoval()
    {
        Mock<IProcessRunner> own = new();
        own.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProcessResult(0, "installed\tgit\n", string.Empty));
        AptInstaller own1 = new(own.Object, Mock.Of<IDebIndex>(), new Versions(), Mock.Of<ILog>());

        Assert.Empty(await own1.RemoveInstalled("nvidia-.*"));

        own.Verify(r => r.Run(It.Is<ProcessCommand>(command => command.FileName == "apt-get"), It.IsAny<CancellationToken>()), Times.Never);
    }
}
