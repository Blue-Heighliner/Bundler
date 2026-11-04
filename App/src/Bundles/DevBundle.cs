namespace BlueHeighliner.Bundler;

/// <summary>
/// Everything for C# development without internet, set up to work together: the .NET SDK, Ollama as a local service, VS Code
/// with the C#, .NET Install Tool, Git Graph and Cline extensions (not the C# Dev Kit, whose license does not allow general
/// commercial use), and the offline NuGet feed that restore uses by default.
/// </summary>
internal sealed class DevBundle(
    IResources resources,
    IReleaseSources sources,
    IDotNetSdks sdks,
    IDownloads downloads,
    IMarketplace marketplace,
    IAptBuilder apt,
    IAptInstaller aptInstaller,
    INuGetBundleBuilder nugetBuilder,
    INuGetFeed feed,
    IDecisions decisions,
    IVersions versions,
    IMachine machine,
    IUsers users,
    IUserFiles userFiles,
    ISystemd systemd,
    ITextBlocks blocks,
    IProcessRunner runner,
    ILog log) : BundleBase
{
    private static readonly string[] extensions = ["ms-dotnettools.csharp", "ms-dotnettools.vscode-dotnet-runtime", "mhutchie.git-graph", "saoudrizwan.claude-dev"];

    private static readonly string[] templates = ["console", "classlib", "worker", "webapi", "web", "mvc", "razor", "blazor", "grpc"];

    private static readonly string dotnetRoot = "/usr/share/dotnet";

    private static readonly string dropInFolder = "/etc/systemd/system/ollama.service.d";

    private static readonly NuGetBundleSpec spec = new() { PackageList = "dev/packages.txt", WarmTemplates = templates };

    private static readonly JsonObject clineState = new()
    {
        ["welcomeViewCompleted"] = true,
        ["telemetrySetting"] = "disabled",
        ["actModeApiProvider"] = "ollama",
        ["planModeApiProvider"] = "ollama",
        ["ollamaBaseUrl"] = "http://127.0.0.1:11434",
    };

    private static readonly JsonObject clineSettings = new() { ["telemetryOptOut"] = true, ["autoUpdateEnabled"] = false };

    /// <inheritdoc />
    public override string Name => "dev";

    /// <inheritdoc />
    public override string Description => "Write C# without internet: the .NET SDK, VS Code with C# support, a local AI coding assistant and an offline package feed.";

    /// <inheritdoc />
    public override int Order => 20;

    /// <inheritdoc />
    public override IReadOnlyList<string> Requires { get; } = ["git"];

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        log.Step(".NET SDK");
        DotNetRelease dotnet = await sdks.Latest(cancellation);
        await downloads.Place(await sdks.Tarball(cancellation), Path.Combine(directory, "dotnet", dotnet.FileName), cancellation);

        log.Step("Visual Studio Code");
        VsCodeRelease code = await sources.LatestVsCode(cancellation);
        string codeDeb = await downloads.Fetch(code.Url, code.FileName, code.Hash, cancellation);

        log.Step("Ollama");
        GitHubRelease ollama = await sources.LatestGitHub("ollama/ollama", [@"^ollama-linux-amd64\.tar\.zst$"], cancellation);
        await downloads.Place(await downloads.Fetch(ollama.Assets[0].Url, ollama.Assets[0].FileName, ollama.Assets[0].Hash, cancellation), Path.Combine(directory, "ollama", ollama.Assets[0].FileName), cancellation);

        log.Step("VS Code extensions");
        IReadOnlyList<ExtensionPackage> packages = await marketplace.Fetch(Path.Combine(directory, "extensions"), extensions, cancellation);

        log.Step("Ubuntu packages (VS Code, the libraries .NET needs, zstd for unpacking Ollama)");
        await apt.Prepare([], cancellation);
        string icu = await apt.Find(@"libicu[0-9]+", cancellation) ?? throw new InvalidOperationException("could not find the ICU library in the Ubuntu 26.04 archive");
        string ssl = await apt.Find(@"libssl[0-9]+(t64)?", cancellation) ?? throw new InvalidOperationException("could not find the OpenSSL library in the Ubuntu 26.04 archive");
        await apt.Download(
            Path.Combine(directory, "debs"),
            new() { Services = [codeDeb], Support = ["zstd", "ca-certificates", "tzdata", "zlib1g", "libstdc++6", "libgcc-s1", "xdg-utils", icu, ssl] },
            cancellation);

        log.Step("Offline NuGet feed");
        NuGetBuildResult nuget = await nugetBuilder.Build(spec, directory, cancellation);

        List<string> components = [$".NET SDK {dotnet.Version} (runtime {dotnet.Runtime})", $"Visual Studio Code {code.Version}", $"Ollama {ollama.Tag}"];
        components.AddRange(packages.Select(package => $"VS Code extension {package.Id} {package.Version}"));
        components.Add(nuget.Components[^1]);
        return new() { Components = components };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        log.Step("Ubuntu packages");
        await runner.Run(new() { FileName = "debconf-set-selections", Input = "code code/add-microsoft-repo boolean false\n" }, cancellation);
        await aptInstaller.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);

        string dotnetBin = await InstallDotNet(context, cancellation);
        await InstallOllama(context, cancellation);

        log.Step("Offline NuGet feed");
        feed.Add(Path.Combine(context.Payload, "nuget"), context.State);
        feed.WriteConfig("/usr/local/share/NuGet/Config/bundler.config");
        if (File.Exists(Path.Combine(context.Payload, "nuget-licenses.tsv")))
        {
            File.Copy(Path.Combine(context.Payload, "nuget-licenses.tsv"), Path.Combine(feed.Directory, "LICENSES.tsv"), overwrite: true);
        }

        log.Step("git defaults");
        await runner.Run(new() { FileName = "git", Arguments = ["config", "--system", "init.defaultBranch", "main"] }, cancellation);
        await runner.Run(new() { FileName = "git", Arguments = ["config", "--system", "core.editor", "code --wait"] }, cancellation);

        log.Step("VS Code, NuGet and Cline settings for each user");
        string extensionFolder = Path.Combine(context.State.Directory, "extensions");
        if (Directory.Exists(extensionFolder))
        {
            Directory.Delete(extensionFolder, recursive: true);
        }

        Directory.CreateDirectory(context.State.Directory);
        CopyFolder(Path.Combine(context.Payload, "extensions"), extensionFolder);
        context.State.Write("vscode-settings.json", resources.Text("dev/vscode-settings.json").Replace("/usr/share/dotnet/dotnet", dotnetBin, StringComparison.Ordinal));
        if (context.Users.Count == 0)
        {
            log.Warn("no normal user accounts found, so no VS Code settings were written. Run the install again after creating one.");
        }

        foreach (string user in context.Users)
        {
            await SetUpUser(user, context, cancellation);
        }
    }

    /// <inheritdoc />
    public override async Task Uninstall(UninstallContext context, CancellationToken cancellation = default)
    {
        string settings = context.State.Read("vscode-settings.json") ?? resources.Text("dev/vscode-settings.json");
        IReadOnlyList<string> installed = ExtensionRows(context.State.Directory).Select(row => row.Id).ToList();
        foreach (string user in context.Users.Where(user => Directory.Exists(users.Home(user))))
        {
            log.Info($"user {user}");
            string home = users.Home(user);
            IReadOnlyList<string> owned = context.State.Lines($"extensions-owned-{user}");
            foreach (string id in installed.Reverse().Where(owned.Contains))
            {
                await users.RunAs(user, false, "code", ["--uninstall-extension", id], cancellation);
                string extensions = Path.Combine(home, ".vscode", "extensions");
                foreach (string folder in Directory.Exists(extensions) ? Directory.EnumerateDirectories(extensions, $"{id}-[0-9]*") : [])
                {
                    Directory.Delete(folder, recursive: true);
                }
            }

            await userFiles.UnsetJson(user, Path.Combine(home, ".config", "Code", "User", "settings.json"), (JsonObject)JsonNode.Parse(settings)!, cancellation);
            await userFiles.UnsetJson(user, Path.Combine(home, ".cline", "data", "globalState.json"), clineState, cancellation);
            await userFiles.UnsetJson(user, Path.Combine(home, ".cline", "data", "settings", "global-settings.json"), clineSettings, cancellation);
            File.Delete(Path.Combine(home, "Documents", "Cline", "Rules", "offline-dotnet.md"));
            RestoreNuGetConfig(Path.Combine(home, ".nuget", "NuGet", "NuGet.Config"));
        }

        await UnsetGitDefaults(cancellation);
        File.Delete("/usr/local/share/NuGet/Config/bundler.config");
        DeleteIfEmpty("/usr/local/share/NuGet/Config");
        DeleteIfEmpty("/usr/local/share/NuGet");
        if (Directory.Exists(feed.Directory))
        {
            Directory.Delete(feed.Directory, recursive: true);
        }

        DeleteIfEmpty(Path.GetDirectoryName(feed.Directory)!);
        await RemoveOllama(context, cancellation);
        RemoveDotNet(context);
        blocks.Remove("/etc/environment", "dotnet");
        await aptInstaller.RemoveAdded(context.State, cancellation);
    }

    private IEnumerable<(string Id, string Version, string File)> ExtensionRows(string stateDirectory)
    {
        string table = Path.Combine(stateDirectory, "extensions", "extensions.tsv");
        return File.Exists(table) ? File.ReadAllLines(table).Select(line => line.Split('\t')).Where(fields => fields.Length == 3).Select(fields => (fields[0], fields[1], fields[2])) : [];
    }

    private async Task<string> InstallDotNet(InstallContext context, CancellationToken cancellation)
    {
        log.Step(".NET SDK");
        string tarball = Directory.EnumerateFiles(Path.Combine(context.Payload, "dotnet"), "dotnet-sdk-*.tar.gz").First();
        string bundled = Path.GetFileName(tarball)["dotnet-sdk-".Length..^"-linux-x64.tar.gz".Length];
        if (decisions.Install(".NET SDK", await sdks.InstalledVersion(cancellation), bundled))
        {
            string staging = dotnetRoot + ".new";
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            Directory.CreateDirectory(staging);
            await using (FileStream file = File.OpenRead(tarball))
            await using (GZipStream gzip = new(file, CompressionMode.Decompress))
            {
                await TarFile.ExtractToDirectoryAsync(gzip, staging, overwriteFiles: true, cancellation);
            }

            if (Directory.Exists(dotnetRoot))
            {
                Directory.Delete(dotnetRoot, recursive: true);
            }

            Directory.Move(staging, dotnetRoot);
            File.Delete("/usr/local/bin/dotnet");
            File.CreateSymbolicLink("/usr/local/bin/dotnet", Path.Combine(dotnetRoot, "dotnet"));
            context.State.MarkOwned("dotnet");
        }

        blocks.Add("/etc/environment", "dotnet", ["DOTNET_CLI_TELEMETRY_OPTOUT=1", "DOTNET_NOLOGO=1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"]);
        string bin = machine.FindExecutable("dotnet") ?? Path.Combine(dotnetRoot, "dotnet");
        log.Info(await runner.Capture(bin, ["--version"]));
        return bin;
    }

    private async Task InstallOllama(InstallContext context, CancellationToken cancellation)
    {
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

        string unit = resources.Text("dev/ollama.service").Replace("ExecStart=/usr/bin/ollama", $"ExecStart={binary}", StringComparison.Ordinal);
        changed |= WriteIfChanged("/etc/systemd/system/ollama.service", unit);
        changed |= WriteIfChanged(Path.Combine(dropInFolder, "zz-bundler.conf"), resources.Text("dev/ollama-bundler.conf"));
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

    private async Task SetUpUser(string user, InstallContext context, CancellationToken cancellation)
    {
        log.Info($"user {user}");
        string home = users.Home(user);
        await userFiles.SetJson(user, Path.Combine(home, ".config", "Code", "User", "settings.json"), (JsonObject)JsonNode.Parse(context.State.Read("vscode-settings.json")!)!, cancellation: cancellation);

        string nuget = Path.Combine(home, ".nuget", "NuGet", "NuGet.Config");
        if (File.Exists(nuget) && !File.ReadAllText(nuget).Contains("bundler", StringComparison.Ordinal) && !File.Exists(nuget + ".bundler.bak"))
        {
            File.Copy(nuget, nuget + ".bundler.bak");
        }

        await users.MakeDirectory(user, Path.GetDirectoryName(nuget)!, cancellation);
        feed.WriteConfig(nuget);
        await users.Own(user, [nuget], cancellation);

        await userFiles.SetJson(user, Path.Combine(home, ".cline", "data", "globalState.json"), clineState, "actModeApiProvider", cancellation);
        await userFiles.SetJson(user, Path.Combine(home, ".cline", "data", "settings", "global-settings.json"), clineSettings, cancellation: cancellation);
        await userFiles.Write(user, Path.Combine(home, "Documents", "Cline", "Rules", "offline-dotnet.md"), resources.Text("dev/cline-rules/offline-dotnet.md"), cancellation);

        string listed = (await users.RunAs(user, false, "code", ["--list-extensions", "--show-versions"], cancellation)).Output;
        foreach ((string id, string version, string file) in ExtensionRows(context.State.Directory))
        {
            string? have = listed.Split('\n').Where(line => line.StartsWith(id + "@", StringComparison.OrdinalIgnoreCase)).Select(line => line[(id.Length + 1)..].Trim()).FirstOrDefault();
            if (decisions.Install($"VS Code extension {id} for {user}", have, version))
            {
                await users.RunAs(user, false, "code", ["--install-extension", Path.Combine(context.State.Directory, "extensions", file), "--force"], cancellation);
                context.State.Add($"extensions-owned-{user}", id);
            }
        }

        string present = (await users.RunAs(user, false, "code", ["--list-extensions"], cancellation)).Output;
        foreach ((string id, _, _) in ExtensionRows(context.State.Directory).Where(row => !present.Contains(row.Id, StringComparison.OrdinalIgnoreCase)))
        {
            log.Warn($"the VS Code extension {id} is not installed for {user}");
        }
    }

    private async Task RemoveOllama(UninstallContext context, CancellationToken cancellation)
    {
        await systemd.Control("disable --now", "ollama", cancellation);
        File.Delete("/etc/systemd/system/ollama.service");
        File.Delete(Path.Combine(dropInFolder, "zz-bundler.conf"));
        DeleteIfEmpty(dropInFolder);
        await systemd.Reload(cancellation);
        if (!context.State.IsOwned("ollama"))
        {
            log.Info("Ollama was already on this computer and was not installed by this bundle, so it is left in place");
            return;
        }

        string prefix = context.State.Read("ollama-prefix")?.Trim() ?? "/usr";
        File.Delete(Path.Combine(prefix, "bin", "ollama"));
        if (Directory.Exists(Path.Combine(prefix, "lib", "ollama")))
        {
            Directory.Delete(Path.Combine(prefix, "lib", "ollama"), recursive: true);
        }

        string models = "/usr/share/ollama/.ollama/models";
        bool hasModels = new[] { "manifests", "manifests-v2" }.Select(folder => Path.Combine(models, folder)).Where(Directory.Exists).Any(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any());
        if (hasModels)
        {
            log.Warn("Ollama models are still in /usr/share/ollama (the ollama user is kept). Delete that folder by hand if you do not need them.");
        }
        else
        {
            await runner.Try("userdel", ["ollama"]);
            if (Directory.Exists("/usr/share/ollama"))
            {
                Directory.Delete("/usr/share/ollama", recursive: true);
            }
        }
    }

    private void RemoveDotNet(UninstallContext context)
    {
        if (!context.State.IsOwned("dotnet"))
        {
            log.Info(".NET was already on this computer and was not installed by this bundle, so it is left in place");
            return;
        }

        if (new FileInfo("/usr/local/bin/dotnet").LinkTarget == Path.Combine(dotnetRoot, "dotnet"))
        {
            File.Delete("/usr/local/bin/dotnet");
        }

        if (Directory.Exists(dotnetRoot))
        {
            Directory.Delete(dotnetRoot, recursive: true);
        }
    }

    private async Task UnsetGitDefaults(CancellationToken cancellation)
    {
        if (machine.FindExecutable("git") is null)
        {
            return;
        }

        foreach ((string key, string value) in new[] { ("init.defaultBranch", "main"), ("core.editor", "code --wait") })
        {
            ProcessResult current = await runner.Try("git", ["config", "--system", "--get", key]);
            if (current.Text == value)
            {
                await runner.Try("git", ["config", "--system", "--unset", key]);
            }
        }
    }

    private void RestoreNuGetConfig(string path)
    {
        if (File.Exists(path) && File.ReadAllText(path).Contains("bundler", StringComparison.Ordinal))
        {
            File.Delete(path);
            if (File.Exists(path + ".bundler.bak"))
            {
                File.Move(path + ".bundler.bak", path);
            }
        }
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

    private void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
    }

    private void DeleteIfEmpty(string folder)
    {
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Directory.Delete(folder);
        }
    }
}
