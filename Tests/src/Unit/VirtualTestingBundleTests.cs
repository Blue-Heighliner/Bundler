namespace BlueHeighliner.Bundler.Tests;

public sealed class VirtualTestingBundleTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IAptBuilder> apt = new();
    private readonly Mock<IAptInstaller> installer = new();
    private readonly Mock<ILog> log = new();
    private readonly VirtualTestingBundle bundle;
    private AptRequest? request;

    public VirtualTestingBundleTests()
    {
        apt.Setup(a => a.Download(It.IsAny<string>(), It.IsAny<AptRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((string _, AptRequest given, CancellationToken _) =>
        {
            request = given;
            return (IReadOnlyList<DebPackage>)[.. given.Services.Concat(["libwlroots12t64"]).Select(name => new DebPackage(name, "1.0-1", name + ".deb"))];
        });
        bundle = new(apt.Object, installer.Object, log.Object);
    }

    public void Dispose()
        => directory.Dispose();

    [Fact]
    public void Bundle_IsCalledVirtualTestingAndNeedsNoOtherBundle()
    {
        Assert.Equal("virtual-testing", bundle.Name);
        Assert.Empty(bundle.Requires);
    }

    [Fact]
    public async Task Build_AsksForEveryTestingPackage()
    {
        await bundle.Build(directory.Combine("staging"));

        Assert.Equal(["xvfb", "xauth", "xdotool", "scrot", "openbox", "cage", "wtype", "wlrctl", "grim", "imagemagick"], request!.Services);
        Assert.Empty(request.Support);
    }

    [Fact]
    public async Task Build_ReportsEveryPackageAndNotTheLibrariesThatCameWithThem()
    {
        BuildResult result = await bundle.Build(directory.Combine("staging"));

        Assert.Equal(10, result.Components.Count);
        Assert.Contains("wlrctl 1.0-1", result.Components);
        Assert.DoesNotContain(result.Components, component => component.StartsWith("libwlroots", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_ReportsTheVersionOfEveryPackage()
    {
        installer.Setup(i => i.InstalledVersion(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("1.0-1");
        BundleState state = new(directory.Combine("state"));

        await bundle.Install(new() { Payload = directory.Combine("payload"), State = state, Users = [], Info = new() { Name = "virtual-testing" } });

        foreach (string package in new[] { "xvfb", "xauth", "xdotool", "scrot", "openbox", "cage", "wtype", "wlrctl", "grim", "imagemagick" })
        {
            log.Verify(l => l.Write(LogLevel.Info, $"{package} 1.0-1"), Times.Once);
        }
    }
}
