namespace BlueHeighliner.Bundler.Tests;

public sealed class AptBuilderTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IPaths> paths = new();
    private readonly Mock<IProcessRunner> runner = new();
    private readonly Mock<IDebIndex> index = new();
    private readonly Mock<ITargetSystem> target = new();
    private readonly List<string> downloads = [];
    private readonly List<IReadOnlyList<string>> simulations = [];
    private readonly AptBuilder builder;
    private ProcessResult simulation = new(0, "Inst gimp (3.2-1ubuntu0.1 Ubuntu:24.04/noble-updates [amd64])\nInst libfoo [1.0-1] (2.0-1ubuntu0.1 Ubuntu:24.04/noble-updates [amd64])\nInst libbar (1.0-1 Ubuntu:24.04/noble [amd64])\n", string.Empty);

    public AptBuilderTests()
    {
        paths.SetupGet(p => p.Cache).Returns(directory.Combine("cache"));
        index.Setup(i => i.Field(It.IsAny<string>(), It.IsAny<string>())).Returns((string paragraph, string field) =>
            paragraph.Split('\n').Where(line => line.StartsWith(field + ": ", StringComparison.Ordinal)).Select(line => line[(field.Length + 2)..]).FirstOrDefault() ?? string.Empty);
        index.Setup(i => i.Write(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() =>
            (IReadOnlyList<DebPackage>)[.. downloads.Select(item => item.Split('=')).Select(parts => new DebPackage(parts[0], parts.ElementAtOrDefault(1) ?? "1", $"{parts[0]}.deb"))]);
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ProcessCommand command, CancellationToken _) => Answer(command));
        builder = new(paths.Object, runner.Object, Mock.Of<IHttpFetcher>(), index.Object, new Versions(), target.Object, Mock.Of<ILog>());
    }

    public void Dispose()
        => directory.Dispose();

    private ProcessResult Answer(ProcessCommand command)
    {
        if (command.FileName == "apt-get" && command.Arguments.Contains("-s"))
        {
            simulations.Add(command.Arguments);
            return simulation;
        }

        if (command.FileName == "apt-get" && command.Arguments.Contains("download"))
        {
            downloads.Add(command.Arguments[^1]);
        }

        return new ProcessResult(0, string.Empty, string.Empty);
    }

    [Fact]
    public async Task Download_FetchesEveryPackageOfThePlanAtThePlannedVersion()
    {
        await builder.Download(directory.Combine("debs"), new() { Services = ["gimp"] });

        Assert.Equal(["gimp=3.2-1ubuntu0.1", "libfoo=2.0-1ubuntu0.1", "libbar=1.0-1"], downloads);
    }

    [Fact]
    public async Task Download_AnUpgradeOfAPackageTheTargetHasIsPartOfThePlan()
    {
        IReadOnlyList<DebPackage> packages = await builder.Download(directory.Combine("debs"), new() { Services = ["gimp"] });

        Assert.Contains(packages, package => package.Package == "libfoo" && package.Version == "2.0-1ubuntu0.1");
    }

    [Fact]
    public async Task Download_ServiceTheTargetHasAlready_IsStillBundled()
    {
        simulation = new(0, "Inst libbar (1.0-1 Ubuntu:24.04/noble [amd64])\n", string.Empty);

        await builder.Download(directory.Combine("debs"), new() { Services = ["gimp"] });

        Assert.Equal(["libbar=1.0-1", "gimp"], downloads);
    }

    [Fact]
    public async Task Download_NothingToAddAndNoService_WritesAnEmptyRepository()
    {
        simulation = new(0, string.Empty, string.Empty);

        IReadOnlyList<DebPackage> packages = await builder.Download(directory.Combine("debs"), new() { Support = ["zlib1g"] });

        Assert.Empty(downloads);
        Assert.Empty(packages);
    }

    [Fact]
    public async Task Download_SupportPackageTheTargetHas_IsNotAskedFor()
    {
        target.Setup(t => t.IsInstalled("zlib1g")).Returns(true);

        await builder.Download(directory.Combine("debs"), new() { Services = ["gimp"], Support = ["zlib1g", "libbaz"] });

        IReadOnlyList<string> arguments = Assert.Single(simulations);
        Assert.Contains("libbaz", arguments);
        Assert.DoesNotContain("zlib1g", arguments);
        Assert.Equal(["gimp", "libbaz"], File.ReadAllLines(directory.Combine("debs", "top-level.txt")).Concat(File.ReadAllLines(directory.Combine("debs", "support.txt")).Where(name => name == "libbaz")));
    }

    [Fact]
    public async Task Download_PlanThatRemovesAPackage_Throws()
    {
        simulation = new(0, "Remv evince [46.0-1]\nInst gimp (3.2-1 Ubuntu:24.04/noble [amd64])\n", string.Empty);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.Download(directory.Combine("debs"), new() { Services = ["gimp"] }));

        Assert.Contains("would remove evince", error.Message);
        Assert.Empty(downloads);
    }

    [Fact]
    public async Task Download_PackagesApt_CannotResolve_Throws()
    {
        simulation = new(100, string.Empty, "E: Unable to locate package gimp");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.Download(directory.Combine("debs"), new() { Services = ["gimp"] }));

        Assert.Contains("Unable to locate package gimp", error.Message);
    }

    [Fact]
    public async Task Plan_ListsTheNamesTheInstallWouldAdd()
    {
        IReadOnlyList<string>? plan = await builder.Plan(["gimp"]);

        Assert.Equal(["gimp", "libfoo", "libbar"], plan);
        Assert.Empty(downloads);
    }

    [Fact]
    public async Task Plan_InstallThatRemovesAPackage_IsNull()
    {
        simulation = new(0, "Remv evince [46.0-1]\nInst gimp (3.2-1 Ubuntu:24.04/noble [amd64])\n", string.Empty);

        Assert.Null(await builder.Plan(["gimp"]));
    }

    [Fact]
    public async Task Plan_InstallThatCannotBeResolved_IsNull()
    {
        simulation = new(100, string.Empty, "E: broken");

        Assert.Null(await builder.Plan(["gimp"]));
    }

    [Fact]
    public async Task FindAll_ReturnsTheMatchingNamesInVersionOrder()
    {
        runner.Setup(r => r.Run(It.Is<ProcessCommand>(command => command.FileName == "apt-cache" && command.Arguments.Contains("pkgnames")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(0, "nvidia-driver-580-open\nnvidia-driver-570-open\nnvidia-driver-595\nnvidia-driver-1000-open\nother\n", string.Empty));

        Assert.Equal(["nvidia-driver-570-open", "nvidia-driver-580-open", "nvidia-driver-1000-open"], await builder.FindAll(@"nvidia-driver-[0-9]+-open"));
        Assert.Equal("nvidia-driver-1000-open", await builder.Find(@"nvidia-driver-[0-9]+-open"));
    }
}
