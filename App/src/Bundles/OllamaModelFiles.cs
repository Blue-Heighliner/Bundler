namespace BlueHeighliner.Bundler;

/// <summary>
/// Puts the Ollama model a bundle carries (<c>ollama-models</c> and <c>model.info</c> in its payload) into Ollama's model folder, and takes it out
/// again. An identical model that is installed already is skipped; a changed model of the same name replaces the old one.
/// </summary>
internal interface IOllamaModelFiles
{
    /// <summary>
    /// Installs the model of a bundle.
    /// </summary>
    /// <param name="install">The install of the bundle, which records the model in its state.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The name of the model, such as <c>nomic-embed-text:latest</c>.</returns>
    /// <exception cref="InvalidOperationException">Ollama is missing or too old for the model, or there is not enough free space.</exception>
    Task<string> Install(InstallContext install, CancellationToken cancellation = default);

    /// <summary>
    /// Adds the steps that take the model out of Ollama's folder to an uninstall script, unless it was there before the bundle and is not the bundle's to remove.
    /// </summary>
    /// <param name="script">The uninstall script of the bundle.</param>
    /// <param name="state">The state of the bundle.</param>
    /// <param name="fallbackName">The name to report when the state has none.</param>
    /// <returns>The name of the model.</returns>
    string Uninstall(UninstallScript script, IBundleState state, string fallbackName);

    /// <summary>
    /// Waits for Ollama to list a model. Does nothing when systemd is not running, because Ollama starts at the next boot then.
    /// </summary>
    /// <param name="name">The model name.</param>
    /// <param name="cancellation">Stops the wait when cancelled.</param>
    /// <returns>A task that completes when the model is listed.</returns>
    /// <exception cref="InvalidOperationException">Ollama does not list the model.</exception>
    Task Verify(string name, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class OllamaModelFiles(IHashing hashing, ISystemd systemd, IProcessRunner runner, IVersions versions, ILog log, string models = "/usr/share/ollama/.ollama/models") : IOllamaModelFiles
{
    /// <inheritdoc />
    public async Task<string> Install(InstallContext install, CancellationToken cancellation = default)
    {
        Dictionary<string, string> info = File.ReadAllLines(Path.Combine(install.Payload, "model.info")).Select(line => line.Split('=', 2)).ToDictionary(parts => parts[0], parts => parts[1]);
        string name = info["model"];
        await CheckOllama(name, info["requires_ollama"]);

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
            await Copy(install, source, manifest, blobs, long.Parse(info["bytes"]), oldManifest, cancellation);
        }

        install.State.Write("model", name);
        install.State.Write("manifest", manifest);
        return name;
    }

    /// <inheritdoc />
    public string Uninstall(UninstallScript script, IBundleState state, string fallbackName)
    {
        string name = state.Read("model")?.Trim() ?? fallbackName;
        string? manifest = state.Read("manifest")?.Trim();
        if (!state.IsOwned("model"))
        {
            if (!string.IsNullOrEmpty(manifest))
            {
                script.Info($"{name} was already on this computer and was not installed by this bundle, so it is left in place");
            }
        }
        else if (!string.IsNullOrEmpty(manifest))
        {
            script.Add("model-remove", models, manifest);
        }

        return name;
    }

    /// <inheritdoc />
    public async Task Verify(string name, CancellationToken cancellation = default)
    {
        if (!systemd.IsRunning)
        {
            log.Info("systemd is not running here, Ollama lists the model at the next boot");
            return;
        }

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

    private string BlobPath(string hex)
        => Path.Combine(models, "blobs", "sha256-" + hex);

    private string PayloadBlob(InstallContext install, string hex)
        => Path.Combine(install.Payload, "ollama-models", "blobs", "sha256-" + hex);

    private IEnumerable<string> Digests(string manifest)
        => Regex.Matches(manifest, "sha256:([0-9a-f]{64})").Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal);

    private async Task CheckOllama(string name, string needed)
    {
        ProcessResult version = await runner.Try("ollama", ["--version"]);
        if (!version.Success && version.ExitCode == 127)
        {
            throw new InvalidOperationException("Ollama is not installed (install the ollama bundle first)");
        }

        string have = versions.Find(version.Output + version.Error);
        if (needed.Length > 0 && versions.Compare(have, needed) < 0)
        {
            throw new InvalidOperationException($"Ollama {have} is older than {needed}, which {name} needs");
        }
    }

    private async Task Copy(InstallContext install, string source, string manifest, string[] blobs, long bytes, string? oldManifest, CancellationToken cancellation)
    {
        // On one disk the files are renamed, not copied, so only a margin is needed.
        bool sameDisk = await Device(install.Payload) == await Device(models);
        long needed = sameDisk ? 2_000_000_000 : bytes + 2_000_000_000;
        long free = new DriveInfo(models).AvailableFreeSpace;
        if (free < needed)
        {
            throw new InvalidOperationException($"not enough free space for the model: {free / 1_000_000_000} GB free, {needed / 1_000_000_000} GB needed");
        }

        log.Step($"Copying {blobs.Length} model files ({bytes} bytes)");
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
                File.DeleteIfPresent(BlobPath(hex));
                File.DeleteIfPresent(Path.Combine(models, "metadata", $"sha256-{hex}.json"));
            }
        }
    }

    private async Task<string> Device(string path)
        => await runner.Capture("stat", ["-c", "%d", path]);
}
