namespace BlueHeighliner.Bundler.Tests;

public sealed class OllamaModelFilesTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IProcessRunner> runner = new();
    private readonly Mock<ISystemd> systemd = new();
    private readonly Mock<ILog> log = new();
    private readonly List<ProcessCommand> commands = [];
    private readonly OllamaModelFiles files;
    private readonly BundleState state;
    private readonly string models;
    private string ollamaVersion = "ollama version is 0.12.0";
    private int ollamaExit;
    private string listing = "NAME ID SIZE MODIFIED\nnomic-embed-text:latest abc 274 MB now\n";

    public OllamaModelFilesTests()
    {
        models = directory.Combine("models");
        Directory.CreateDirectory(models);
        state = new(directory.Combine("state"));
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ProcessCommand command, CancellationToken _) =>
        {
            commands.Add(command);
            return command.FileName switch
            {
                "ollama" when command.Arguments.Contains("--version") => new ProcessResult(ollamaExit, ollamaVersion, string.Empty),
                "ollama" when command.Arguments.Contains("list") => new ProcessResult(0, listing, string.Empty),
                "stat" => new ProcessResult(0, "1\n", string.Empty),
                _ => new ProcessResult(0, string.Empty, string.Empty),
            };
        });
        systemd.SetupGet(s => s.IsRunning).Returns(true);
        files = new(new Hashing(), systemd.Object, runner.Object, new Versions(), log.Object, models);
    }

    public void Dispose()
        => directory.Dispose();

    private static string Hex(string seed)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    private InstallContext Payload(string name, params string[] blobs)
    {
        string root = directory.Combine("payload");
        string manifest = string.Join(',', blobs.Select(blob => $"{{\"digest\":\"sha256:{Hex(blob)}\"}}"));
        directory.Write("payload/model.info", $"model={name}\nrequires_ollama=0.1.26\nbytes=100\n");
        directory.Write($"payload/ollama-models/manifests/registry.ollama.ai/library/{name.Split(':')[0]}/{name.Split(':')[1]}", $"[{manifest}]");
        foreach (string blob in blobs)
        {
            directory.Write($"payload/ollama-models/blobs/sha256-{Hex(blob)}", blob);
        }

        return new() { Payload = root, State = state, Users = [], Info = new() { Name = "x" } };
    }

    private string Manifest(string name)
        => Path.Combine(models, "manifests", "registry.ollama.ai", "library", name.Split(':')[0], name.Split(':')[1]);

    private string Blob(string blob)
        => Path.Combine(models, "blobs", "sha256-" + Hex(blob));

    [Fact]
    public async Task Install_PutsTheManifestAndFilesInOllamasFolderAndRecordsThem()
    {
        InstallContext install = Payload("nomic-embed-text:latest", "weights", "params");

        string name = await files.Install(install);

        Assert.Equal("nomic-embed-text:latest", name);
        Assert.True(File.Exists(Manifest(name)));
        Assert.Equal("weights", File.ReadAllText(Blob("weights")));
        Assert.Equal("params", File.ReadAllText(Blob("params")));
        Assert.True(state.IsOwned("model"));
        Assert.Equal(name, state.Read("model"));
        Assert.Equal(Manifest(name), state.Read("manifest"));
        Assert.Contains(commands, command => command.FileName == "chown" && command.Arguments.Contains("ollama:ollama"));
    }

    [Fact]
    public async Task Install_AnIdenticalModelThatIsThereAlready_IsSkippedAndNotOwned()
    {
        await files.Install(Payload("m:latest", "weights"));
        state.End();
        BundleState second = new(directory.Combine("state2"));
        InstallContext again = Payload("m:latest", "weights") with { State = second };

        await files.Install(again);

        Assert.False(second.IsOwned("model"));
        log.Verify(l => l.Write(LogLevel.Info, It.Is<string>(text => text.Contains("already installed, skipped"))), Times.Once);
    }

    [Fact]
    public async Task Install_AChangedModelOfTheSameName_ReplacesItAndRemovesTheFilesNothingUsesAnymore()
    {
        await files.Install(Payload("m:latest", "old-weights", "shared"));
        Directory.Delete(directory.Combine("payload"), recursive: true);
        InstallContext changed = Payload("m:latest", "new-weights", "shared");

        await files.Install(changed);

        Assert.False(File.Exists(Blob("old-weights")));
        Assert.Equal("new-weights", File.ReadAllText(Blob("new-weights")));
        Assert.True(File.Exists(Blob("shared")));
        log.Verify(l => l.Write(LogLevel.Info, It.Is<string>(text => text.Contains("replacing the installed version"))), Times.Once);
    }

    [Fact]
    public async Task Install_OllamaNotInstalled_Throws()
    {
        ollamaExit = 127;
        ollamaVersion = string.Empty;

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => files.Install(Payload("m:latest", "w")));

        Assert.Contains("install the ollama bundle first", error.Message);
        Assert.False(File.Exists(Manifest("m:latest")));
    }

    [Fact]
    public async Task Install_OllamaOlderThanTheModelNeeds_Throws()
    {
        ollamaVersion = "ollama version is 0.1.0";

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => files.Install(Payload("m:latest", "w")));

        Assert.Contains("is older than 0.1.26", error.Message);
    }

    [Fact]
    public async Task Uninstall_RemovesTheModelTheBundleInstalled()
    {
        await files.Install(Payload("m:latest", "weights"));

        string name = await files.Uninstall(state, "fallback:1");

        Assert.Equal("m:latest", name);
        Assert.False(File.Exists(Manifest("m:latest")));
        Assert.False(File.Exists(Blob("weights")));
    }

    [Fact]
    public async Task Uninstall_ModelThatWasThereBefore_IsLeftInPlace()
    {
        await files.Install(Payload("m:latest", "weights"));
        state.End();
        BundleState second = new(directory.Combine("state2"));
        await files.Install(Payload("m:latest", "weights") with { State = second });

        await files.Uninstall(second, "fallback:1");

        Assert.True(File.Exists(Manifest("m:latest")));
        Assert.True(File.Exists(Blob("weights")));
    }

    [Fact]
    public async Task Uninstall_NoRecordedModel_ReportsTheFallbackName()
        => Assert.Equal("fallback:1", await files.Uninstall(state, "fallback:1"));

    [Fact]
    public async Task Verify_ListedModel_Passes()
        => await files.Verify("nomic-embed-text:latest");

    [Fact]
    public async Task Verify_ModelThatIsNotListed_Throws()
    {
        listing = "NAME ID SIZE MODIFIED\nother:latest abc 1 GB now\n";

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => files.Verify("nomic-embed-text:latest"));

        Assert.Contains("does not list nomic-embed-text:latest", error.Message);
    }

    [Fact]
    public async Task Verify_SystemdNotRunning_DoesNotAskOllama()
    {
        systemd.SetupGet(s => s.IsRunning).Returns(false);

        await files.Verify("nomic-embed-text:latest");

        Assert.DoesNotContain(commands, command => command.FileName == "ollama");
    }
}
