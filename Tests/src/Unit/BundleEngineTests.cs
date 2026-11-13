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
    private readonly List<(string Name, long? Limit, byte[] Bytes)> written = [];
    private readonly BundleEngine engine;

    public BundleEngineTests()
    {
        Directory.CreateDirectory(directory.Combine("bundles"));
        git.SetupGet(b => b.Name).Returns("git");
        git.SetupGet(b => b.Description).Returns("Git");
        git.SetupGet(b => b.Target).Returns("Ubuntu 24.04");
        git.SetupGet(b => b.Requires).Returns([]);
        git.SetupGet(b => b.Resumable).Returns([]);
        dev.SetupGet(b => b.Name).Returns("dev");
        dev.SetupGet(b => b.Description).Returns("Dev");
        dev.SetupGet(b => b.Target).Returns("Ubuntu 24.04");
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
        engine = new(catalog.Object, files.Object, archive.Object, states.Object, users.Object, paths.Object, machine.Object, new ByteSizes(), log.Object);
    }

    public void Dispose()
        => directory.Dispose();

    private BundleFile Bundle(string name, params int[] sizes)
    {
        string[] paths = sizes.Length == 1
            ? [directory.Write($"bundles/{name}.bundle", new string('x', sizes[0]))]
            : [.. sizes.Select((size, index) => directory.Write($"bundles/{name}.bundle.{index + 1:D3}", new string('x', size)))];
        return new() { Name = name, Paths = paths, Length = sizes.Sum(), Modified = paths.Max(path => File.GetLastWriteTime(path)), IsSplit = sizes.Length > 1 };
    }

    private void SetupPack()
    {
        archive.Setup(a => a.Pack(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<BundleInfo>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Stream, BundleInfo, IReadOnlyDictionary<string, string>, CancellationToken>((_, output, _, _, _) => output.Write("packed"u8))
            .Returns(Task.CompletedTask);
        files.Setup(f => f.Write(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<Func<Stream, Task>>(), It.IsAny<CancellationToken>())).Returns(async (string name, long? limit, Func<Stream, Task> write, CancellationToken _) =>
        {
            using MemoryStream stream = new();
            await write(stream);
            written.Add((name, limit, stream.ToArray()));
            return Bundle(name, (int)stream.Length);
        });
    }

    private BundleFile HaveBuilt(string name, params int[] sizes)
    {
        BundleFile file = Bundle(name, sizes.Length == 0 ? [6] : sizes);
        files.Setup(f => f.Find(name)).Returns(file);
        archive.Setup(a => a.ReadInfo(file)).Returns(new BundleInfo { Name = name, Built = "2026-10-30T09:00:00" });
        return file;
    }

    [Fact]
    public void List_ReportsEachStandingAndWhenTheFileWasChanged()
    {
        BundleFile built = HaveBuilt("git");
        files.Setup(f => f.Find("git")).Returns(built with { Modified = new DateTime(2026, 10, 30, 9, 19, 0) });
        gitState.SetupGet(s => s.IsInstalled).Returns(true);
        devState.SetupGet(s => s.IsInstalled).Returns(false);
        files.Setup(f => f.Find("dev")).Returns((BundleFile?)null);

        IReadOnlyList<BundleStatus> list = engine.List();

        Assert.Equal("Ubuntu 24.04", list[0].Target);
        Assert.Equal(BundleStanding.Installed, list[0].State);
        Assert.Equal(new DateTime(2026, 10, 30, 9, 19, 0), list[0].FileModified);
        Assert.Equal(BundleStanding.NotBuilt, list[1].State);
        Assert.Null(list[1].FileModified);
    }

    [Fact]
    public void List_BuiltOnly_IsAvailable()
    {
        HaveBuilt("git");
        files.Setup(f => f.Find("dev")).Returns((BundleFile?)null);

        Assert.Equal(BundleStanding.Available, engine.List()[0].State);
    }

    [Fact]
    public async Task Build_FirstTime_PacksTheBundleFile()
    {
        git.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BuildResult { Components = ["git"] });
        archive.Setup(a => a.ComputeChecksums(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, string> { ["a"] = "1" });
        SetupPack();

        await engine.Build(["git"]);

        archive.Verify(a => a.Pack(It.IsAny<string>(), It.IsAny<Stream>(), It.Is<BundleInfo>(info => info.Name == "git"), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        (string name, long? limit, byte[] bytes) = Assert.Single(written);
        Assert.Equal("git", name);
        Assert.Null(limit);
        Assert.Equal("packed"u8.ToArray(), bytes);
    }

    [Fact]
    public async Task Build_ContentDiffers_ReplacesTheBundleFile()
    {
        HaveBuilt("git");
        archive.Setup(a => a.ReadChecksums(It.IsAny<BundleFile>())).Returns(new Dictionary<string, string> { ["a"] = "old" });
        git.Setup(b => b.IsUnchanged(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        git.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BuildResult { Components = [] });
        archive.Setup(a => a.ComputeChecksums(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, string> { ["a"] = "new" });
        SetupPack();

        await engine.Build(["git"]);

        archive.Verify(a => a.Pack(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<BundleInfo>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Build_SameContent_LeavesTheFileAlone()
    {
        HaveBuilt("git");
        Dictionary<string, string> same = new() { ["a"] = "same" };
        archive.Setup(a => a.ReadChecksums(It.IsAny<BundleFile>())).Returns(same);
        archive.Setup(a => a.ComputeChecksums(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(same);
        git.Setup(b => b.IsUnchanged(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        git.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BuildResult { Components = [] });

        await engine.Build(["git"]);

        archive.Verify(a => a.Pack(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<BundleInfo>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Build_UnchangedUpstream_SkipsDownloadingEntirely()
    {
        HaveBuilt("git");
        archive.Setup(a => a.ReadChecksums(It.IsAny<BundleFile>())).Returns(new Dictionary<string, string>());
        git.Setup(b => b.IsUnchanged(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await engine.Build(["git"]);

        git.Verify(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Build_All_BuildsEveryBundleOnce()
    {
        files.Setup(f => f.Find(It.IsAny<string>())).Returns((BundleFile?)null);
        archive.Setup(a => a.ComputeChecksums(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, string>());
        SetupPack();
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
        files.Setup(f => f.Find("git")).Returns((BundleFile?)null);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.Install(["git"], new()));

        Assert.Contains("not built", error.Message);
    }

    [Fact]
    public async Task Install_NewBundle_UnpacksVerifiesInstallsAndRecords()
    {
        HaveBuilt("git");
        gitState.SetupGet(s => s.IsInstalled).Returns(false);
        archive.Setup(a => a.Extract(It.IsAny<BundleFile>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        archive.Setup(a => a.Verify(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await engine.Install(["git"], new());

        git.Verify(b => b.Install(It.Is<InstallContext>(context => context.Users.SequenceEqual(new[] { "alice" })), It.IsAny<CancellationToken>()), Times.Once);
        gitState.Verify(s => s.Begin(It.Is<IEnumerable<string>>(accounts => accounts.SequenceEqual(new[] { "alice" }))), Times.Once);
    }

    [Theory]
    [InlineData("Ubuntu 26.04", true)]
    [InlineData("Ubuntu 24.04", false)]
    [InlineData(null, false)]
    public async Task Install_SystemOtherThanTheTarget_WarnsButStillInstalls(string? system, bool warns)
    {
        HaveBuilt("git");
        gitState.SetupGet(s => s.IsInstalled).Returns(false);
        machine.SetupGet(m => m.SystemName).Returns(system);
        archive.Setup(a => a.Extract(It.IsAny<BundleFile>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        archive.Setup(a => a.Verify(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await engine.Install(["git"], new());

        log.Verify(l => l.Write(LogLevel.Warning, It.Is<string>(text => text.Contains("made for Ubuntu 24.04") && text.Contains("runs Ubuntu 26.04"))), warns ? Times.Once : Times.Never);
        git.Verify(b => b.Install(It.IsAny<InstallContext>(), It.IsAny<CancellationToken>()), Times.Once);
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
        files.Setup(f => f.Find("git")).Returns((BundleFile?)null);

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

    [Fact]
    public async Task Build_WithALimit_PassesItToTheFileWriter()
    {
        git.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BuildResult { Components = [] });
        archive.Setup(a => a.ComputeChecksums(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, string> { ["a"] = "1" });
        SetupPack();

        await engine.Build(["git"], 4_700_000_000);

        Assert.Equal(4_700_000_000, Assert.Single(written).Limit);
    }

    [Theory]
    [InlineData(999_999)]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Build_LimitBelowOneMegabyte_Throws(long limit)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => engine.Build(["git"], limit));

        git.Verify(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Build_OneMegabyteLimit_IsAllowed()
    {
        git.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BuildResult { Components = [] });
        archive.Setup(a => a.ComputeChecksums(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, string>());
        SetupPack();

        await engine.Build(["git"], 1_000_000);

        Assert.Equal(1_000_000, Assert.Single(written).Limit);
    }

    [Fact]
    public async Task Build_UnchangedAndAlreadyLaidOutForTheLimit_IsLeftAlone()
    {
        HaveBuilt("git", 1_000_000, 1_000_000, 300_000);
        archive.Setup(a => a.ReadChecksums(It.IsAny<BundleFile>())).Returns(new Dictionary<string, string>());
        git.Setup(b => b.IsUnchanged(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await engine.Build(["git"], 1_000_000);

        files.Verify(f => f.Write(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<Func<Stream, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        git.Verify(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Build_UnchangedButNotLaidOutForTheLimit_CutsTheExistingFilesUpWithoutDownloading()
    {
        BundleFile existing = HaveBuilt("git", 2_500_000);
        archive.Setup(a => a.ReadChecksums(It.IsAny<BundleFile>())).Returns(new Dictionary<string, string>());
        git.Setup(b => b.IsUnchanged(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        files.Setup(f => f.OpenRead(existing)).Returns(() => new MemoryStream("0123456789"u8.ToArray()));
        SetupPack();

        await engine.Build(["git"], 1_000_000);

        git.Verify(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(written, item => item.Limit == 1_000_000 && item.Bytes.SequenceEqual("0123456789"u8.ToArray()));
    }

    [Fact]
    public async Task Build_UnchangedSplitBundleAndNoLimit_IsJoinedIntoOneFile()
    {
        BundleFile existing = HaveBuilt("git", 100, 100, 30);
        archive.Setup(a => a.ReadChecksums(It.IsAny<BundleFile>())).Returns(new Dictionary<string, string>());
        git.Setup(b => b.IsUnchanged(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        files.Setup(f => f.OpenRead(existing)).Returns(() => new MemoryStream(new byte[230]));
        SetupPack();

        await engine.Build(["git"]);

        (_, long? limit, byte[] bytes) = Assert.Single(written);
        Assert.Null(limit);
        Assert.Equal(230, bytes.Length);
    }

    [Fact]
    public async Task Build_ExistingFilesWithAProblem_AreBuiltAgainInsteadOfTrusted()
    {
        BundleFile broken = Bundle("git", 100, 100, 30) with { Problem = "part 2 is missing" };
        files.Setup(f => f.Find("git")).Returns(broken);
        git.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BuildResult { Components = [] });
        archive.Setup(a => a.ComputeChecksums(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, string> { ["a"] = "1" });
        SetupPack();

        await engine.Build(["git"], 2_000_000);

        git.Verify(b => b.Build(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        archive.Verify(a => a.ReadChecksums(It.IsAny<BundleFile>()), Times.Never);
        log.Verify(l => l.Write(LogLevel.Warning, It.Is<string>(text => text.Contains("part 2 is missing"))), Times.Once);
    }

    [Fact]
    public void List_ReportsHowManyFilesABundleIsInAndWhatIsWrongWithThem()
    {
        BundleFile built = Bundle("git", 100, 100, 30) with { Problem = "part 2 is missing" };
        files.Setup(f => f.Find("git")).Returns(built);
        files.Setup(f => f.Find("dev")).Returns((BundleFile?)null);

        IReadOnlyList<BundleStatus> list = engine.List();

        Assert.Equal(3, list[0].FileCount);
        Assert.Equal("part 2 is missing", list[0].FileProblem);
        Assert.Equal(0, list[1].FileCount);
        Assert.Null(list[1].FileProblem);
    }

    [Fact]
    public async Task Install_ABundleInParts_IsExtractedFromTheWholeBundleAndItsTotalSizeIsChecked()
    {
        BundleFile parts = HaveBuilt("git", 40, 40, 10);
        gitState.SetupGet(s => s.IsInstalled).Returns(false);
        archive.Setup(a => a.Extract(It.IsAny<BundleFile>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        archive.Setup(a => a.Verify(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await engine.Install(["git"], new());

        archive.Verify(a => a.Extract(parts, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        git.Verify(b => b.Install(It.IsAny<InstallContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Install_PartsWithAProblem_IsRefusedBeforeAnythingIsChanged()
    {
        files.Setup(f => f.Find("git")).Returns(Bundle("git", 40, 40, 10) with { Problem = "part 2 is missing" });

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.Install(["git"], new()));

        Assert.Contains("part 2 is missing", error.Message);
        Assert.Contains("all of its parts", error.Message);
        git.Verify(b => b.Install(It.IsAny<InstallContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Install_NotBuilt_MentionsThePartsToo()
    {
        files.Setup(f => f.Find("git")).Returns((BundleFile?)null);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.Install(["git"], new()));

        Assert.Contains("git.bundle.001", error.Message);
    }
}
