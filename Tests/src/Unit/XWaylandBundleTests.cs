namespace BlueHeighliner.Bundler.Tests;

public sealed class XWaylandBundleTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IAptBuilder> apt = new();
    private readonly Mock<IAptInstaller> installer = new();
    private readonly Mock<ILog> log = new();
    private readonly XWaylandBundle bundle;
    private AptRequest? request;

    public XWaylandBundleTests()
    {
        apt.Setup(a => a.Download(It.IsAny<string>(), It.IsAny<AptRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((string _, AptRequest given, CancellationToken _) =>
        {
            request = given;
            return (IReadOnlyList<DebPackage>)[new DebPackage("xwayland", "2:23.2.6-1ubuntu0.8", "xwayland.deb"), new DebPackage("libxfont2", "1:2.0.6-1build1", "libxfont2.deb")];
        });
        bundle = new(apt.Object, installer.Object, log.Object);
    }

    public void Dispose()
        => directory.Dispose();

    [Fact]
    public void Bundle_IsCalledXwaylandAndNeedsNoOtherBundle()
    {
        Assert.Equal("xwayland", bundle.Name);
        Assert.Empty(bundle.Requires);
    }

    [Fact]
    public async Task Build_BundlesXwaylandAsTheOnlyServiceAndReportsItsVersion()
    {
        BuildResult result = await bundle.Build(directory.Combine("staging"));

        Assert.Equal(["xwayland"], request!.Services);
        Assert.Empty(request.Support);
        Assert.Equal(["xwayland 2:23.2.6-1ubuntu0.8"], result.Components);
    }

    [Fact]
    public async Task Install_InstallsThePackagesAndReportsTheVersion()
    {
        installer.Setup(i => i.InstalledVersion("xwayland", It.IsAny<CancellationToken>())).ReturnsAsync("2:23.2.6-1ubuntu0.8");
        BundleState state = new(directory.Combine("state"));

        await bundle.Install(new() { Payload = directory.Combine("payload"), State = state, Users = [], Info = new() { Name = "xwayland" } });

        installer.Verify(i => i.InstallRecorded(Path.Combine(directory.Combine("payload"), "debs"), state, It.IsAny<CancellationToken>()), Times.Once);
        log.Verify(l => l.Write(LogLevel.Info, "xwayland 2:23.2.6-1ubuntu0.8"), Times.Once);
    }
}
