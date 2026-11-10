namespace BlueHeighliner.Bundler.Tests;

public sealed class VsCodeBundleTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IResources> resources = new();
    private readonly Mock<IAptInstaller> aptInstaller = new();
    private readonly Mock<IMachine> machine = new();
    private readonly Mock<IUsers> users = new();
    private readonly Mock<IUserFiles> userFiles = new();
    private readonly Mock<IProcessRunner> runner = new();
    private readonly Mock<ILog> log = new();
    private readonly List<(string User, string Path, bool Readable, bool OtherCanRead)> installs = [];
    private readonly VsCodeBundle bundle;
    private readonly BundleState state;

    public VsCodeBundleTests()
    {
        directory.Write("payload/extensions/ms-dotnettools.csharp-2.160.4.vsix", "csharp");
        directory.Write("payload/extensions/saoudrizwan.claude-dev-4.1.26.vsix", "cline");
        directory.Write("payload/extensions/extensions.tsv", "ms-dotnettools.csharp\t2.160.4\tms-dotnettools.csharp-2.160.4.vsix\nsaoudrizwan.claude-dev\t4.1.26\tsaoudrizwan.claude-dev-4.1.26.vsix\n");
        state = new(directory.Combine("state"));

        resources.Setup(r => r.Text("vs-code/vscode-settings.json")).Returns("{ \"path\": \"/usr/share/dotnet/dotnet\" }");
        resources.Setup(r => r.Text("vs-code/cline-rules/offline-dotnet.md")).Returns("rules");
        machine.Setup(m => m.FindExecutable("dotnet")).Returns("/usr/local/bin/dotnet");
        users.Setup(u => u.Home(It.IsAny<string>())).Returns((string user) => directory.Combine("home", user));
        users.Setup(u => u.RunAs(It.IsAny<string>(), false, "code", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(
            (string user, bool _, string _, IReadOnlyList<string> arguments, CancellationToken _) =>
            {
                if (arguments[0] == "--install-extension")
                {
                    string path = arguments[1];
                    installs.Add((user, path, File.Exists(path), File.GetUnixFileMode(path).HasFlag(UnixFileMode.OtherRead) && File.GetUnixFileMode(Path.GetDirectoryName(path)!).HasFlag(UnixFileMode.OtherExecute)));
                }

                return new ProcessResult(0, arguments[0] == "--list-extensions" ? "ms-dotnettools.csharp\nsaoudrizwan.claude-dev\n" : string.Empty, string.Empty);
            });
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProcessResult(0, string.Empty, string.Empty));

        bundle = new(resources.Object, Mock.Of<IReleaseSources>(), Mock.Of<IDownloads>(), Mock.Of<IMarketplace>(), Mock.Of<IAptBuilder>(), aptInstaller.Object, new Decisions(new Versions(), log.Object), machine.Object, users.Object, userFiles.Object, runner.Object, log.Object);
    }

    public void Dispose()
        => directory.Dispose();

    private InstallContext Install(params string[] accounts)
        => new() { Payload = directory.Combine("payload"), State = state, Users = accounts, Info = new() { Name = "vs-code" } };

    [Fact]
    public void Describes_ItselfWithTheBundlesItConfigures()
    {
        Assert.Equal("vs-code", bundle.Name);
        Assert.Equal(["git", "dotnet", "ollama"], bundle.Requires);
        Assert.Equal("Ubuntu 24.04", bundle.Target);
    }

    [Fact]
    public async Task Install_InstallsEachExtensionForEachUserFromACopyTheUserCanRead_AndRemovesTheCopy()
    {
        await bundle.Install(Install("alice", "bob"));

        Assert.Equal(4, installs.Count);
        Assert.All(installs, install => Assert.True(install.Readable && install.OtherCanRead));
        Assert.All(installs, install => Assert.DoesNotContain(state.Directory, install.Path));
        Assert.Equal(2, installs.Select(install => install.User).Distinct().Count());
        Assert.All(installs, install => Assert.False(Directory.Exists(Path.GetDirectoryName(install.Path))));
        Assert.Equal(["ms-dotnettools.csharp", "saoudrizwan.claude-dev"], state.Lines("extensions-owned-alice"));
    }

    [Fact]
    public async Task Install_WritesTheEditorSettingsWithTheDotNetOfThisComputer_AndTheClineSettings()
    {
        await bundle.Install(Install("alice"));

        string settingsPath = directory.Combine("home", "alice", ".config", "Code", "User", "settings.json");
        string rulesPath = directory.Combine("home", "alice", "Documents", "Cline", "Rules", "offline-dotnet.md");
        userFiles.Verify(u => u.SetJson("alice", settingsPath, It.Is<JsonObject>(settings => settings["path"]!.GetValue<string>() == "/usr/local/bin/dotnet"), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        userFiles.Verify(u => u.Write("alice", rulesPath, "rules", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Install_SetsTheGitDefaults()
    {
        await bundle.Install(Install());

        Assert.Contains(runner.Invocations.Select(call => call.Arguments[0]).OfType<ProcessCommand>(), command => command.FileName == "git" && command.Arguments.SequenceEqual(["config", "--system", "core.editor", "code --wait"]));
        log.Verify(l => l.Write(LogLevel.Warning, It.Is<string>(text => text.Contains("no normal user accounts"))), Times.Once);
    }

    [Fact]
    public async Task Uninstall_RemovesTheExtensionsTheBundleInstalled_AndTheSettings()
    {
        await bundle.Install(Install("alice"));
        Directory.CreateDirectory(directory.Combine("home", "alice"));

        await bundle.Uninstall(new() { State = state, Users = ["alice"] });

        users.Verify(u => u.RunAs("alice", false, "code", It.Is<IReadOnlyList<string>>(arguments => arguments[0] == "--uninstall-extension" && arguments[1] == "saoudrizwan.claude-dev"), It.IsAny<CancellationToken>()), Times.Once);
        users.Verify(u => u.RunAs("alice", false, "code", It.Is<IReadOnlyList<string>>(arguments => arguments[0] == "--uninstall-extension" && arguments[1] == "ms-dotnettools.csharp"), It.IsAny<CancellationToken>()), Times.Once);
        aptInstaller.Verify(a => a.RemoveAdded(state, It.IsAny<CancellationToken>()), Times.Once);
    }
}
