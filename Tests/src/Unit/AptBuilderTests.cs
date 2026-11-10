namespace BlueHeighliner.Bundler.Tests;

public sealed class AptBuilderTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IPaths> paths = new();
    private readonly Mock<IProcessRunner> runner = new();
    private readonly Mock<IDebIndex> index = new();
    private readonly List<string> downloads = [];
    private readonly AptBuilder builder;

    public AptBuilderTests()
    {
        paths.SetupGet(p => p.Cache).Returns(directory.Combine("cache"));
        index.Setup(i => i.Field(It.IsAny<string>(), It.IsAny<string>())).Returns((string paragraph, string field) =>
            paragraph.Split('\n').Where(line => line.StartsWith(field + ": ", StringComparison.Ordinal)).Select(line => line[(field.Length + 2)..]).FirstOrDefault() ?? string.Empty);
        index.Setup(i => i.Write(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() =>
            (IReadOnlyList<DebPackage>)[.. downloads.Select(item => item.Split('=')).Select(parts => new DebPackage(parts[0], parts[1], $"{parts[0]}_{parts[1]}.deb"))]);
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ProcessCommand command, CancellationToken _) => Answer(command));
        builder = new(paths.Object, runner.Object, Mock.Of<IHttpFetcher>(), index.Object, new Versions(), Mock.Of<ILog>());
    }

    public void Dispose()
        => directory.Dispose();

    private ProcessResult Answer(ProcessCommand command)
    {
        if (command.FileName == "apt-get" && command.Arguments.Contains("-s"))
        {
            return new ProcessResult(
                0,
                "Inst gimp (3.2-1ubuntu0.1 Ubuntu:24.04/noble-updates [amd64])\nInst libfoo (2.0-1ubuntu0.1 Ubuntu:24.04/noble-updates [amd64])\nInst libbar (1.0-1 Ubuntu:24.04/noble [amd64])\nInst zlib1g (1:1.3-1ubuntu0.1 Ubuntu:24.04/noble-updates [amd64])\n",
                string.Empty);
        }

        if (command.FileName == "apt-get" && command.Arguments.Contains("download"))
        {
            downloads.Add(command.Arguments[^1]);
        }

        if (command.FileName == "apt-cache" && command.Arguments.Contains("show"))
        {
            return new ProcessResult(0, "Package: zlib1g\nPriority: required\n\nPackage: gimp\nPriority: optional\n\nPackage: libfoo\nPriority: optional\n", string.Empty);
        }

        if (command.FileName == "apt-cache" && command.Arguments.Contains("madison"))
        {
            return new ProcessResult(
                0,
                string.Join(
                    '\n',
                    " gimp | 3.2-1ubuntu0.1 | http://archive.ubuntu.com/ubuntu noble-updates/main amd64 Packages",
                    " gimp | 3.2-1 | http://archive.ubuntu.com/ubuntu noble/main amd64 Packages",
                    " libfoo | 2.0-1ubuntu0.1 | http://archive.ubuntu.com/ubuntu noble-updates/main amd64 Packages",
                    " libfoo | 2.0-1 | http://archive.ubuntu.com/ubuntu noble/main amd64 Packages",
                    " libbar | 1.0-1 | http://archive.ubuntu.com/ubuntu noble/main amd64 Packages",
                    " zlib1g | 1:1.3-1ubuntu0.1 | http://archive.ubuntu.com/ubuntu noble-updates/main amd64 Packages",
                    " zlib1g | 1:1.3-1 | http://archive.ubuntu.com/ubuntu noble/main amd64 Packages") + "\n",
                string.Empty);
        }

        return new ProcessResult(0, string.Empty, string.Empty);
    }

    [Fact]
    public async Task Download_AlsoBundlesTheReleaseVersionOfADependencyTakenFromUpdates()
    {
        await builder.Download(directory.Combine("debs"), new() { Services = ["gimp"] });

        Assert.Contains("libfoo=2.0-1ubuntu0.1", downloads);
        Assert.Contains("libfoo=2.0-1", downloads);
    }

    [Fact]
    public async Task Download_ServicesAndPackagesAlreadyAtTheReleaseVersionGetNoSecondVersion()
    {
        await builder.Download(directory.Combine("debs"), new() { Services = ["gimp"] });

        Assert.Equal(["gimp=3.2-1ubuntu0.1"], downloads.Where(item => item.StartsWith("gimp=", StringComparison.Ordinal)));
        Assert.Equal(["libbar=1.0-1"], downloads.Where(item => item.StartsWith("libbar=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Download_ABasePackageIsBundledOnlyAtTheNewVersion()
    {
        await builder.Download(directory.Combine("debs"), new() { Services = ["gimp"] });

        Assert.Equal(["zlib1g=1:1.3-1ubuntu0.1"], downloads.Where(item => item.StartsWith("zlib1g=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Download_ReturnsOnlyTheVersionsOfThePlan()
    {
        IReadOnlyList<DebPackage> packages = await builder.Download(directory.Combine("debs"), new() { Services = ["gimp"] });

        Assert.DoesNotContain(packages, package => package.Package == "libfoo" && package.Version == "2.0-1");
        Assert.Contains(packages, package => package.Package == "libfoo" && package.Version == "2.0-1ubuntu0.1");
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
