namespace BlueHeighliner.Bundler.Tests;

public sealed class OpenCodeBundleTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IReleaseSources> sources = new();
    private readonly Mock<IDownloads> downloads = new();
    private readonly Mock<IHttpFetcher> http = new();
    private readonly Mock<IMachine> machine = new();
    private readonly Mock<IProcessRunner> runner = new();
    private readonly Mock<IOpenCodeFiles> files = new();
    private readonly Mock<ILog> log = new();
    private readonly OpenCodeBundle bundle;
    private readonly string payload;
    private readonly BundleState state;

    public OpenCodeBundleTests()
    {
        payload = directory.Combine("payload");
        directory.Write("archive/opencode", "opencode program");
        directory.Write("payload/LICENSE", "opencode license");
        using (FileStream file = File.Create(directory.Combine("payload", "opencode-linux-x64-baseline.tar.gz")))
        using (GZipStream gzip = new(file, CompressionLevel.Fastest))
        {
            TarFile.CreateFromDirectory(directory.Combine("archive"), gzip, includeBaseDirectory: false);
        }

        state = new(directory.Combine("state"));
        files.SetupGet(f => f.Root).Returns(directory.Combine("opt"));
        files.SetupGet(f => f.Binary).Returns(directory.Combine("opt", "opencode"));
        files.SetupGet(f => f.Link).Returns(directory.Combine("bin", "opencode"));
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProcessResult(0, string.Empty, string.Empty));

        bundle = new(sources.Object, downloads.Object, http.Object, new Decisions(new Versions(), log.Object), new Versions(), machine.Object, runner.Object, files.Object, log.Object);
    }

    public void Dispose()
        => directory.Dispose();

    private InstallContext Install()
        => new() { Payload = payload, State = state, Users = [], Info = new() { Name = "open-code", Components = ["OpenCode 2.2.5 (linux x64)"] } };

    private void OpenCodeIs(string output)
    {
        string existing = directory.Write("elsewhere/opencode", "theirs");
        machine.Setup(m => m.FindExecutable("opencode")).Returns(existing);
        runner.Setup(r => r.Run(It.Is<ProcessCommand>(command => command.FileName == existing), It.IsAny<CancellationToken>())).ReturnsAsync(new ProcessResult(0, output, string.Empty));
    }

    [Fact]
    public void Describes_ItselfWithTheLinuxTargetAndTheNeedForGit()
    {
        Assert.Equal("open-code", bundle.Name);
        Assert.Equal("Ubuntu 26.04", bundle.Target);
        Assert.Equal(["git"], bundle.Requires);
        Assert.Equal(35, bundle.Order);
    }

    [Fact]
    public async Task Build_TakesTheNewestVersion2ReleaseAndItsLicense()
    {
        sources.Setup(s => s.NewestGitHub("anomalyco/opencode", @"^v(2\.\d+\.\d+)$", It.Is<IReadOnlyList<string>>(patterns => patterns.Single() == @"^opencode-linux-x64-baseline\.tar\.gz$"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitHubRelease { Tag = "v2.3.0", Assets = [new() { FileName = "opencode-linux-x64-baseline.tar.gz", Url = "https://example.com/oc.tar.gz", Hash = new("sha256", "aa") }] });
        downloads.Setup(d => d.Fetch("https://example.com/oc.tar.gz", "opencode-linux-x64-baseline.tar.gz", It.IsAny<HashSpec?>(), It.IsAny<CancellationToken>())).ReturnsAsync("/cache/oc.tar.gz");

        BuildResult result = await bundle.Build(directory.Combine("staging"));

        Assert.Equal(["OpenCode 2.3.0 (linux x64)"], result.Components);
        downloads.Verify(d => d.Place("/cache/oc.tar.gz", directory.Combine("staging", "opencode-linux-x64-baseline.tar.gz"), It.IsAny<CancellationToken>()), Times.Once);
        http.Verify(h => h.Download("https://raw.githubusercontent.com/anomalyco/opencode/v2.3.0/LICENSE", directory.Combine("staging", "LICENSE"), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Install_PutsTheProgramWithItsLicenseInPlaceAndLinksTheCommand()
    {
        await bundle.Install(Install());

        Assert.Equal("opencode program", File.ReadAllText(files.Object.Binary));
        Assert.True(File.GetUnixFileMode(files.Object.Binary).HasFlag(UnixFileMode.OtherExecute));
        Assert.Equal("opencode license", File.ReadAllText(Path.Combine(files.Object.Root, "LICENSE")));
        Assert.Equal(files.Object.Binary, new FileInfo(files.Object.Link).LinkTarget);
        Assert.True(state.IsOwned("opencode"));
    }

    [Fact]
    public async Task Install_OpenCodeAtTheSameOrANewerVersion_IsKept()
    {
        OpenCodeIs("opencode v2.2.5");

        await bundle.Install(Install());

        Assert.False(File.Exists(files.Object.Binary));
        Assert.False(state.IsOwned("opencode"));
    }

    [Fact]
    public async Task Install_OlderOpenCode_IsReplaced()
    {
        OpenCodeIs("2.1.0");

        await bundle.Install(Install());

        Assert.Equal("opencode program", File.ReadAllText(files.Object.Binary));
        Assert.True(state.IsOwned("opencode"));
    }

    [Fact]
    public async Task Install_OpenCodeThatSaysNoVersion_IsReplaced()
    {
        OpenCodeIs("garbled");

        await bundle.Install(Install());

        Assert.Equal("opencode program", File.ReadAllText(files.Object.Binary));
    }

    [Fact]
    public async Task Uninstall_RemovesTheProgramAndTheLink()
    {
        await bundle.Install(Install());

        await bundle.Uninstall(new() { State = state, Users = [] });

        Assert.False(Directory.Exists(files.Object.Root));
        Assert.False(File.Exists(files.Object.Link) || new FileInfo(files.Object.Link).LinkTarget is not null);
    }

    [Fact]
    public async Task Uninstall_ALinkThatPointsElsewhere_IsLeftAlone()
    {
        await bundle.Install(Install());
        File.Delete(files.Object.Link);
        File.CreateSymbolicLink(files.Object.Link, "/somewhere/else");

        await bundle.Uninstall(new() { State = state, Users = [] });

        Assert.Equal("/somewhere/else", new FileInfo(files.Object.Link).LinkTarget);
    }

    [Fact]
    public async Task Uninstall_OpenCodeThatWasThereBefore_IsLeftInPlace()
    {
        OpenCodeIs("opencode v2.9.0");
        directory.Write("opt/opencode", "theirs");
        await bundle.Install(Install());

        await bundle.Uninstall(new() { State = state, Users = [] });

        Assert.Equal("theirs", File.ReadAllText(files.Object.Binary));
    }
}
