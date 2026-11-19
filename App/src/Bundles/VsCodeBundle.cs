namespace BlueHeighliner.Bundler;

/// <summary>
/// VS Code with the C#, .NET Install Tool, Git Graph and Cline extensions (not the C# Dev Kit, whose license does not allow general
/// commercial use), set up for every user to work offline: it finds the .NET SDK and git, telemetry and update checks are off, Cline
/// talks to the local Ollama and has rules for working offline, and git uses branch <c>main</c> with VS Code as its editor.
/// </summary>
internal sealed class VsCodeBundle(
    IResources resources,
    IReleaseSources sources,
    IDownloads downloads,
    IMarketplace marketplace,
    IAptBuilder apt,
    IAptInstaller aptInstaller,
    IDecisions decisions,
    IMachine machine,
    IUsers users,
    IUserFiles userFiles,
    IProcessRunner runner,
    ILog log) : BundleBase
{
    private static readonly string[] extensions = ["ms-dotnettools.csharp", "ms-dotnettools.vscode-dotnet-runtime", "mhutchie.git-graph", "saoudrizwan.claude-dev"];

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
    public override string Name => "vs-code";

    /// <inheritdoc />
    public override string Description => "Edit code in VS Code with C# support, git history and a local AI coding assistant, all working without internet.";

    /// <inheritdoc />
    public override int Order => 24;

    /// <inheritdoc />
    public override IReadOnlyList<string> Requires { get; } = ["git", "dotnet", "ollama"];

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        log.Step("Visual Studio Code");
        VsCodeRelease code = await sources.LatestVsCode(cancellation);
        string codeDeb = await downloads.Fetch(code.Url, code.FileName, code.Hash, cancellation);

        log.Step("VS Code extensions");
        IReadOnlyList<ExtensionPackage> packages = await marketplace.Fetch(Path.Combine(directory, "extensions"), extensions, cancellation);

        log.Step("Ubuntu packages (VS Code)");
        await apt.Prepare([], cancellation);
        await apt.Download(Path.Combine(directory, "debs"), new() { Services = [codeDeb], Support = ["ca-certificates", "xdg-utils"] }, cancellation);

        List<string> components = [$"Visual Studio Code {code.Version}"];
        components.AddRange(packages.Select(package => $"VS Code extension {package.Id} {package.Version}"));
        return new() { Components = components };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        log.Step("Ubuntu packages");
        await runner.Run(new() { FileName = "debconf-set-selections", Input = "code code/add-microsoft-repo boolean false\n" }, cancellation);
        await aptInstaller.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);

        log.Step("git defaults");
        await runner.Run(new() { FileName = "git", Arguments = ["config", "--system", "init.defaultBranch", "main"] }, cancellation);
        await runner.Run(new() { FileName = "git", Arguments = ["config", "--system", "core.editor", "code --wait"] }, cancellation);

        log.Step("VS Code and Cline settings for each user");
        string extensionFolder = Path.Combine(context.State.Directory, "extensions");
        if (Directory.Exists(extensionFolder))
        {
            Directory.Delete(extensionFolder, recursive: true);
        }

        Directory.CreateDirectory(context.State.Directory);
        CopyFolder(Path.Combine(context.Payload, "extensions"), extensionFolder);
        string dotnetBin = machine.FindExecutable("dotnet") ?? "/usr/share/dotnet/dotnet";
        context.State.Write("vscode-settings.json", resources.Text("vs-code/vscode-settings.json").Replace("/usr/share/dotnet/dotnet", dotnetBin, StringComparison.Ordinal));
        if (context.Users.Count == 0)
        {
            log.Warn("no normal user accounts found, so no VS Code settings were written. Run the install again after creating one.");
        }

        // The state folder is under root's home, which a normal user cannot read, so each user installs the extensions from a copy outside it.
        DirectoryInfo staged = Directory.CreateTempSubdirectory("bundler-vsix-");
        try
        {
            File.SetUnixFileMode(staged.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            foreach (string file in Directory.EnumerateFiles(extensionFolder, "*.vsix"))
            {
                string copy = Path.Combine(staged.FullName, Path.GetFileName(file));
                File.Copy(file, copy);
                File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }

            foreach (string user in context.Users)
            {
                await SetUpUser(user, context, staged.FullName, cancellation);
            }
        }
        finally
        {
            staged.Delete(recursive: true);
        }
    }

    /// <inheritdoc />
    public override UninstallScript Script(UninstallContext context)
    {
        UninstallScript script = new();
        string settings = context.State.Read("vscode-settings.json") ?? resources.Text("vs-code/vscode-settings.json");
        IReadOnlyList<string> installed = ExtensionRows(context.State.Directory).Select(row => row.Id).ToList();
        foreach (string user in context.Users.Where(user => Directory.Exists(users.Home(user))))
        {
            string home = users.Home(user);
            IReadOnlyList<string> owned = context.State.Lines($"extensions-owned-{user}");
            script.Info($"user {user}");
            foreach (string id in installed.Reverse().Where(owned.Contains))
            {
                script.Add("run-as", user, "code", "--uninstall-extension", id);
                script.Add("rm-glob", Path.Combine(home, ".vscode", "extensions"), $"{id}-[0-9]*");
            }

            script.Add("json-unset", user, Path.Combine(home, ".config", "Code", "User", "settings.json"), ((JsonObject)JsonNode.Parse(settings)!).ToJsonString());
            script.Add("json-unset", user, Path.Combine(home, ".cline", "data", "globalState.json"), clineState.ToJsonString());
            script.Add("json-unset", user, Path.Combine(home, ".cline", "data", "settings", "global-settings.json"), clineSettings.ToJsonString());
            script.Remove(Path.Combine(home, "Documents", "Cline", "Rules", "offline-dotnet.md"));
        }

        script.Add("git-unset", "init.defaultBranch", "main");
        script.Add("git-unset", "core.editor", "code --wait");
        script.Add("apt-remove-added");
        return script;
    }

    private IEnumerable<(string Id, string Version, string File)> ExtensionRows(string stateDirectory)
    {
        string table = Path.Combine(stateDirectory, "extensions", "extensions.tsv");
        return File.Exists(table) ? File.ReadAllLines(table).Select(line => line.Split('\t')).Where(fields => fields.Length == 3).Select(fields => (fields[0], fields[1], fields[2])) : [];
    }

    private async Task SetUpUser(string user, InstallContext context, string stagedExtensions, CancellationToken cancellation)
    {
        log.Info($"user {user}");
        string home = users.Home(user);
        await userFiles.SetJson(user, Path.Combine(home, ".config", "Code", "User", "settings.json"), (JsonObject)JsonNode.Parse(context.State.Read("vscode-settings.json")!)!, cancellation: cancellation);
        await userFiles.SetJson(user, Path.Combine(home, ".cline", "data", "globalState.json"), clineState, "actModeApiProvider", cancellation);
        await userFiles.SetJson(user, Path.Combine(home, ".cline", "data", "settings", "global-settings.json"), clineSettings, cancellation: cancellation);
        await userFiles.Write(user, Path.Combine(home, "Documents", "Cline", "Rules", "offline-dotnet.md"), resources.Text("vs-code/cline-rules/offline-dotnet.md"), cancellation);

        string listed = (await users.RunAs(user, false, "code", ["--list-extensions", "--show-versions"], cancellation)).Output;
        foreach ((string id, string version, string file) in ExtensionRows(context.State.Directory))
        {
            string? have = listed.Split('\n').Where(line => line.StartsWith(id + "@", StringComparison.OrdinalIgnoreCase)).Select(line => line[(id.Length + 1)..].Trim()).FirstOrDefault();
            if (decisions.Install($"VS Code extension {id} for {user}", have, version))
            {
                await users.RunAs(user, false, "code", ["--install-extension", Path.Combine(stagedExtensions, file), "--force"], cancellation);
                context.State.Add($"extensions-owned-{user}", id);
            }
        }

        string present = (await users.RunAs(user, false, "code", ["--list-extensions"], cancellation)).Output;
        foreach ((string id, _, _) in ExtensionRows(context.State.Directory).Where(row => !present.Contains(row.Id, StringComparison.OrdinalIgnoreCase)))
        {
            log.Warn($"the VS Code extension {id} is not installed for {user}");
        }
    }

    private void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
    }
}
