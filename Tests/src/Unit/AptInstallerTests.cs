namespace BlueHeighliner.Bundler.Tests;

public sealed class AptInstallerTests
{
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
