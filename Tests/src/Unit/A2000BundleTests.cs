namespace BlueHeighliner.Bundler.Tests;

public sealed class A2000BundleTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IReleaseSources> sources = new();
    private readonly Mock<IAptBuilder> apt = new();
    private readonly List<string> requested = [];
    private readonly A2000Bundle bundle;

    public A2000BundleTests()
    {
        apt.Setup(a => a.FindAll(@"nvidia-driver-[0-9]+-open", It.IsAny<CancellationToken>())).ReturnsAsync(["nvidia-driver-570-open", "nvidia-driver-580-open", "nvidia-driver-595-open"]);
        apt.Setup(a => a.Exists(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        apt.Setup(a => a.Download(It.IsAny<string>(), It.IsAny<AptRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((string _, AptRequest request, CancellationToken _) =>
        {
            requested.AddRange(request.Services);
            return (IReadOnlyList<DebPackage>)[.. request.Services.Select(name => new DebPackage(name, name.StartsWith("nvidia-driver", StringComparison.Ordinal) ? "595.99.02-0ubuntu0.24.04.1" : "6.8.0-149.149", name + ".deb"))];
        });
        Directory.CreateDirectory(directory.Combine("staging"));
        bundle = new(sources.Object, apt.Object, Mock.Of<IAptInstaller>(), Mock.Of<IDecisions>(), Mock.Of<IProcessRunner>(), new Versions(), Mock.Of<ILog>());
    }

    public void Dispose()
        => directory.Dispose();

    [Fact]
    public async Task Build_TakesTheNewestSeriesUbuntuPackagesWhenNvidiaIsAhead()
    {
        sources.Setup(s => s.LatestNvidiaDriver(It.IsAny<CancellationToken>())).ReturnsAsync("620.70.06");

        BuildResult result = await bundle.Build(directory.Combine("staging"));

        Assert.Equal(["nvidia-driver-595-open", "linux-modules-nvidia-595-open-generic"], requested);
        Assert.Equal("595.99.02", File.ReadAllText(directory.Combine("staging", "version.txt")));
        Assert.StartsWith("NVIDIA driver 595.99.02", result.Components[0]);
    }

    [Fact]
    public async Task Build_NeverTakesASeriesNewerThanNvidiasLatest()
    {
        sources.Setup(s => s.LatestNvidiaDriver(It.IsAny<CancellationToken>())).ReturnsAsync("585.10.01");

        await bundle.Build(directory.Combine("staging"));

        Assert.Equal("nvidia-driver-580-open", requested[0]);
    }

    [Fact]
    public async Task Build_SeriesWithoutPrebuiltModules_FallsBackToTheNextOne()
    {
        sources.Setup(s => s.LatestNvidiaDriver(It.IsAny<CancellationToken>())).ReturnsAsync("620.70.06");
        apt.Setup(a => a.Exists("linux-modules-nvidia-595-open-generic", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await bundle.Build(directory.Combine("staging"));

        Assert.Equal("nvidia-driver-580-open", requested[0]);
    }

    [Fact]
    public async Task Build_NoSeriesAtOrBelowTheLatest_Throws()
    {
        sources.Setup(s => s.LatestNvidiaDriver(It.IsAny<CancellationToken>())).ReturnsAsync("500.1.1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => bundle.Build(directory.Combine("staging")));
    }

    [Fact]
    public async Task Install_NamesTheKernelTheModulesAreFor()
    {
        directory.Write("payload/version.txt", "0.0.1");
        Mock<ILog> log = new();
        Mock<IProcessRunner> runner = new();
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProcessResult(1, string.Empty, string.Empty));
        A2000Bundle installing = new(sources.Object, apt.Object, Mock.Of<IAptInstaller>(), Mock.Of<IDecisions>(), runner.Object, new Versions(), log.Object);

        await installing.Install(new() { Payload = directory.Combine("payload"), State = new BundleState(directory.Combine("state")), Users = [], Info = new() { Name = "a2000", Components = ["NVIDIA driver 595.99.02 (nvidia-driver-595-open)", "linux-modules-nvidia-595-open-generic 6.8.0-149.149"] } });

        log.Verify(l => l.Write(LogLevel.Warning, It.Is<string>(text => text.Contains("kernel 6.8.0-149-generic") && text.Contains("boot menu"))), Times.Once);
    }

    private (A2000Bundle Bundle, Mock<IAptInstaller> Installer, Mock<IDecisions> Decisions) Installing(string installedDrivers)
    {
        Mock<IProcessRunner> runner = new();
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ProcessCommand command, CancellationToken _) =>
            new ProcessResult(command.FileName == "dpkg-query" ? 0 : 1, command.FileName == "dpkg-query" ? installedDrivers : string.Empty, string.Empty));
        Mock<IAptInstaller> installer = new();
        installer.Setup(i => i.RemoveInstalled(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(["old"]);
        Mock<IDecisions> decisions = new();
        decisions.Setup(d => d.Install(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>())).Returns(true);
        directory.Write("payload/version.txt", "615.71.09");
        return (new(sources.Object, apt.Object, installer.Object, decisions.Object, runner.Object, new Versions(), Mock.Of<ILog>()), installer, decisions);
    }

    private InstallContext Context()
        => new() { Payload = directory.Combine("payload"), State = new BundleState(directory.Combine("state")), Users = [], Info = new() { Name = "a2000", Components = [] } };

    [Fact]
    public async Task Install_AnotherDriverSeriesIsThere_RemovesThatSeriesFirst()
    {
        (A2000Bundle bundle, Mock<IAptInstaller> installer, _) = Installing("installed nvidia-driver-580-open\nconfig-files nvidia-driver-570\n");
        string? pattern = null;
        installer.Setup(i => i.RemoveInstalled(It.IsAny<string>(), It.IsAny<CancellationToken>())).Callback((string p, CancellationToken _) => pattern = p).ReturnsAsync(["nvidia-driver-580-open"]);

        await bundle.Install(Context());

        Assert.NotNull(pattern);
        Regex expression = new($"^(?:{pattern})$");
        Assert.All(new[] { "nvidia-driver-580-open", "nvidia-driver-580", "libnvidia-gl-580", "libnvidia-cfg1-580", "nvidia-kernel-source-580-open", "nvidia-firmware-580-580.178.04", "xserver-xorg-video-nvidia-580", "linux-modules-nvidia-580-open-generic", "nvidia-dkms-580-open" }, name => Assert.Matches(expression, name));
        Assert.All(new[] { "nvidia-driver-615-open", "libnvidia-gl-615", "nvidia-driver-5800", "nvidia-container-toolkit", "nvidia-settings", "git" }, name => Assert.DoesNotMatch(expression, name));
        installer.Verify(i => i.RemoveInstalled(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        installer.Verify(i => i.InstallRecorded(It.IsAny<string>(), It.IsAny<IBundleState>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Install_OnlyTheLibrariesOfAnotherSeriesAreThere_RemovesThatSeries()
    {
        (A2000Bundle bundle, Mock<IAptInstaller> installer, _) = Installing("installed nvidia-kernel-common-580\ninstalled libnvidia-gl-580\ninstalled nvidia-settings\n");

        await bundle.Install(Context());

        installer.Verify(i => i.RemoveInstalled(It.Is<string>(pattern => pattern.Contains("580")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Install_OnlyTheSameSeriesIsThere_RemovesNothing()
    {
        (A2000Bundle bundle, Mock<IAptInstaller> installer, _) = Installing("installed nvidia-driver-615-open\n");

        await bundle.Install(Context());

        installer.Verify(i => i.RemoveInstalled(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        installer.Verify(i => i.InstallRecorded(It.IsAny<string>(), It.IsAny<IBundleState>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Install_NoDriverIsThere_RemovesNothing()
    {
        (A2000Bundle bundle, Mock<IAptInstaller> installer, _) = Installing(string.Empty);

        await bundle.Install(Context());

        installer.Verify(i => i.RemoveInstalled(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Install_DriverIsKept_RemovesNothingAndInstallsNothing()
    {
        (A2000Bundle bundle, Mock<IAptInstaller> installer, Mock<IDecisions> decisions) = Installing("installed nvidia-driver-620-open\n");
        decisions.Setup(d => d.Install(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>())).Returns(false);

        await bundle.Install(Context());

        installer.Verify(i => i.RemoveInstalled(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        installer.Verify(i => i.InstallRecorded(It.IsAny<string>(), It.IsAny<IBundleState>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
