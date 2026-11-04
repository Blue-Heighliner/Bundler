namespace BlueHeighliner.Bundler;

/// <summary>
/// Gemma 4 26B-A4B (25.2B parameters, Q4_K_M) for Ollama, set as the model of Cline in VS Code. An identical model that is
/// already installed is skipped; a changed model of the same name replaces the old one.
/// </summary>
internal sealed class GemmaBundle(IOllamaRegistry registry, IHashing hashing, IUsers users, IUserFiles userFiles, ISystemd systemd, IProcessRunner runner, IVersions versions, ILog log) : BundleBase
{
    private static readonly int context = 32768;

    private static readonly string models = "/usr/share/ollama/.ollama/models";

    private static readonly string dropIn = "/etc/systemd/system/ollama.service.d/zz-bundler-gemma.conf";

    private readonly string model = "gemma4:26b";

    /// <inheritdoc />
    public override string Name => "gemma";

    /// <inheritdoc />
    public override string Description => "A local AI model that helps write code in VS Code, running entirely on this computer.";

    /// <inheritdoc />
    public override int Order => 30;

    /// <inheritdoc />
    public override IReadOnlyList<string> Requires { get; } = ["dev"];

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
        Dictionary<string, string> info = File.ReadAllLines(Path.Combine(install.Payload, "model.info")).Select(line => line.Split('=', 2)).ToDictionary(parts => parts[0], parts => parts[1]);
        string name = info["model"];
        await CheckRequirements(name, info["requires_ollama"]);

        string source = Directory.EnumerateFiles(Path.Combine(install.Payload, "ollama-models", "manifests"), "*", SearchOption.AllDirectories).First();
        string relative = Path.GetRelativePath(Path.Combine(install.Payload, "ollama-models"), source);
        string manifest = Path.Combine(models, relative);
        Directory.CreateDirectory(Path.Combine(models, "blobs"));
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        string[] blobs = Digests(File.ReadAllText(source)).ToArray();
        string? oldManifest = null;
        bool installed = false;
        if (File.Exists(manifest))
        {
            installed = File.ReadAllBytes(manifest).AsSpan().SequenceEqual(File.ReadAllBytes(source))
                && blobs.All(hex => File.Exists(BlobPath(hex)) && new FileInfo(BlobPath(hex)).Length == new FileInfo(PayloadBlob(install, hex)).Length);
            if (installed)
            {
                log.Info($"{name}: already installed, skipped");
            }
            else
            {
                log.Info($"{name}: replacing the installed version with the one in the bundle");
                oldManifest = File.ReadAllText(manifest);
            }
        }
        else
        {
            log.Info($"{name}: not on this computer, installing");
        }

        if (!installed)
        {
            await CopyModel(install, source, manifest, blobs, long.Parse(info["bytes"]), oldManifest, cancellation);
        }

        install.State.Write("model", name);
        install.State.Write("manifest", manifest);
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
        string name = uninstall.State.Read("model")?.Trim() ?? model;
        foreach (string user in uninstall.Users.Where(user => Directory.Exists(users.Home(user))))
        {
            await userFiles.UnsetJson(user, ClinePath(user), ClineSettings(name), cancellation);
        }

        string? manifest = uninstall.State.Read("manifest")?.Trim();
        if (!uninstall.State.IsOwned("model"))
        {
            if (!string.IsNullOrEmpty(manifest))
            {
                log.Info($"{name} was already on this computer and was not installed by this bundle, so it is left in place");
            }
        }
        else if (!string.IsNullOrEmpty(manifest) && File.Exists(manifest))
        {
            string[] digests = Digests(File.ReadAllText(manifest)).ToArray();
            File.Delete(manifest);
            File.Delete(Path.Combine(models, "manifests-v2", Path.GetRelativePath(Path.Combine(models, "manifests"), manifest)));
            RemoveUnused(digests);
        }

        File.Delete(dropIn);
        await systemd.Reload(cancellation);
        await systemd.Control("restart", "ollama", cancellation);
    }

    private string BlobPath(string hex)
        => Path.Combine(models, "blobs", "sha256-" + hex);

    private string PayloadBlob(InstallContext install, string hex)
        => Path.Combine(install.Payload, "ollama-models", "blobs", "sha256-" + hex);

    private string ClinePath(string user)
        => Path.Combine(users.Home(user), ".cline", "data", "globalState.json");

    private JsonObject ClineSettings(string name)
        => new() { ["actModeOllamaModelId"] = name, ["planModeOllamaModelId"] = name, ["ollamaApiOptionsCtxNum"] = context.ToString() };

    private IEnumerable<string> Digests(string manifest)
        => Regex.Matches(manifest, "sha256:([0-9a-f]{64})").Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal);

    private async Task CheckRequirements(string name, string needed)
    {
        ProcessResult version = await runner.Try("ollama", ["--version"]);
        if (!version.Success && version.ExitCode == 127)
        {
            throw new InvalidOperationException("Ollama is not installed (install the dev bundle first)");
        }

        string have = versions.Find(version.Output + version.Error);
        if (needed.Length > 0 && versions.Compare(have, needed) < 0)
        {
            throw new InvalidOperationException($"Ollama {have} is older than {needed}, which {name} needs");
        }

        long memory = File.ReadLines("/proc/meminfo").Where(line => line.StartsWith("MemTotal:", StringComparison.Ordinal)).Select(line => long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1])).FirstOrDefault();
        if (name == "gemma4:26b" && memory / 1048576 < 28)
        {
            log.Warn($"this computer has about {memory / 1048576} GB of RAM; Gemma 4 26B needs roughly 24 GB or more and will be very slow or fail to load");
        }
    }

    private async Task CopyModel(InstallContext install, string source, string manifest, string[] blobs, long bytes, string? oldManifest, CancellationToken cancellation)
    {
        long free = new DriveInfo(models).AvailableFreeSpace;
        if (free < bytes + 2_000_000_000)
        {
            throw new InvalidOperationException($"not enough free space for the model: {free / 1_000_000_000} GB free, {(bytes + 2_000_000_000) / 1_000_000_000} GB needed");
        }

        log.Step($"Copying {blobs.Length} model files ({bytes} bytes)");
        bool sameDisk = await Device(install.Payload) == await Device(models);
        for (int index = 0; index < blobs.Length; index++)
        {
            string hex = blobs[index];
            string from = PayloadBlob(install, hex);
            if (File.Exists(BlobPath(hex)) && new FileInfo(BlobPath(hex)).Length == new FileInfo(from).Length && await hashing.Sha256(BlobPath(hex), cancellation) == hex)
            {
                log.Info($"file {index + 1} of {blobs.Length} is already in place");
                continue;
            }

            log.Info($"file {index + 1} of {blobs.Length} ({new FileInfo(from).Length / 1_000_000} MB)");
            File.Move(from, BlobPath(hex) + ".partial", overwrite: true);
            if (!sameDisk && await hashing.Sha256(BlobPath(hex) + ".partial", cancellation) != hex)
            {
                throw new InvalidDataException($"the copy of model file {hex} is damaged, run the install again");
            }

            File.Move(BlobPath(hex) + ".partial", BlobPath(hex), overwrite: true);
        }

        File.Copy(source, manifest, overwrite: true);
        await runner.Run(new() { FileName = "chown", Arguments = ["-R", "ollama:ollama", Path.GetDirectoryName(models)!] }, cancellation);
        if (oldManifest is not null)
        {
            RemoveUnused(Digests(oldManifest).ToArray());
        }

        install.State.MarkOwned("model");
    }

    private void RemoveUnused(string[] digests)
    {
        foreach (string hex in digests)
        {
            bool used = new[] { "manifests", "manifests-v2" }.Select(folder => Path.Combine(models, folder)).Where(Directory.Exists)
                .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)).Any(file => File.ReadAllText(file).Contains("sha256:" + hex, StringComparison.Ordinal));
            if (!used)
            {
                File.Delete(BlobPath(hex));
                File.Delete(Path.Combine(models, "metadata", $"sha256-{hex}.json"));
            }
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
        string listed = string.Empty;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            ProcessResult result = await runner.Try("ollama", ["list"]);
            if (result.Success)
            {
                listed = result.Output;
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellation);
        }

        if (!listed.Contains(name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Ollama does not list {name} after the install");
        }

        log.Info(listed.Trim());
    }

    private async Task<string> Device(string path)
        => await runner.Capture("stat", ["-c", "%d", path]);
}
