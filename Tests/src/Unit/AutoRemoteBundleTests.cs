namespace BlueHeighliner.Bundler.Tests;

public sealed class AutoRemoteBundleTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly AutoRemoteFiles real = new();
    private readonly Mock<IAutoRemoteFiles> files = new();
    private readonly Mock<IUsers> users = new();
    private readonly Mock<IUserFiles> userFiles = new();
    private readonly Mock<IProcessRunner> runner = new();
    private readonly Mock<ISystemd> systemd = new();
    private readonly Mock<IAptInstaller> aptInstaller = new();
    private readonly Mock<ILog> log = new();
    private readonly AutoRemoteBundle bundle;
    private readonly string payload;
    private readonly BundleState state;

    public AutoRemoteBundleTests()
    {
        payload = directory.Combine("payload");
        directory.Write("payload/version.txt", "1.2.0");
        directory.Write("payload/server/Markwardt.AutoRemote.Server", "server");
        directory.Write("payload/desktop/Markwardt.AutoRemote.DesktopClient", "client");
        directory.Write("payload/THIRD-PARTY-NOTICES.txt", "notices");
        directory.Write("payload/LICENSE", "license");
        directory.Write("payload/auto-remote.png", "icon");
        state = new(directory.Combine("state"));

        files.SetupGet(f => f.Root).Returns(directory.Combine("opt"));
        files.SetupGet(f => f.Server).Returns(directory.Combine("opt", "server", "Markwardt.AutoRemote.Server"));
        files.SetupGet(f => f.Client).Returns(directory.Combine("opt", "desktop", "Markwardt.AutoRemote.DesktopClient"));
        files.SetupGet(f => f.Icon).Returns(directory.Combine("share", "icons", "auto-remote.png"));
        files.SetupGet(f => f.DesktopEntry).Returns(directory.Combine("share", "applications", "auto-remote.desktop"));
        files.SetupGet(f => f.VersionFile).Returns(directory.Combine("opt", "version.txt"));
        files.SetupGet(f => f.UnitText).Returns("UNIT\n");
        files.SetupGet(f => f.DesktopEntryText).Returns("ENTRY\n");
        files.Setup(f => f.PortFor(It.IsAny<int>())).Returns((int position) => real.PortFor(position));
        files.Setup(f => f.ConfigText(It.IsAny<string>(), It.IsAny<int?>())).Returns((string key, int? port) => real.ConfigText(key, port));
        files.Setup(f => f.NewKey()).Returns("NEWKEY");

        users.Setup(u => u.Home(It.IsAny<string>())).Returns((string user) => directory.Combine("home", user));
        users.Setup(u => u.MakeDirectory(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns((string _, string folder, CancellationToken _) =>
        {
            Directory.CreateDirectory(folder);
            return Task.CompletedTask;
        });
        users.Setup(u => u.Own(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        userFiles.Setup(u => u.Write(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns((string _, string path, string text, CancellationToken _) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return Task.CompletedTask;
        });
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProcessResult(0, string.Empty, string.Empty));

        bundle = new(
            Mock.Of<IReleaseSources>(),
            Mock.Of<IDownloads>(),
            Mock.Of<IHttpFetcher>(),
            Mock.Of<IResources>(),
            Mock.Of<IAptBuilder>(),
            aptInstaller.Object,
            new Decisions(new Versions(), log.Object),
            userFiles.Object,
            users.Object,
            files.Object,
            runner.Object,
            systemd.Object,
            log.Object);
    }

    public void Dispose()
        => directory.Dispose();

    private InstallContext Install(params string[] accounts)
        => new() { Payload = payload, State = state, Users = accounts, Info = new() { Name = "auto-remote" } };

    private string Home(string user, params string[] parts)
        => directory.Combine(["home", user, .. parts]);

    private bool Ran(string fileName, params string[] arguments)
        => runner.Invocations.Select(call => call.Arguments[0]).OfType<ProcessCommand>().Any(command => command.FileName == fileName && arguments.All(command.Arguments.Contains));

    [Fact]
    public void Describes_ItselfWithTheLinuxTargetAndTheBundlesItNeedsForTheLocalModel()
    {
        Assert.Equal("auto-remote", bundle.Name);
        Assert.Equal("Ubuntu 26.04", bundle.Target);
        Assert.Equal(["git", "dotnet", "ollama", "gemma", "open-code"], bundle.Requires);
        Assert.Equal(100, bundle.Order);
    }

    [Fact]
    public async Task Install_PutsTheProgramsTheIconAndTheAppFinderEntryInPlace()
    {
        await bundle.Install(Install("alice"));

        Assert.Equal("server", File.ReadAllText(files.Object.Server));
        Assert.Equal("client", File.ReadAllText(files.Object.Client));
        Assert.Equal("icon", File.ReadAllText(files.Object.Icon));
        Assert.Equal("ENTRY\n", File.ReadAllText(files.Object.DesktopEntry));
        Assert.Equal("1.2.0", File.ReadAllText(files.Object.VersionFile));
        Assert.True(File.Exists(Path.Combine(files.Object.Root, "THIRD-PARTY-NOTICES.txt")));
        Assert.True(File.Exists(Path.Combine(files.Object.Root, "LICENSE")));
        Assert.True(File.GetUnixFileMode(files.Object.Server).HasFlag(UnixFileMode.OtherExecute));
        Assert.True(File.GetUnixFileMode(files.Object.Client).HasFlag(UnixFileMode.UserExecute));
        Assert.False(File.GetUnixFileMode(files.Object.Icon).HasFlag(UnixFileMode.UserExecute));
        Assert.True(state.IsOwned("auto-remote"));
        Assert.True(Ran("update-desktop-database", "/usr/share/applications"));
        aptInstaller.Verify(a => a.InstallRecorded(Path.Combine(payload, "debs"), state, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Install_BuildsTheFontCacheSoTheClientDoesNotAbortOnItsFirstStart()
    {
        await bundle.Install(Install("alice"));

        Assert.True(Ran("fc-cache", "-s"));
    }

    [Fact]
    public async Task Install_SetsUpTheServerServiceForEachUserTheWayTheAuthorHasIt()
    {
        await bundle.Install(Install("alice", "bob"));

        foreach (string user in new[] { "alice", "bob" })
        {
            string unit = Home(user, ".config", "systemd", "user", "auto-remote.service");
            Assert.Equal("UNIT\n", File.ReadAllText(unit));
            string link = Home(user, ".config", "systemd", "user", "default.target.wants", "auto-remote.service");
            Assert.Equal(unit, new FileInfo(link).LinkTarget);
        }

        Assert.Equal(["alice", "bob"], state.Lines("service-users"));
        Assert.Equal(["alice", "bob"], state.Lines("linger-users"));
        Assert.True(Ran("loginctl", "enable-linger", "alice"));
        Assert.True(Ran("loginctl", "enable-linger", "bob"));
    }

    [Fact]
    public async Task Install_CreatesAConfigWithANewKeyThatOnlyTheUserCanRead_AndTheSecondUserGetsTheNextPort()
    {
        await bundle.Install(Install("alice", "bob"));

        JsonNode alice = JsonNode.Parse(File.ReadAllText(Home("alice", ".config", "auto-remote", "config.json")))!;
        JsonNode bob = JsonNode.Parse(File.ReadAllText(Home("bob", ".config", "auto-remote", "config.json")))!;
        Assert.Equal("NEWKEY", alice["key"]!.GetValue<string>());
        Assert.Null(alice["port"]);
        Assert.Equal(52680, bob["port"]!.GetValue<int>());
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Home("alice", ".config", "auto-remote", "config.json")));
        log.Verify(l => l.Write(LogLevel.Info, It.Is<string>(text => text.Contains("NEWKEY"))), Times.Never);
    }

    [Fact]
    public async Task Install_MakesSureTheDataFolderOfTheServerWillBeUnderLocalShare()
    {
        await bundle.Install(Install("alice"));

        Assert.True(Directory.Exists(Home("alice", ".local", "share")));
    }

    [Fact]
    public async Task Install_KeepsAConfigThatIsThereAlready()
    {
        directory.Write("home/alice/.config/auto-remote/config.json", "{ \"key\": \"mine\" }");

        await bundle.Install(Install("alice"));

        Assert.Equal("{ \"key\": \"mine\" }", File.ReadAllText(Home("alice", ".config", "auto-remote", "config.json")));
    }

    [Fact]
    public async Task Install_LeavesAUnitThatIsNotItsOwnAloneAndSetsNothingUpForThatUser()
    {
        directory.Write("home/alice/.config/systemd/user/auto-remote.service", "their own unit");

        await bundle.Install(Install("alice"));

        Assert.Equal("their own unit", File.ReadAllText(Home("alice", ".config", "systemd", "user", "auto-remote.service")));
        Assert.False(Directory.Exists(Home("alice", ".config", "systemd", "user", "default.target.wants")));
        Assert.Empty(state.Lines("service-users"));
        log.Verify(l => l.Write(LogLevel.Warning, It.Is<string>(text => text.Contains("is not the one of this bundle"))), Times.Once);
    }

    [Fact]
    public async Task Install_SameVersionAlreadyThere_SkipsTheFilesButStillSetsUpTheUsers()
    {
        directory.Write("opt/version.txt", "1.2.0");
        directory.Write("opt/server/Markwardt.AutoRemote.Server", "old server");

        await bundle.Install(Install("alice"));

        Assert.Equal("old server", File.ReadAllText(files.Object.Server));
        Assert.False(state.IsOwned("auto-remote"));
        Assert.Equal(["alice"], state.Lines("service-users"));
    }

    [Fact]
    public async Task Install_OlderVersionThere_IsReplaced()
    {
        directory.Write("opt/version.txt", "1.1.9");
        directory.Write("opt/server/Markwardt.AutoRemote.Server", "old server");

        await bundle.Install(Install("alice"));

        Assert.Equal("server", File.ReadAllText(files.Object.Server));
        Assert.Equal("1.2.0", File.ReadAllText(files.Object.VersionFile));
    }

    [Fact]
    public async Task Install_WithSystemdRunning_RestartsTheServiceInTheUsersOwnManager()
    {
        systemd.SetupGet(s => s.IsRunning).Returns(true);

        await bundle.Install(Install("alice"));

        Assert.True(Ran("systemctl", "--user", "alice@.host", "daemon-reload"));
        Assert.True(Ran("systemctl", "--user", "alice@.host", "restart", "auto-remote.service"));
    }

    [Fact]
    public async Task Install_WithoutSystemd_DoesNotCallSystemctl()
    {
        await bundle.Install(Install("alice"));

        Assert.False(Ran("systemctl"));
    }

    [Fact]
    public async Task Install_WithoutAnyUser_WarnsAndStillInstallsThePrograms()
    {
        await bundle.Install(Install());

        Assert.True(File.Exists(files.Object.Server));
        log.Verify(l => l.Write(LogLevel.Warning, It.Is<string>(text => text.Contains("no normal user accounts"))), Times.Once);
    }

    [Fact]
    public async Task Uninstall_RemovesTheServiceTheProgramsAndTheEntry_ButKeepsTheConfigAndData()
    {
        await bundle.Install(Install("alice"));
        directory.Write("home/alice/.local/share/AutoRemote/workspaces.json", "{}");

        await bundle.Uninstall(new() { State = state, Users = ["alice"] });

        Assert.False(File.Exists(Home("alice", ".config", "systemd", "user", "auto-remote.service")));
        Assert.False(Directory.Exists(Home("alice", ".config", "systemd")));
        Assert.True(File.Exists(Home("alice", ".config", "auto-remote", "config.json")));
        Assert.True(File.Exists(Home("alice", ".local", "share", "AutoRemote", "workspaces.json")));
        Assert.False(Directory.Exists(files.Object.Root));
        Assert.False(File.Exists(files.Object.Icon));
        Assert.False(File.Exists(files.Object.DesktopEntry));
        Assert.True(Ran("loginctl", "disable-linger", "alice"));
        aptInstaller.Verify(a => a.RemoveAdded(state, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Uninstall_WithSystemdRunning_StopsAndDisablesTheServiceFirst()
    {
        systemd.SetupGet(s => s.IsRunning).Returns(true);
        await bundle.Install(Install("alice"));

        await bundle.Uninstall(new() { State = state, Users = ["alice"] });

        Assert.True(Ran("systemctl", "--user", "alice@.host", "disable", "--now", "auto-remote.service"));
    }

    [Fact]
    public async Task Uninstall_ProgramsThatWereThereBefore_AreLeftInPlace()
    {
        directory.Write("opt/version.txt", "1.2.0");
        directory.Write("opt/server/Markwardt.AutoRemote.Server", "theirs");
        await bundle.Install(Install("alice"));

        await bundle.Uninstall(new() { State = state, Users = ["alice"] });

        Assert.Equal("theirs", File.ReadAllText(files.Object.Server));
    }

    [Fact]
    public async Task Uninstall_ForAUserThatNoLongerExists_DoesNotFail()
    {
        await bundle.Install(Install("alice"));
        users.Setup(u => u.Home("alice")).Throws(new InvalidOperationException("no such user: alice"));

        await bundle.Uninstall(new() { State = state, Users = [] });

        Assert.False(Directory.Exists(files.Object.Root));
    }

    [Fact]
    public async Task ReinstallShape_UninstallThenInstall_KeepsTheKey()
    {
        await bundle.Install(Install("alice"));
        files.Setup(f => f.NewKey()).Returns("ANOTHER");

        await bundle.Uninstall(new() { State = state, Users = ["alice"] });
        state.End();
        await bundle.Install(Install("alice"));

        Assert.Equal("NEWKEY", JsonNode.Parse(File.ReadAllText(Home("alice", ".config", "auto-remote", "config.json")))!["key"]!.GetValue<string>());
        Assert.Equal("UNIT\n", File.ReadAllText(Home("alice", ".config", "systemd", "user", "auto-remote.service")));
    }
}
