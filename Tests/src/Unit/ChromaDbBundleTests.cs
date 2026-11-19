namespace BlueHeighliner.Bundler.Tests;

public sealed class ChromaDbBundleTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IPythonWheels> python = new();
    private readonly Mock<IOllamaRegistry> registry = new();
    private readonly Mock<IOllamaModelFiles> modelFiles = new();
    private readonly Mock<IAptBuilder> apt = new();
    private readonly Mock<IAptInstaller> aptInstaller = new();
    private readonly Mock<ISystemd> systemd = new();
    private readonly Mock<IProcessRunner> runner = new();
    private readonly Mock<ILog> log = new();
    private readonly List<ProcessCommand> commands = [];
    private readonly ChromaDbBundle bundle;
    private readonly BundleState state;
    private readonly string root;
    private readonly string data;
    private readonly string link;
    private readonly string unit;
    private bool userExists;
    private string? chromaVersion;

    public ChromaDbBundleTests()
    {
        root = directory.Combine("opt");
        data = directory.Combine("var");
        link = directory.Combine("bin", "chroma");
        unit = directory.Combine("systemd", "chroma-db.service");
        Directory.CreateDirectory(directory.Combine("bin"));
        Directory.CreateDirectory(directory.Combine("staging"));
        state = new(directory.Combine("state"));
        python.Setup(p => p.Download(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<PythonWheel>)
            [new() { Name = "numpy", Version = "2.5.5", FileName = "numpy.whl", License = "BSD-3-Clause", NeedsReview = false }, new() { Name = "chromadb", Version = "1.5.9", FileName = "chromadb.whl", License = "Apache-2.0", NeedsReview = false }]);
        python.Setup(p => p.Format(It.IsAny<IEnumerable<PythonWheel>>())).Returns("numpy\t2.5.5\tOK\tBSD-3-Clause\n");
        registry.Setup(r => r.Fetch("nomic-embed-text", "latest", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new OllamaModel { Parameters = "137M", Quantization = "F16", Requires = "0.1.26", Bytes = 274_000_000 });
        modelFiles.Setup(m => m.Install(It.IsAny<InstallContext>(), It.IsAny<CancellationToken>())).ReturnsAsync("nomic-embed-text:latest");
        systemd.SetupGet(s => s.IsRunning).Returns(true);
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ProcessCommand command, CancellationToken _) =>
        {
            commands.Add(command);
            return command.FileName switch
            {
                "id" => new ProcessResult(userExists ? 0 : 1, string.Empty, string.Empty),
                "getent" => new ProcessResult(1, string.Empty, string.Empty),
                _ when command.Arguments.Contains("import importlib.metadata as m;print(m.version('chromadb'))") => new ProcessResult(chromaVersion is null ? 1 : 0, chromaVersion + "\n", string.Empty),
                _ => new ProcessResult(0, string.Empty, string.Empty),
            };
        });
        bundle = new(python.Object, registry.Object, modelFiles.Object, apt.Object, aptInstaller.Object, new Resources(), new Decisions(new Versions(), log.Object), new Versions(), systemd.Object, runner.Object, log.Object, root, data, link, unit);
    }

    public void Dispose()
        => directory.Dispose();

    private InstallContext Install()
        => new() { Payload = directory.Combine("payload"), State = state, Users = [], Info = new() { Name = "chroma-db", Components = ["ChromaDB 1.5.9", "2 Python packages (wheels)", "nomic-embed-text:latest 137M F16"] } };

    private UninstallContext Uninstall()
        => new() { State = state, Users = [] };

    private string VenvPython
        => Path.Combine(root, "venv", "bin", "python");

    [Fact]
    public void Bundle_NeedsOllamaForTheModel()
    {
        Assert.Equal("chroma-db", bundle.Name);
        Assert.Equal(["ollama"], bundle.Requires);
        Assert.Equal(["ollama-models"], bundle.Resumable);
    }

    [Fact]
    public async Task Build_BringsTheWheelsTheModelAndPythonVenv_AndReportsThem()
    {
        BuildResult result = await bundle.Build(directory.Combine("staging"));

        Assert.Equal(["ChromaDB 1.5.9", "2 Python packages (wheels for Python 3.12, linux x86-64)", "nomic-embed-text:latest 137M F16"], result.Components);
        python.Verify(p => p.Download(directory.Combine("staging", "wheels"), It.Is<IReadOnlyList<string>>(requirements => requirements.SequenceEqual(new[] { "chromadb", "ollama" })), It.IsAny<CancellationToken>()), Times.Once);
        registry.Verify(r => r.Fetch("nomic-embed-text", "latest", directory.Combine("staging", "ollama-models"), It.IsAny<CancellationToken>()), Times.Once);
        apt.Verify(a => a.Download(directory.Combine("staging", "debs"), It.Is<AptRequest>(request => request.Support.SequenceEqual(new[] { "python3-venv" })), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("numpy\t2.5.5\tOK\tBSD-3-Clause\n", File.ReadAllText(directory.Combine("staging", "python-licenses.tsv")));
        Assert.Equal("model=nomic-embed-text:latest\nrequires_ollama=0.1.26\nbytes=274000000\n", File.ReadAllText(directory.Combine("staging", "model.info")));
    }

    [Fact]
    public async Task Build_AWheelWhoseLicenseNeedsReview_IsWarnedAbout()
    {
        python.Setup(p => p.Download(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<PythonWheel>)
            [new() { Name = "chromadb", Version = "1.5.9", FileName = "c.whl", License = "Apache-2.0", NeedsReview = false }, new() { Name = "odd", Version = "3", FileName = "o.whl", License = "GPL-3.0-only", NeedsReview = true }]);

        await bundle.Build(directory.Combine("staging"));

        log.Verify(l => l.Write(LogLevel.Warning, "odd 3 has a license that is not on the permissive list: GPL-3.0-only"), Times.Once);
    }

    [Fact]
    public async Task Install_BuildsAVirtualEnvironmentFromTheBundledWheelsOnly()
    {
        await bundle.Install(Install());

        ProcessCommand venv = commands.Single(command => command.FileName == "python3");
        Assert.Equal(["-m", "venv", "--clear", Path.Combine(root, "venv")], venv.Arguments);
        ProcessCommand pip = commands.Single(command => command.FileName == VenvPython && command.Arguments.Contains("install"));
        Assert.Contains("--no-index", pip.Arguments);
        Assert.Equal(directory.Combine("payload", "wheels"), pip.Arguments[pip.Arguments.ToList().IndexOf("--find-links") + 1]);
        Assert.Equal(["chromadb==1.5.9", "ollama"], pip.Arguments.TakeLast(2));
        Assert.True(state.IsOwned("chroma"));
    }

    [Fact]
    public async Task Install_PutsTheOllamaPythonClientIntoTheEnvironmentToo()
    {
        await bundle.Install(Install());

        Assert.Contains(commands, command => command.FileName == VenvPython && command.Arguments.Contains("install") && command.Arguments.Contains("ollama"));
    }

    [Fact]
    public async Task Install_InstallsThePythonVenvPackageFromTheBundle()
    {
        await bundle.Install(Install());

        aptInstaller.Verify(i => i.InstallRecorded(directory.Combine("payload", "debs"), state, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Install_PutsTheChromaCommandOnThePath()
    {
        await bundle.Install(Install());

        Assert.Equal(Path.Combine(root, "venv", "bin", "chroma"), new FileInfo(link).LinkTarget);
        Assert.True(state.IsOwned("link"));
    }

    [Fact]
    public async Task Install_ACommandAlreadyOnThePath_IsNotReplaced()
    {
        File.WriteAllText(link, "someone else's");

        await bundle.Install(Install());

        Assert.Equal("someone else's", File.ReadAllText(link));
        Assert.False(state.IsOwned("link"));
    }

    [Fact]
    public async Task Install_CreatesTheServiceAccountAndItsDataFolder()
    {
        await bundle.Install(Install());

        Assert.Contains(commands, command => command.FileName == "groupadd" && command.Arguments.SequenceEqual(new[] { "-r", "chroma" }));
        Assert.Contains(commands, command => command.FileName == "useradd" && command.Arguments.Contains("/usr/sbin/nologin") && command.Arguments.Contains(data));
        Assert.Contains(commands, command => command.FileName == "chown" && command.Arguments.SequenceEqual(new[] { "chroma:chroma", data }));
        Assert.True(Directory.Exists(data));
        Assert.True(state.IsOwned("user"));
    }

    [Fact]
    public async Task Install_AnAccountThatExists_IsNotCreatedAgainOrOwned()
    {
        userExists = true;

        await bundle.Install(Install());

        Assert.DoesNotContain(commands, command => command.FileName == "useradd");
        Assert.False(state.IsOwned("user"));
    }

    [Fact]
    public async Task Install_WritesALocalOnlyServiceUnitThatNamesTheRealFolders()
    {
        await bundle.Install(Install());

        string text = File.ReadAllText(unit);
        Assert.Contains($"ExecStart={Path.Combine(root, "venv", "bin", "chroma")} run --path {data} --host 127.0.0.1 --port 8000", text);
        Assert.Contains("User=chroma", text);
        Assert.Contains("Environment=ANONYMIZED_TELEMETRY=False", text);
        Assert.Contains("Environment=HF_HUB_OFFLINE=1", text);
        Assert.Contains("Restart=on-failure", text);
        Assert.Contains("WantedBy=multi-user.target", text);
    }

    [Fact]
    public async Task Install_EnablesAndStartsTheServiceAndWaitsForIt()
    {
        await bundle.Install(Install());

        systemd.Verify(s => s.Reload(It.IsAny<CancellationToken>()), Times.Once);
        systemd.Verify(s => s.Control("enable", "chroma-db", It.IsAny<CancellationToken>()), Times.Once);
        systemd.Verify(s => s.Control("restart", "chroma-db", It.IsAny<CancellationToken>()), Times.Once);
        log.Verify(l => l.Write(LogLevel.Info, "the ChromaDB service answers"), Times.Once);
    }

    [Fact]
    public async Task Install_SystemdNotRunning_StartsNothingAndSaysSo()
    {
        systemd.SetupGet(s => s.IsRunning).Returns(false);

        await bundle.Install(Install());

        systemd.Verify(s => s.Control(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        log.Verify(l => l.Write(LogLevel.Info, It.Is<string>(text => text.Contains("starts at the next boot"))), Times.Once);
    }

    [Fact]
    public async Task Install_PutsTheEmbeddingModelIntoOllamaAndChecksItIsListed()
    {
        InstallContext context = Install();

        await bundle.Install(context);

        modelFiles.Verify(m => m.Install(context, It.IsAny<CancellationToken>()), Times.Once);
        modelFiles.Verify(m => m.Verify("nomic-embed-text:latest", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Install_TellsHowToUseTheModelBecauseTheDefaultEmbeddingNeedsTheInternet()
    {
        await bundle.Install(Install());

        log.Verify(l => l.Write(LogLevel.Info, It.Is<string>(text => text.Contains("OllamaEmbeddingFunction") && text.Contains("nomic-embed-text:latest") && text.Contains("needs the internet"))), Times.Once);
    }

    [Fact]
    public async Task Install_ANewerChromaDbThatIsThereAlready_IsKept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(VenvPython)!);
        File.WriteAllText(VenvPython, string.Empty);
        chromaVersion = "9.0.0";

        await bundle.Install(Install());

        Assert.DoesNotContain(commands, command => command.FileName == "python3");
        Assert.DoesNotContain(commands, command => command.Arguments.Contains("install"));
        Assert.False(state.IsOwned("chroma"));
    }

    [Fact]
    public async Task Install_AnOlderChromaDbThatIsThereAlready_IsReplaced()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(VenvPython)!);
        File.WriteAllText(VenvPython, string.Empty);
        chromaVersion = "0.5.0";

        await bundle.Install(Install());

        Assert.Contains(commands, command => command.FileName == VenvPython && command.Arguments.Contains("chromadb==1.5.9"));
        Assert.True(state.IsOwned("chroma"));
    }

    [Fact]
    public async Task Uninstall_RemovesTheServiceCommandAndEnvironmentAndKeepsTheData()
    {
        await bundle.Install(Install());
        File.WriteAllText(Path.Combine(data, "chroma.sqlite3"), "data");

        await bundle.Uninstall(Uninstall());

        systemd.Verify(s => s.Control("disable --now", "chroma-db", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(File.Exists(unit));
        Assert.False(File.Exists(link) || new FileInfo(link).LinkTarget is not null);
        Assert.False(Directory.Exists(root));
        Assert.True(File.Exists(Path.Combine(data, "chroma.sqlite3")));
        Assert.DoesNotContain(commands, command => command.FileName == "userdel");
        log.Verify(l => l.Write(LogLevel.Warning, It.Is<string>(text => text.Contains("the chroma user is kept"))), Times.Once);
        modelFiles.Verify(m => m.Uninstall(state, "nomic-embed-text:latest", It.IsAny<CancellationToken>()), Times.Once);
        aptInstaller.Verify(i => i.RemoveAdded(state, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Uninstall_WithNoDataLeft_RemovesTheAccountItCreated()
    {
        await bundle.Install(Install());

        await bundle.Uninstall(Uninstall());

        Assert.Contains(commands, command => command.FileName == "userdel" && command.Arguments.SequenceEqual(new[] { "chroma" }));
        Assert.False(Directory.Exists(data));
    }

    [Fact]
    public async Task Uninstall_ChromaDbThatWasNotInstalledByTheBundle_IsLeftInPlace()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(VenvPython)!);
        File.WriteAllText(VenvPython, string.Empty);
        chromaVersion = "9.0.0";
        await bundle.Install(Install());

        await bundle.Uninstall(Uninstall());

        Assert.True(Directory.Exists(root));
        log.Verify(l => l.Write(LogLevel.Info, It.Is<string>(text => text.Contains("was not installed by this bundle"))), Times.Once);
    }
}
