namespace BlueHeighliner.Bundler.Tests;

public sealed class PythonAndNodeBundleTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IReleaseSources> sources = new();
    private readonly Mock<IDownloads> downloads = new();
    private readonly Mock<IToolInstaller> tool = new();
    private readonly Mock<IProcessRunner> runner = new();
    private readonly Mock<ILog> log = new();
    private readonly PythonBundle python;
    private readonly NodeBundle node;
    private readonly BundleState state;
    private Dictionary<string, string>? commands;
    private string? installedVersion;

    public PythonAndNodeBundleTests()
    {
        state = new(directory.Combine("state"));
        sources.Setup(s => s.LatestPython(It.IsAny<CancellationToken>())).ReturnsAsync(new ArchiveRelease { Version = "3.15.2", Url = "https://example.test/py.tgz", FileName = "cpython-3.15.2+1-x86_64-unknown-linux-gnu-install_only.tar.gz", Hash = new("sha256", "aa") });
        sources.Setup(s => s.LatestNode(It.IsAny<CancellationToken>())).ReturnsAsync(new ArchiveRelease { Version = "26.13.1", Url = "https://example.test/node.txz", FileName = "node-v26.13.1-linux-x64.tar.xz", Hash = new("sha256", "bb") });
        downloads.Setup(d => d.Fetch(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<HashSpec?>(), It.IsAny<CancellationToken>())).ReturnsAsync((string _, string name, HashSpec? _, CancellationToken _) => "/cache/" + name);
        tool.Setup(t => t.InstalledVersion(It.IsAny<string>())).Returns(() => installedVersion);
        tool.Setup(t => t.Install(It.IsAny<IBundleState>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback((IBundleState _, string _, string _, string _, IReadOnlyDictionary<string, string> given, CancellationToken _) => commands = new(given)).Returns(Task.CompletedTask);
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProcessResult(0, "Python 3.15.2\n", string.Empty));
        python = new(sources.Object, downloads.Object, tool.Object, new Decisions(new Versions(), log.Object), new Versions(), runner.Object, log.Object);
        node = new(sources.Object, downloads.Object, tool.Object, new Decisions(new Versions(), log.Object), new Versions(), runner.Object, log.Object);
    }

    public void Dispose()
        => directory.Dispose();

    private InstallContext Install(string name, string component, string folder, string file)
    {
        directory.Write($"payload/{folder}/{file}", "archive");
        return new() { Payload = directory.Combine("payload"), State = state, Users = [], Info = new() { Name = name, Components = [component] } };
    }

    [Fact]
    public async Task Python_BuildTakesTheNewestStableArchiveAndNamesItsVersion()
    {
        BuildResult result = await python.Build(directory.Combine("staging"));

        Assert.Equal(["Python 3.15.2 (linux x64, python-build-standalone)"], result.Components);
        downloads.Verify(d => d.Fetch("https://example.test/py.tgz", "cpython-3.15.2+1-x86_64-unknown-linux-gnu-install_only.tar.gz", new HashSpec("sha256", "aa"), It.IsAny<CancellationToken>()), Times.Once);
        downloads.Verify(d => d.Place("/cache/cpython-3.15.2+1-x86_64-unknown-linux-gnu-install_only.tar.gz", directory.Combine("staging", "python", "cpython-3.15.2+1-x86_64-unknown-linux-gnu-install_only.tar.gz"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Python_InstallLinksPythonPipAndTheVersionedCommands_NotPython3()
    {
        await python.Install(Install("python", "Python 3.15.2 (linux x64, python-build-standalone)", "python", "cpython-3.15.2+1-x86_64-unknown-linux-gnu-install_only.tar.gz"));

        tool.Verify(t => t.Install(state, It.Is<string>(file => file.EndsWith("install_only.tar.gz", StringComparison.Ordinal)), "/opt/python", "3.15.2", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(["python", "python3.15", "pip", "pip3.15"], commands!.Keys);
        Assert.Equal("bin/python3.15", commands["python3.15"]);
        Assert.Equal("bin/pip3", commands["pip"]);
        Assert.DoesNotContain("python3", commands.Keys);
    }

    [Fact]
    public async Task Python_ANewerPythonThatIsThereAlready_IsKept()
    {
        installedVersion = "3.16.0";

        await python.Install(Install("python", "Python 3.15.2 (linux x64, python-build-standalone)", "python", "cpython-3.15.2+1-x86_64-unknown-linux-gnu-install_only.tar.gz"));

        tool.Verify(t => t.Install(It.IsAny<IBundleState>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Python_TheSameVersionThatIsThereAlready_IsSkipped()
    {
        installedVersion = "3.15.2";

        await python.Install(Install("python", "Python 3.15.2 (linux x64, python-build-standalone)", "python", "cpython-3.15.2+1-x86_64-unknown-linux-gnu-install_only.tar.gz"));

        tool.Verify(t => t.Install(It.IsAny<IBundleState>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Python_InstallChecksThatPythonAndPipRun()
    {
        await python.Install(Install("python", "Python 3.15.2 (linux x64, python-build-standalone)", "python", "cpython-3.15.2+1-x86_64-unknown-linux-gnu-install_only.tar.gz"));

        runner.Verify(r => r.Run(It.Is<ProcessCommand>(command => command.FileName == "python" && command.Arguments.SequenceEqual(new[] { "--version" })), It.IsAny<CancellationToken>()), Times.Once);
        runner.Verify(r => r.Run(It.Is<ProcessCommand>(command => command.FileName == "python" && command.Arguments.SequenceEqual(new[] { "-m", "pip", "--version" })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Node_BuildTakesTheNewestStableArchiveAndNamesItsVersion()
    {
        BuildResult result = await node.Build(directory.Combine("staging"));

        Assert.Equal(["Node.js 26.13.1 (linux x64)"], result.Components);
        downloads.Verify(d => d.Place("/cache/node-v26.13.1-linux-x64.tar.xz", directory.Combine("staging", "node", "node-v26.13.1-linux-x64.tar.xz"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Node_InstallLinksNodeNpmAndNpx()
    {
        await node.Install(Install("node", "Node.js 26.13.1 (linux x64)", "node", "node-v26.13.1-linux-x64.tar.xz"));

        tool.Verify(t => t.Install(state, It.Is<string>(file => file.EndsWith("node-v26.13.1-linux-x64.tar.xz", StringComparison.Ordinal)), "/opt/node", "26.13.1", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(["node", "npm", "npx"], commands!.Keys);
        Assert.All(commands, pair => Assert.Equal("bin/" + pair.Key, pair.Value));
    }

    [Fact]
    public async Task Node_AnOlderNodeThatIsThereAlready_IsReplacedByTheNewVersionNextToIt()
    {
        installedVersion = "24.0.0";

        await node.Install(Install("node", "Node.js 26.13.1 (linux x64)", "node", "node-v26.13.1-linux-x64.tar.xz"));

        tool.Verify(t => t.Install(It.IsAny<IBundleState>(), It.IsAny<string>(), "/opt/node", "26.13.1", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Uninstall_RemovesWhatTheToolInstallerPutThere()
    {
        UninstallContext context = new() { State = state, Users = [] };

        await python.Uninstall(context);
        await node.Uninstall(context);

        tool.Verify(t => t.Uninstall(state), Times.Exactly(2));
    }

    [Fact]
    public async Task Uninstall_AProgramThatWasThereBefore_IsLeftInPlaceAndSaid()
    {
        await node.Uninstall(new() { State = state, Users = [] });

        log.Verify(l => l.Write(LogLevel.Info, "Node.js was already on this computer and was not installed by this bundle, so it is left in place"), Times.Once);
    }
}
