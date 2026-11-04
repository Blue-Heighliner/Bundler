namespace BlueHeighliner.Bundler.Tests;

public sealed class BundleEngineTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IBundleCatalog> catalog = new();
    private readonly Mock<IBundleFiles> files = new();
    private readonly Mock<IBundleArchive> archive = new();
    private readonly Mock<IStateStore> states = new();
    private readonly Mock<IUsers> users = new();
    private readonly Mock<IPaths> paths = new();
    private readonly Mock<IMachine> machine = new();
    private readonly Mock<ILog> log = new();
    private readonly Mock<IBundle> git = new();
    private readonly Mock<IBundle> dev = new();
    private readonly Mock<IBundleState> gitState = new();
    private readonly Mock<IBundleState> devState = new();
    private readonly BundleEngine engine;

    public BundleEngineTests()
    {
        Directory.CreateDirectory(directory.Combine("bundles"));
        git.SetupGet(b => b.Name).Returns("git");
        git.SetupGet(b => b.Description).Returns("Git");
        git.SetupGet(b => b.Target).Returns("Ubuntu 26.04");
        git.SetupGet(b => b.Requires).Returns([]);
        git.SetupGet(b => b.Resumable).Returns([]);
        dev.SetupGet(b => b.Name).Returns("dev");
        dev.SetupGet(b => b.Description).Returns("Dev");
        dev.SetupGet(b => b.Target).Returns("Ubuntu 26.04");
        dev.SetupGet(b => b.Requires).Returns(["git"]);
        dev.SetupGet(b => b.Resumable).Returns([]);
        catalog.SetupGet(c => c.All).Returns([git.Object, dev.Object]);
        catalog.Setup(c => c.Get("git")).Returns(git.Object);
        catalog.Setup(c => c.Get("dev")).Returns(dev.Object);
        catalog.Setup(c => c.Get("nope")).Throws(new InvalidOperationException("unknown bundle 'nope'"));
        states.Setup(s => s.Get("git")).Returns(gitState.Object);
        states.Setup(s => s.Get("dev")).Returns(devState.Object);
        gitState.SetupGet(s => s.Directory).Returns(directory.Combine("state", "git"));
        devState.SetupGet(s => s.Directory).Returns(directory.Combine("state", "dev"));
        paths.SetupGet(p => p.Staging).Returns(directory.Combine("staging"));
        paths.SetupGet(p => p.Unpack).Returns(directory.Path);
        paths.SetupGet(p => p.Bundles).Returns(directory.Combine("bundles"));
        machine.SetupGet(m => m.IsLinux).Returns(true);
        machine.SetupGet(m => m.IsPrivileged).Returns(true);
        users.Setup(u => u.Normal()).Returns(["alice"]);
        engine = new(catalog.Object, files.Object, archive.Object, states.Object, users.Object, paths.Object, machine.Object, log.Object);
    }

    public void Dispose()
        => directory.Dispose();

    private void SetupPack()
        => archive.Setup(a => a.Pack(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<BundleInfo>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, BundleInfo, IReadOnlyDictionary<string, string>, CancellationToken>((_, file, _, _, _) => File.WriteAllText(file, "packed"))
            .Returns(Task.CompletedTask);

    private void HaveBuilt(string name)
    {
        string path = directory.Write($"bundles/{name}.bundle", "bundle");
        files.Setup(f => f.Find(name)).Returns(path);
        files.Setup(f => f.PathFor(name)).Returns(path);
        archive.Setup(a => a.ReadInfo(path)).Returns(new BundleInfo { Name = name, Built = "2026-10-30T09:00:00" });
    }

    [Fact]
    public void List_ReportsEachStandingAndWhenTheFileWasChanged()
    {
        HaveBuilt("git");
        File.SetLastWriteTime(directory.Combine("bundles", "git.bundle"), new DateTime(2026, 10, 30, 9, 19, 0));
        gitState.SetupGet(s => s.IsInstalled).Returns(true);
        devState.SetupGet(s => s.IsInstalled).Returns(false);
        files.Setup(f => f.Find("dev")).Returns((string?)null);

        IReadOnlyList<BundleStatus> list = engine.List();

        Assert.Equal("Ubuntu 26.04", list[0].Target);
        Assert.Equal(BundleStanding.Installed, list[0].State);
        Assert.Equal(new DateTime(2026, 10, 30, 9, 19, 0), list[0].FileModified);
        Assert.Equal(BundleStanding.NotBuilt, list[1].State);
        Assert.Null(list[1].FileModified);
    }

    [Fact]
    public void List_BuiltOnly_IsAvailable()
    {
        HaveBuilt("git");
        files.Setup(f => f.Find("dev")).Returns((string?)null);

        Assert.Equal(BundleStanding.Available, engine.List()[0].State);
    }

    [Fact]
    public async Task Build_FirstTime_PacksTheBundleFile()
    {
        files.Setup(f => f.PathFor("git")).Returns(directory.Combine("bundles", "git.bundle"));
        git.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BuildResult { Components = ["git"] });
        archive.Setup(a => a.ComputeChecksums(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, string> { ["a"] = "1" });
        SetupPack();

        await engine.Build(["git"]);

        archive.Verify(a => a.Pack(It.IsAny<string>(), directory.Combine("bundles", "git.bundle"), It.Is<BundleInfo>(info => info.Name == "git"), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Build_ContentDiffers_ReplacesTheBundleFile()
    {
        HaveBuilt("git");
        archive.Setup(a => a.ReadChecksums(It.IsAny<string>())).Returns(new Dictionary<string, string> { ["a"] = "old" });
        git.Setup(b => b.IsUnchanged(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        git.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BuildResult { Components = [] });
        archive.Setup(a => a.ComputeChecksums(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, string> { ["a"] = "new" });
        SetupPack();

        await engine.Build(["git"]);

        archive.Verify(a => a.Pack(It.IsAny<string>(), directory.Combine("bundles", "git.bundle"), It.IsAny<BundleInfo>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Build_SameContent_LeavesTheFileAlone()
    {
        HaveBuilt("git");
        Dictionary<string, string> same = new() { ["a"] = "same" };
        archive.Setup(a => a.ReadChecksums(It.IsAny<string>())).Returns(same);
        archive.Setup(a => a.ComputeChecksums(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(same);
        git.Setup(b => b.IsUnchanged(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        git.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BuildResult { Components = [] });

        await engine.Build(["git"]);

        archive.Verify(a => a.Pack(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<BundleInfo>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Build_UnchangedUpstream_SkipsDownloadingEntirely()
    {
        HaveBuilt("git");
        archive.Setup(a => a.ReadChecksums(It.IsAny<string>())).Returns(new Dictionary<string, string>());
        git.Setup(b => b.IsUnchanged(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await engine.Build(["git"]);

        git.Verify(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Build_All_BuildsEveryBundleOnce()
    {
        files.Setup(f => f.Find(It.IsAny<string>())).Returns((string?)null);
        files.Setup(f => f.PathFor(It.IsAny<string>())).Returns((string name) => directory.Combine("bundles", name + ".bundle"));
        archive.Setup(a => a.ComputeChecksums(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, string>());
        archive.Setup(a => a.Pack(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<BundleInfo>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, BundleInfo, IReadOnlyDictionary<string, string>, CancellationToken>((_, file, _, _, _) => File.WriteAllText(file, "packed"))
            .Returns(Task.CompletedTask);
        git.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BuildResult { Components = [] });
        dev.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BuildResult { Components = [] });

        await engine.Build(["all", "git"]);

        git.Verify(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        dev.Verify(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Build_UnknownName_FailsBeforeBuildingAnything()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.Build(["git", "nope"]));

        git.Verify(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Build_NotOnLinux_Throws()
    {
        machine.SetupGet(m => m.IsLinux).Returns(false);

        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => engine.Build(["git"]));
    }

    [Fact]
    public void InstallPlan_AddsMissingRequirementsFirst()
    {
        gitState.SetupGet(s => s.IsInstalled).Returns(false);

        Assert.Equal(["git", "dev"], engine.InstallPlan(["dev"]));
    }

    [Fact]
    public void InstallPlan_SkipsRequirementsThatAreInstalled()
    {
        gitState.SetupGet(s => s.IsInstalled).Returns(true);

        Assert.Equal(["dev"], engine.InstallPlan(["dev"]));
    }

    [Fact]
    public async Task Install_NotPrivileged_Throws()
    {
        machine.SetupGet(m => m.IsPrivileged).Returns(false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => engine.Install(["git"], new()));
    }

    [Fact]
    public async Task Install_NotBuilt_Throws()
    {
        files.Setup(f => f.Find("git")).Returns((string?)null);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.Install(["git"], new()));

        Assert.Contains("not built", error.Message);
    }

    [Fact]
    public async Task Install_NewBundle_UnpacksVerifiesInstallsAndRecords()
    {
        HaveBuilt("git");
        gitState.SetupGet(s => s.IsInstalled).Returns(false);
        archive.Setup(a => a.Extract(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        archive.Setup(a => a.Verify(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await engine.Install(["git"], new());

        git.Verify(b => b.Install(It.Is<InstallContext>(context => context.Users.SequenceEqual(new[] { "alice" })), It.IsAny<CancellationToken>()), Times.Once);
        gitState.Verify(s => s.Begin(It.Is<IEnumerable<string>>(accounts => accounts.SequenceEqual(new[] { "alice" }))), Times.Once);
    }

    [Fact]
    public async Task Install_AlreadyInstalled_IsSkippedWithAHintToReinstall()
    {
        HaveBuilt("git");
        gitState.SetupGet(s => s.IsInstalled).Returns(true);

        await engine.Install(["git"], new());

        git.Verify(b => b.Install(It.IsAny<InstallContext>(), It.IsAny<CancellationToken>()), Times.Never);
        log.Verify(l => l.Write(LogLevel.Info, It.Is<string>(text => text.Contains("reinstall git"))), Times.Once);
    }

    [Fact]
    public async Task Reinstall_UninstallsWhatIsInstalledThenInstallsAll()
    {
        HaveBuilt("git");
        HaveBuilt("dev");
        gitState.SetupGet(s => s.IsInstalled).Returns(true);
        gitState.SetupGet(s => s.Users).Returns([]);
        devState.SetupGet(s => s.IsInstalled).Returns(false);
        devState.SetupGet(s => s.Directory).Returns(directory.Combine("state", "missing-dev"));
        git.SetupGet(b => b.Order).Returns(10);
        dev.SetupGet(b => b.Order).Returns(20);
        archive.Setup(a => a.Verify(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        List<string> order = [];
        git.Setup(b => b.Uninstall(It.IsAny<UninstallContext>(), It.IsAny<CancellationToken>())).Callback(() =>
        {
            order.Add("uninstall git");
            gitState.SetupGet(s => s.IsInstalled).Returns(false);
        }).Returns(Task.CompletedTask);
        git.Setup(b => b.Install(It.IsAny<InstallContext>(), It.IsAny<CancellationToken>())).Callback(() => order.Add("install git")).Returns(Task.CompletedTask);
        dev.Setup(b => b.Install(It.IsAny<InstallContext>(), It.IsAny<CancellationToken>())).Callback(() => order.Add("install dev")).Returns(Task.CompletedTask);

        await engine.Reinstall(["git", "dev"], new());

        Assert.Equal(["uninstall git", "install git", "install dev"], order);
        dev.Verify(b => b.Uninstall(It.IsAny<UninstallContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reinstall_AnotherInstalledBundleNeedsOne_ChangesNothing()
    {
        HaveBuilt("git");
        gitState.SetupGet(s => s.IsInstalled).Returns(true);
        devState.SetupGet(s => s.IsInstalled).Returns(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.Reinstall(["git"], new()));

        git.Verify(b => b.Uninstall(It.IsAny<UninstallContext>(), It.IsAny<CancellationToken>()), Times.Never);
        git.Verify(b => b.Install(It.IsAny<InstallContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reinstall_MissingFile_ChangesNothing()
    {
        gitState.SetupGet(s => s.IsInstalled).Returns(true);
        files.Setup(f => f.Find("git")).Returns((string?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.Reinstall(["git"], new()));

        git.Verify(b => b.Uninstall(It.IsAny<UninstallContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reinstall_Declined_ChangesNothing()
    {
        HaveBuilt("git");
        gitState.SetupGet(s => s.IsInstalled).Returns(true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => engine.Reinstall(["git"], new() { Confirm = _ => Task.FromResult(false) }));

        git.Verify(b => b.Uninstall(It.IsAny<UninstallContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Install_AfterTheOldOneWasUninstalled_InstallsTheFile()
    {
        HaveBuilt("git");
        gitState.SetupGet(s => s.IsInstalled).Returns(false);
        archive.Setup(a => a.Verify(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await engine.Install(["git"], new());

        gitState.Verify(s => s.Begin(It.IsAny<IEnumerable<string>>()), Times.Once);
    }

    [Fact]
    public async Task Install_DamagedFile_ThrowsAndInstallsNothing()
    {
        HaveBuilt("git");
        gitState.SetupGet(s => s.IsInstalled).Returns(false);
        archive.Setup(a => a.Verify(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(["a.txt"]);

        await Assert.ThrowsAsync<InvalidDataException>(() => engine.Install(["git"], new()));

        git.Verify(b => b.Install(It.IsAny<InstallContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Install_Declined_ThrowsCancelledAndInstallsNothing()
    {
        HaveBuilt("git");
        gitState.SetupGet(s => s.IsInstalled).Returns(false);

        await Assert.ThrowsAsync<OperationCanceledException>(() => engine.Install(["git"], new() { Confirm = _ => Task.FromResult(false) }));

        git.Verify(b => b.Install(It.IsAny<InstallContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Install_PlansTheRequiredBundleToo()
    {
        HaveBuilt("git");
        HaveBuilt("dev");
        gitState.SetupGet(s => s.IsInstalled).Returns(false);
        devState.SetupGet(s => s.IsInstalled).Returns(false);
        archive.Setup(a => a.Verify(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        List<string> order = [];
        git.Setup(b => b.Install(It.IsAny<InstallContext>(), It.IsAny<CancellationToken>())).Callback(() => order.Add("git")).Returns(Task.CompletedTask);
        dev.Setup(b => b.Install(It.IsAny<InstallContext>(), It.IsAny<CancellationToken>())).Callback(() => order.Add("dev")).Returns(Task.CompletedTask);

        await engine.Install(["dev"], new());

        Assert.Equal(["git", "dev"], order);
    }

    [Fact]
    public async Task Uninstall_WhileADependentIsInstalled_Throws()
    {
        gitState.SetupGet(s => s.IsInstalled).Returns(true);
        devState.SetupGet(s => s.IsInstalled).Returns(true);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.Uninstall(["git"], new()));

        Assert.Contains("dev", error.Message);
        git.Verify(b => b.Uninstall(It.IsAny<UninstallContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Uninstall_NotInstalled_Throws()
    {
        gitState.SetupGet(s => s.IsInstalled).Returns(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.Uninstall(["git"], new()));
    }

    [Fact]
    public async Task Uninstall_BothTogether_RemovesDependentFirst()
    {
        gitState.SetupGet(s => s.IsInstalled).Returns(true);
        devState.SetupGet(s => s.IsInstalled).Returns(true);
        gitState.SetupGet(s => s.Users).Returns([]);
        devState.SetupGet(s => s.Users).Returns([]);
        git.SetupGet(b => b.Order).Returns(10);
        dev.SetupGet(b => b.Order).Returns(20);
        List<string> order = [];
        git.Setup(b => b.Uninstall(It.IsAny<UninstallContext>(), It.IsAny<CancellationToken>())).Callback(() => order.Add("git")).Returns(Task.CompletedTask);
        dev.Setup(b => b.Uninstall(It.IsAny<UninstallContext>(), It.IsAny<CancellationToken>())).Callback(() => order.Add("dev")).Returns(Task.CompletedTask);

        await engine.Uninstall(["git", "dev"], new());

        Assert.Equal(["dev", "git"], order);
        gitState.Verify(s => s.End(), Times.Once);
        devState.Verify(s => s.End(), Times.Once);
    }
}
