namespace BlueHeighliner.Bundler.Tests;

public sealed class LibreOfficeBundleTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IAptBuilder> apt = new();
    private readonly Mock<IAptInstaller> installer = new();
    private readonly Mock<ILog> log = new();
    private readonly LibreOfficeBundle bundle;
    private AptRequest? request;

    public LibreOfficeBundleTests()
    {
        apt.Setup(a => a.Download(It.IsAny<string>(), It.IsAny<AptRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((string _, AptRequest given, CancellationToken _) =>
        {
            request = given;
            return (IReadOnlyList<DebPackage>)[.. given.Services.Concat(given.Support).Concat(["libreoffice-core"]).Select(name => new DebPackage(name, "4:24.2.7-0ubuntu0.24.04.7", name + ".deb"))];
        });
        bundle = new(apt.Object, installer.Object, log.Object);
    }

    public void Dispose()
        => directory.Dispose();

    [Fact]
    public void Bundle_IsCalledLibreOfficeAndNeedsNoOtherBundle()
    {
        Assert.Equal("libre-office", bundle.Name);
        Assert.Empty(bundle.Requires);
        Assert.Equal("Ubuntu 24.04", bundle.Target);
    }

    [Fact]
    public async Task Build_AsksForAllSixToolsAndTheirOptionalParts()
    {
        await bundle.Build(directory.Combine("staging"));

        Assert.NotNull(request);
        Assert.Equal(["libreoffice-writer", "libreoffice-calc", "libreoffice-impress", "libreoffice-draw", "libreoffice-math", "libreoffice-base"], request.Services);
        Assert.Equal(["libreoffice-sdbc-firebird", "libreoffice-script-provider-python", "libreoffice-lightproof-en"], request.Support);
        apt.Verify(a => a.Prepare(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Build_ReportsTheVersionOfEveryToolAndNothingElse()
    {
        BuildResult result = await bundle.Build(directory.Combine("staging"));

        Assert.Equal(6, result.Components.Count);
        Assert.All(result.Components, component => Assert.EndsWith(" 4:24.2.7-0ubuntu0.24.04.7", component));
        Assert.Contains("libreoffice-base 4:24.2.7-0ubuntu0.24.04.7", result.Components);
        Assert.DoesNotContain(result.Components, component => component.StartsWith("libreoffice-core", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_InstallsThePackagesAndReportsEveryToolsVersion()
    {
        installer.Setup(i => i.InstalledVersion(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("4:24.2.7-0ubuntu0.24.04.7");
        BundleState state = new(directory.Combine("state"));

        await bundle.Install(new() { Payload = directory.Combine("payload"), State = state, Users = [], Info = new() { Name = "libre-office" } });

        installer.Verify(i => i.InstallRecorded(Path.Combine(directory.Combine("payload"), "debs"), state, It.IsAny<CancellationToken>()), Times.Once);
        foreach (string tool in new[] { "writer", "calc", "impress", "draw", "math", "base" })
        {
            log.Verify(l => l.Write(LogLevel.Info, $"libreoffice-{tool} 4:24.2.7-0ubuntu0.24.04.7"), Times.Once);
        }
    }

    [Fact]
    public void Script_RemovesOnlyWhatTheBundleAdded()
    {
        BundleState state = new(directory.Combine("state"));

        IReadOnlyList<string> lines = bundle.Script(new() { State = state, Users = [] }).Lines();

        Assert.Equal(["apt-remove-added"], lines);
    }
}
