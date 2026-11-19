namespace BlueHeighliner.Bundler;

/// <summary>
/// Gemma 4 26B-A4B (25.2B parameters, Q4_K_M) for Ollama, set as the model of Cline. An identical model that is
/// already installed is skipped; a changed model of the same name replaces the old one.
/// </summary>
internal sealed class GemmaBundle(IOllamaRegistry registry, IOllamaModelFiles modelFiles, IUsers users, IUserFiles userFiles, ISystemd systemd, ILog log) : BundleBase
{
    private static readonly int context = 32768;

    private static readonly string dropIn = "/etc/systemd/system/ollama.service.d/zz-bundler-gemma.conf";

    private readonly string model = "gemma4:26b";

    /// <inheritdoc />
    public override string Name => "gemma";

    /// <inheritdoc />
    public override string Description => "A local AI model for writing code and working on projects, running entirely on this computer.";

    /// <inheritdoc />
    public override int Order => 30;

    /// <inheritdoc />
    public override IReadOnlyList<string> Requires { get; } = ["ollama"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Resumable { get; } = ["ollama-models"];

    private string ModelName => model.Split(':')[0];

    private string Tag => model.Contains(':') ? model.Split(':')[1] : "latest";

    /// <inheritdoc />
    public override async Task<bool> IsUnchanged(IReadOnlyDictionary<string, string> latest, CancellationToken cancellation = default)
        => latest.TryGetValue($"ollama-models/manifests/registry.ollama.ai/library/{ModelName}/{Tag}", out string? known) && known == await registry.ManifestHash(ModelName, Tag, cancellation);

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        log.Step($"Downloading {model} from the Ollama registry");
        OllamaModel downloaded = await registry.Fetch(ModelName, Tag, Path.Combine(directory, "ollama-models"), cancellation);
        if (model == "gemma4:26b" && (downloaded.Parameters != "25.2B" || downloaded.Quantization != "Q4_K_M"))
        {
            throw new InvalidOperationException($"gemma4:26b is now {downloaded.Parameters} {downloaded.Quantization}, not 25.2B Q4_K_M. Check what the registry changed before using this bundle.");
        }

        await File.WriteAllTextAsync(Path.Combine(directory, "model.info"), $"model={model}\nrequires_ollama={downloaded.Requires}\nbytes={downloaded.Bytes}\n", cancellation);
        return new() { Components = [$"{model} {downloaded.Parameters} {downloaded.Quantization}"] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext install, CancellationToken cancellation = default)
    {
        CheckMemory();
        string name = await modelFiles.Install(install, cancellation);
        await ConfigureOllama(name, cancellation);
        log.Step("Cline model for each user");
        foreach (string user in install.Users)
        {
            log.Info($"user {user}");
            await userFiles.SetJson(user, ClinePath(user), ClineSettings(name), cancellation: cancellation);
        }
    }

    /// <inheritdoc />
    public override async Task Uninstall(UninstallContext uninstall, CancellationToken cancellation = default)
    {
        string name = await modelFiles.Uninstall(uninstall.State, model, cancellation);
        foreach (string user in uninstall.Users.Where(user => Directory.Exists(users.Home(user))))
        {
            await userFiles.UnsetJson(user, ClinePath(user), ClineSettings(name), cancellation);
        }

        File.DeleteIfPresent(dropIn);
        await systemd.Reload(cancellation);
        await systemd.Control("restart", "ollama", cancellation);
    }

    private string ClinePath(string user)
        => Path.Combine(users.Home(user), ".cline", "data", "globalState.json");

    private JsonObject ClineSettings(string name)
        => new() { ["actModeOllamaModelId"] = name, ["planModeOllamaModelId"] = name, ["ollamaApiOptionsCtxNum"] = context.ToString() };

    private void CheckMemory()
    {
        long memory = File.ReadLines("/proc/meminfo").Where(line => line.StartsWith("MemTotal:", StringComparison.Ordinal)).Select(line => long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1])).FirstOrDefault();
        if (memory / 1048576 < 28)
        {
            log.Warn($"this computer has about {memory / 1048576} GB of RAM; Gemma 4 26B needs roughly 24 GB or more and will be very slow or fail to load");
        }
    }

    private async Task ConfigureOllama(string name, CancellationToken cancellation)
    {
        log.Step("Ollama settings");
        Directory.CreateDirectory(Path.GetDirectoryName(dropIn)!);
        await File.WriteAllTextAsync(dropIn, $"# Written by the gemma bundle of Bundler.\n[Service]\nEnvironment=\"OLLAMA_CONTEXT_LENGTH={context}\"\n", cancellation);
        if (!systemd.IsRunning)
        {
            log.Info("systemd is not running here, Ollama picks up the settings at the next boot");
            return;
        }

        await systemd.Reload(cancellation);
        await systemd.Control("restart", "ollama", cancellation);
        await modelFiles.Verify(name, cancellation);
    }

}
