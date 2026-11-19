namespace BlueHeighliner.Bundler.Tests;

public sealed class ToolInstallerTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly UninstallHarness harness = new();
    private readonly Mock<IProcessRunner> runner = new();
    private readonly Mock<ILog> log = new();
    private readonly List<ProcessCommand> commands = [];
    private readonly ToolInstaller installer;
    private readonly BundleState state;
    private readonly string root;
    private readonly string binaries;

    public ToolInstallerTests()
    {
        root = directory.Combine("opt", "tool");
        binaries = directory.Combine("bin");
        state = new(directory.Combine("state"));
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ProcessCommand command, CancellationToken _) =>
        {
            commands.Add(command);
            string target = command.Arguments[command.Arguments.ToList().IndexOf("-C") + 1];
            Directory.CreateDirectory(Path.Combine(target, "bin"));
            File.WriteAllText(Path.Combine(target, "bin", "tool"), "program");
            File.WriteAllText(Path.Combine(target, "bin", "helper"), "helper");
            return new ProcessResult(0, string.Empty, string.Empty);
        });
        installer = new(runner.Object, new Versions(), log.Object, binaries);
    }

    public void Dispose()
    {
        harness.Dispose();
        directory.Dispose();
    }

    private Task Uninstall()
    {
        UninstallScript script = new();
        installer.Uninstall(script, state);
        return harness.Run(state, script, "tool");
    }

    private Dictionary<string, string> Links()
        => new() { ["tool"] = "bin/tool", ["helper"] = "bin/helper" };

    [Fact]
    public async Task Install_UnpacksTheArchiveWithoutItsTopFolderIntoTheFolderOfTheVersion()
    {
        await installer.Install(state, "/x/a.tar.xz", root, "1.2.3", Links());

        ProcessCommand tar = Assert.Single(commands);
        Assert.Equal("tar", tar.FileName);
        Assert.Equal("-xf", tar.Arguments[0]);
        Assert.Contains("--strip-components=1", tar.Arguments);
        Assert.Contains("--no-same-owner", tar.Arguments);
        Assert.Equal("program", File.ReadAllText(Path.Combine(root, "1.2.3", "bin", "tool")));
        Assert.False(Directory.Exists(Path.Combine(root, "1.2.3.partial")));
        Assert.True(state.IsOwned("tool"));
        Assert.Equal(Path.Combine(root, "1.2.3"), state.Read("tool-folder"));
    }

    [Fact]
    public async Task Install_LinksTheCommandsIntoTheFolderOfTheVersion()
    {
        await installer.Install(state, "/x/a.tar.xz", root, "1.2.3", Links());

        Assert.Equal(Path.Combine(root, "1.2.3", "bin", "tool"), new FileInfo(Path.Combine(binaries, "tool")).LinkTarget);
        Assert.Equal(Path.Combine(root, "1.2.3", "bin", "helper"), new FileInfo(Path.Combine(binaries, "helper")).LinkTarget);
        Assert.Equal([Path.Combine(binaries, "tool"), Path.Combine(binaries, "helper")], state.Lines("links"));
    }

    [Fact]
    public async Task Install_ACommandThatIsSomeoneElsesFile_IsLeftAloneAndReported()
    {
        Directory.CreateDirectory(binaries);
        File.WriteAllText(Path.Combine(binaries, "tool"), "theirs");

        await installer.Install(state, "/x/a.tar.xz", root, "1.2.3", Links());

        Assert.Equal("theirs", File.ReadAllText(Path.Combine(binaries, "tool")));
        Assert.Equal([Path.Combine(binaries, "helper")], state.Lines("links"));
        log.Verify(l => l.Write(LogLevel.Warning, It.Is<string>(text => text.Contains("is already there and is not a link into"))), Times.Once);
    }

    [Fact]
    public async Task Install_ACommandWhoseFileIsNotInTheArchive_IsNotLinked()
    {
        Dictionary<string, string> links = Links();
        links["extra"] = "bin/extra";

        await installer.Install(state, "/x/a.tar.xz", root, "1.2.3", links);

        Assert.Null(new FileInfo(Path.Combine(binaries, "extra")).LinkTarget);
        Assert.DoesNotContain(Path.Combine(binaries, "extra"), state.Lines("links"));
        log.Verify(l => l.Write(LogLevel.Info, "extra is not in this version, so no command extra is linked"), Times.Once);
        Assert.NotNull(new FileInfo(Path.Combine(binaries, "tool")).LinkTarget);
    }

    [Fact]
    public async Task Install_ALinkToAnOlderVersionOfTheSameProgram_IsMovedToTheNewVersion()
    {
        await installer.Install(state, "/x/a.tar.xz", root, "1.0.0", Links());
        BundleState second = new(directory.Combine("state2"));

        await installer.Install(second, "/x/b.tar.xz", root, "2.0.0", Links());

        Assert.Equal(Path.Combine(root, "2.0.0", "bin", "tool"), new FileInfo(Path.Combine(binaries, "tool")).LinkTarget);
        Assert.True(Directory.Exists(Path.Combine(root, "1.0.0")));
    }

    [Fact]
    public async Task Install_ALeftoverPartialFolder_IsReplaced()
    {
        directory.Write("opt/tool/1.2.3.partial/junk.txt", "old");

        await installer.Install(state, "/x/a.tar.xz", root, "1.2.3", Links());

        Assert.False(File.Exists(Path.Combine(root, "1.2.3", "junk.txt")));
        Assert.True(File.Exists(Path.Combine(root, "1.2.3", "bin", "tool")));
    }

    [Fact]
    public void InstalledVersion_IsTheNewestFolder_AndIgnoresPartialOnes()
    {
        foreach (string name in new[] { "9.0.1", "10.2.0", "10.10.0", "10.11.0.partial", "notes" })
        {
            Directory.CreateDirectory(Path.Combine(root, name));
        }

        Assert.Equal("10.10.0", installer.InstalledVersion(root));
    }

    [Fact]
    public void InstalledVersion_NoFolder_IsNull()
        => Assert.Null(installer.InstalledVersion(root));

    [Fact]
    public async Task Uninstall_RemovesTheLinksTheFolderAndTheEmptyProgramFolder()
    {
        await installer.Install(state, "/x/a.tar.xz", root, "1.2.3", Links());

        await Uninstall();

        Assert.False(File.Exists(Path.Combine(binaries, "tool")) || new FileInfo(Path.Combine(binaries, "tool")).LinkTarget is not null);
        Assert.False(Directory.Exists(Path.Combine(root, "1.2.3")));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Uninstall_KeepsTheProgramFolderWhenAnotherVersionIsInIt()
    {
        Directory.CreateDirectory(Path.Combine(root, "0.9.0"));
        await installer.Install(state, "/x/a.tar.xz", root, "1.2.3", Links());

        await Uninstall();

        Assert.True(Directory.Exists(Path.Combine(root, "0.9.0")));
    }

    [Fact]
    public async Task Uninstall_ALinkThatPointsSomewhereElseNow_IsLeftAlone()
    {
        await installer.Install(state, "/x/a.tar.xz", root, "1.2.3", Links());
        File.Delete(Path.Combine(binaries, "tool"));
        File.CreateSymbolicLink(Path.Combine(binaries, "tool"), "/usr/bin/something-else");

        await Uninstall();

        Assert.Equal("/usr/bin/something-else", new FileInfo(Path.Combine(binaries, "tool")).LinkTarget);
    }

    [Fact]
    public async Task Uninstall_WhenTheBundleDidNotInstallTheFolder_RemovesNothing()
    {
        Directory.CreateDirectory(Path.Combine(root, "1.2.3"));
        state.Write("tool-folder", Path.Combine(root, "1.2.3"));

        await Uninstall();

        Assert.True(Directory.Exists(Path.Combine(root, "1.2.3")));
    }
}
