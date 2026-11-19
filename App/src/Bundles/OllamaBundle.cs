namespace BlueHeighliner.Bundler;

/// <summary>
/// Ollama as a local-only systemd service on <c>127.0.0.1:11434</c> that runs AI models on this computer. Models come from other
/// bundles, such as <c>gemma</c>.
/// </summary>
internal sealed class OllamaBundle(
    IResources resources,
    IReleaseSources sources,
    IDownloads downloads,
    IAptBuilder apt,
    IAptInstaller aptInstaller,
    IDecisions decisions,
    IVersions versions,
    IMachine machine,
    ISystemd systemd,
    IProcessRunner runner,
    ILog log) : BundleBase
{
    private static readonly string dropInFolder = "/etc/systemd/system/ollama.service.d";

    /// <inheritdoc />
    public override string Name => "ollama";

    /// <inheritdoc />
    public override string Description => "Run AI models on this computer with Ollama, as a background service that only this computer can reach.";

    /// <inheritdoc />
    public override int Order => 22;

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        log.Step("Ollama");
        GitHubRelease ollama = await sources.LatestGitHub("ollama/ollama", [@"^ollama-linux-amd64\.tar\.zst$"], cancellation);
        await downloads.Place(await downloads.Fetch(ollama.Assets[0].Url, ollama.Assets[0].FileName, ollama.Assets[0].Hash, cancellation), Path.Combine(directory, "ollama", ollama.Assets[0].FileName), cancellation);

        log.Step("Ubuntu packages (zstd for unpacking Ollama)");
        await apt.Prepare([], cancellation);
        await apt.Download(Path.Combine(directory, "debs"), new() { Support = ["zstd"] }, cancellation);
        return new() { Components = [$"Ollama {ollama.Tag}"] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        log.Step("Ubuntu packages");
        await aptInstaller.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);

        log.Step("Ollama");
        string bundled = versions.Find(context.Info.Components.First(component => component.StartsWith("Ollama ", StringComparison.Ordinal)));
        string? existing = machine.FindExecutable("ollama");
        string? have = null;
        if (existing is not null)
        {
            ProcessResult result = await runner.Try(existing, ["--version"]);
            have = versions.Find(result.Output + result.Error);
        }

        string prefix = existing is null ? "/usr" : Path.GetDirectoryName(Path.GetDirectoryName(existing))!;
        bool changed = false;
        if (decisions.Install("Ollama", have, bundled))
        {
            string archive = Directory.EnumerateFiles(Path.Combine(context.Payload, "ollama"), "ollama-linux-amd64.tar.zst").First();
            await runner.Run(new() { FileName = "tar", Arguments = ["--zstd", "-xf", archive, "-C", prefix] }, cancellation);
            context.State.MarkOwned("ollama");
            context.State.Write("ollama-prefix", prefix);
            changed = true;
        }

        string binary = (have is null || changed) ? Path.Combine(prefix, "bin", "ollama") : existing!;
        if (!(await runner.Try("getent", ["group", "ollama"])).Success)
        {
            await runner.Run(new() { FileName = "groupadd", Arguments = ["-r", "ollama"] }, cancellation);
        }

        if (!(await runner.Try("id", ["ollama"])).Success)
        {
            await runner.Run(new() { FileName = "useradd", Arguments = ["-r", "-g", "ollama", "-d", "/usr/share/ollama", "-m", "-s", "/usr/sbin/nologin", "ollama"] }, cancellation);
        }

        foreach (string group in new[] { "render", "video" })
        {
            if ((await runner.Try("getent", ["group", group])).Success)
            {
                await runner.Try("usermod", ["-aG", group, "ollama"]);
            }
        }

        string unit = resources.Text("ollama/ollama.service").Replace("ExecStart=/usr/bin/ollama", $"ExecStart={binary}", StringComparison.Ordinal);
        changed |= WriteIfChanged("/etc/systemd/system/ollama.service", unit);
        changed |= WriteIfChanged(Path.Combine(dropInFolder, "zz-bundler.conf"), resources.Text("ollama/ollama-bundler.conf"));
        if (!systemd.IsRunning)
        {
            log.Info("systemd is not running here, the Ollama service will start at the next boot");
            return;
        }

        await systemd.Reload(cancellation);
        await systemd.Control("enable", "ollama", cancellation);
        await systemd.Control(changed ? "restart" : "start", "ollama", cancellation);
        for (int attempt = 0; attempt < 30 && !(await runner.Try(binary, ["list"])).Success; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellation);
        }
    }

    /// <inheritdoc />
    public override UninstallScript Script(UninstallContext context)
    {
        UninstallScript script = new();
        script.Add("systemctl", "disable --now", "ollama");
        script.Remove("/etc/systemd/system/ollama.service");
        script.Remove(Path.Combine(dropInFolder, "zz-bundler.conf"));
        script.RemoveEmpty(dropInFolder);
        script.Add("daemon-reload");
        if (!context.State.IsOwned("ollama"))
        {
            script.Info("Ollama was already on this computer and was not installed by this bundle, so it is left in place");
        }
        else
        {
            AddFileRemoval(script, context);
        }

        script.Add("apt-remove-added");
        return script;
    }

    private void AddFileRemoval(UninstallScript script, UninstallContext context)
    {
        string prefix = context.State.Read("ollama-prefix")?.Trim() ?? "/usr";
        script.Remove(Path.Combine(prefix, "bin", "ollama"));
        script.RemoveTree(Path.Combine(prefix, "lib", "ollama"));
        string models = "/usr/share/ollama/.ollama/models";
        string[] folders = [Path.Combine(models, "manifests"), Path.Combine(models, "manifests-v2")];
        script.IfFiles(true, folders, then => then.Warn("Ollama models are still in /usr/share/ollama (the ollama user is kept). Delete that folder by hand if you do not need them."));
        script.IfFiles(false, folders, then =>
        {
            then.Add("run", "userdel", "ollama");
            then.RemoveTree("/usr/share/ollama");
        });
    }

    private bool WriteIfChanged(string path, string text)
    {
        if (File.Exists(path) && File.ReadAllText(path) == text)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return true;
    }

    private void DeleteIfEmpty(string folder)
    {
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Directory.Delete(folder);
        }
    }
}
