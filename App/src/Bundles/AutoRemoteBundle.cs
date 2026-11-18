namespace BlueHeighliner.Bundler;

/// <summary>
/// The Auto Remote server and its Linux desktop client, latest stable release of Markwardt-Labs/Auto-Remote. The server runs as a
/// systemd user service for every normal account (enabled, with lingering so it runs without a login) and the client gets an app
/// finder entry with its icon, the way the author installs it on their own computer. Each account also gets an AI environment that
/// makes new workspaces run on the local Gemma model, and the client reaches the server through the server's local channel. It needs git (for its git features), the dotnet bundle (for its scripts), and the
/// ollama, gemma and open-code bundles, so that workspaces run on the local Gemma model through OpenCode and Ollama without any network.
/// </summary>
internal sealed class AutoRemoteBundle(
    IReleaseSources sources,
    IDownloads downloads,
    IHttpFetcher http,
    IResources resources,
    IAptBuilder apt,
    IAptInstaller aptInstaller,
    IDecisions decisions,
    IUserFiles userFiles,
    IUsers users,
    IAutoRemoteFiles files,
    IProcessRunner runner,
    ISystemd systemd,
    ILog log) : BundleBase
{
    private static readonly string repository = "Markwardt-Labs/Auto-Remote";

    private static readonly string[] libraries =
    [
        "libx11-6", "libxcursor1", "libxext6", "libxi6", "libxrandr2", "libxrender1", "libxkbcommon0", "libice6", "libsm6", "fontconfig", "libfontconfig1",
        "libgl1", "libegl1", "libvulkan1", "fonts-dejavu-core",
    ];

    /// <inheritdoc />
    public override string Name => "auto-remote";

    /// <inheritdoc />
    public override string Description => "Use AI coding assistants on this computer from your phone, browser or another computer, also without a network on the local Gemma model: the Auto Remote server as a background service and its desktop app.";

    /// <inheritdoc />
    public override int Order => 100;

    /// <inheritdoc />
    public override IReadOnlyList<string> Requires { get; } = ["git", "dotnet", "ollama", "gemma", "open-code"];

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        log.Step("Auto Remote");
        GitHubRelease release = await sources.LatestGitHub(repository, ["^AutoRemote-Server-linux$", "^AutoRemote-linux$", @"^THIRD-PARTY-NOTICES\.txt$"], cancellation);
        string[] targets = ["server/Markwardt.AutoRemote.Server", "desktop/Markwardt.AutoRemote.DesktopClient", "THIRD-PARTY-NOTICES.txt"];
        for (int index = 0; index < release.Assets.Count; index++)
        {
            DownloadAsset asset = release.Assets[index];
            await downloads.Place(await downloads.Fetch(asset.Url, asset.FileName, asset.Hash, cancellation), Path.Combine(directory, targets[index]), cancellation);
        }

        string version = release.Tag;
        await http.Download($"https://raw.githubusercontent.com/{repository}/{version}/LICENSE", Path.Combine(directory, "LICENSE"), null, cancellation);
        await File.WriteAllBytesAsync(Path.Combine(directory, "auto-remote.png"), resources.Bytes("auto-remote/icon.png").ToArray(), cancellation);
        await File.WriteAllTextAsync(Path.Combine(directory, "version.txt"), version, cancellation);

        log.Step("Ubuntu packages");
        await apt.Prepare([], cancellation);
        string icu = await apt.Find(@"libicu[0-9]+", cancellation) ?? throw new InvalidOperationException("could not find the ICU library in the Ubuntu 24.04 archive");
        string ssl = await apt.Find(@"libssl[0-9]+(t64)?", cancellation) ?? throw new InvalidOperationException("could not find the OpenSSL library in the Ubuntu 24.04 archive");
        await apt.Download(Path.Combine(directory, "debs"), new() { Support = [.. libraries, icu, ssl] }, cancellation);
        return new() { Components = [$"Auto Remote server {version} (linux x64)", $"Auto Remote desktop client {version} (linux x64)"] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        await aptInstaller.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);
        await runner.Try("fc-cache", ["-s"]);
        string bundled = (await File.ReadAllTextAsync(Path.Combine(context.Payload, "version.txt"), cancellation)).Trim();
        log.Step($"Auto Remote {bundled}");
        string? have = File.Exists(files.VersionFile) ? (await File.ReadAllTextAsync(files.VersionFile, cancellation)).Trim() : null;
        if (decisions.Install("Auto Remote", have, bundled))
        {
            await InstallFiles(context, bundled, cancellation);
        }

        log.Step("Server service for each user");
        if (context.Users.Count == 0)
        {
            log.Warn("no normal user accounts found, so no server service was set up. Run the install again after creating one.");
        }

        for (int index = 0; index < context.Users.Count; index++)
        {
            await SetUpUser(context.Users[index], index, context, cancellation);
        }
    }

    /// <inheritdoc />
    public override async Task Uninstall(UninstallContext context, CancellationToken cancellation = default)
    {
        foreach (string user in context.State.Lines("service-users"))
        {
            if (HomeOf(user) is string home && Directory.Exists(home))
            {
                await RemoveUser(user, home);
            }
        }

        foreach (string user in context.State.Lines("linger-users"))
        {
            await DisableLinger(user);
        }

        if (context.State.IsOwned("auto-remote"))
        {
            File.DeleteIfPresent(files.DesktopEntry);
            File.DeleteIfPresent(files.Icon);
            if (Directory.Exists(files.Root))
            {
                Directory.Delete(files.Root, recursive: true);
            }

            await runner.Try("update-desktop-database", ["/usr/share/applications"]);
        }
        else
        {
            log.Info("Auto Remote was already on this computer and was not installed by this bundle, so it is left in place");
        }

        await aptInstaller.RemoveAdded(context.State, cancellation);
    }

    private async Task InstallFiles(InstallContext context, string bundled, CancellationToken cancellation)
    {
        Install(Path.Combine(context.Payload, "server", "Markwardt.AutoRemote.Server"), files.Server, executable: true);
        Install(Path.Combine(context.Payload, "desktop", "Markwardt.AutoRemote.DesktopClient"), files.Client, executable: true);
        Install(Path.Combine(context.Payload, "THIRD-PARTY-NOTICES.txt"), Path.Combine(files.Root, "THIRD-PARTY-NOTICES.txt"), executable: false);
        Install(Path.Combine(context.Payload, "LICENSE"), Path.Combine(files.Root, "LICENSE"), executable: false);
        Install(Path.Combine(context.Payload, "auto-remote.png"), files.Icon, executable: false);
        Directory.CreateDirectory(Path.GetDirectoryName(files.DesktopEntry)!);
        await File.WriteAllTextAsync(files.DesktopEntry, files.DesktopEntryText, cancellation);
        File.SetUnixFileMode(files.DesktopEntry, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        await runner.Try("update-desktop-database", ["/usr/share/applications"]);
        await File.WriteAllTextAsync(files.VersionFile, bundled, cancellation);
        context.State.MarkOwned("auto-remote");
        log.Info($"server {files.Server}, client {files.Client}, app finder entry {files.DesktopEntry}");
    }

    private void Install(string source, string destination, bool executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".new";
        File.Copy(source, temporary, overwrite: true);
        UnixFileMode read = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        File.SetUnixFileMode(temporary, executable ? read | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute : read);
        File.Move(temporary, destination, overwrite: true);
    }

    private async Task SetUpUser(string user, int position, InstallContext context, CancellationToken cancellation)
    {
        log.Info($"user {user}");
        string home = users.Home(user);
        await users.MakeDirectory(user, Path.Combine(home, ".local", "share"), cancellation);
        string config = Path.Combine(home, ".config", "auto-remote", "config.json");
        if (File.Exists(config))
        {
            log.Info($"{config} exists, its key and settings are kept");
        }
        else
        {
            await userFiles.Write(user, config, files.ConfigText(files.NewKey(), position == 0 ? null : files.PortFor(position)), cancellation);
            File.SetUnixFileMode(config, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            log.Info($"created {config} with a new key (read it there to connect a client)");
        }

        string environment = Path.Combine(home, ".local", "share", "AutoRemote", "environment.json");
        if (File.Exists(environment))
        {
            log.Info($"{environment} exists, its AI settings are kept");
        }
        else
        {
            await userFiles.Write(user, environment, files.EnvironmentText, cancellation);
            File.SetUnixFileMode(environment, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            log.Info($"created {environment}: new workspaces run on the local {files.LocalModel} model through OpenCode and Ollama");
        }

        string unit = Path.Combine(home, ".config", "systemd", "user", "auto-remote.service");
        if (File.Exists(unit) && await File.ReadAllTextAsync(unit, cancellation) != files.UnitText && !context.State.Lines("service-users").Contains(user))
        {
            log.Warn($"{unit} is already there and is not the one of this bundle, so it is left alone and no service was set up for {user}");
            return;
        }

        await userFiles.Write(user, unit, files.UnitText, cancellation);
        string wants = Path.Combine(Path.GetDirectoryName(unit)!, "default.target.wants");
        await users.MakeDirectory(user, wants, cancellation);
        string link = Path.Combine(wants, "auto-remote.service");
        if (new FileInfo(link).LinkTarget is null)
        {
            File.CreateSymbolicLink(link, unit);
            await users.Own(user, [link], cancellation);
        }

        context.State.Add("service-users", user);
        await EnableLinger(user, context, cancellation);
        await Start(user);
    }

    private async Task EnableLinger(string user, InstallContext context, CancellationToken cancellation)
    {
        string marker = Path.Combine("/var/lib/systemd/linger", user);
        if (File.Exists(marker))
        {
            return;
        }

        if (!(await runner.Try("loginctl", ["enable-linger", user])).Success)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            await File.WriteAllTextAsync(marker, string.Empty, cancellation);
        }

        context.State.Add("linger-users", user);
        log.Info($"{user}'s services keep running without a login (linger)");
    }

    private async Task Start(string user)
    {
        if (!systemd.IsRunning)
        {
            log.Info("systemd is not running here, the server service starts at the next boot");
            return;
        }

        string machine = $"{user}@.host";
        await runner.Try("systemctl", ["--user", "-M", machine, "daemon-reload"]);
        if (!(await runner.Try("systemctl", ["--user", "-M", machine, "restart", "auto-remote.service"])).Success)
        {
            log.Warn($"the server service of {user} could not be started now, it starts at the next login or boot");
        }
    }

    private async Task RemoveUser(string user, string home)
    {
        if (systemd.IsRunning)
        {
            await runner.Try("systemctl", ["--user", "-M", $"{user}@.host", "disable", "--now", "auto-remote.service"]);
        }

        string unitFolder = Path.Combine(home, ".config", "systemd", "user");
        File.DeleteIfPresent(Path.Combine(unitFolder, "default.target.wants", "auto-remote.service"));
        File.DeleteIfPresent(Path.Combine(unitFolder, "auto-remote.service"));
        foreach (string folder in new[] { Path.Combine(unitFolder, "default.target.wants"), unitFolder, Path.GetDirectoryName(unitFolder)! })
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }

        if (systemd.IsRunning)
        {
            await runner.Try("systemctl", ["--user", "-M", $"{user}@.host", "daemon-reload"]);
        }

        log.Info($"user {user}: the service is removed; {Path.Combine(home, ".config", "auto-remote", "config.json")} and the data in {Path.Combine(home, ".local", "share", "AutoRemote")} are kept");
    }

    private string? HomeOf(string user)
    {
        try
        {
            return users.Home(user);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task DisableLinger(string user)
    {
        if (!(await runner.Try("loginctl", ["disable-linger", user])).Success)
        {
            File.DeleteIfPresent(Path.Combine("/var/lib/systemd/linger", user));
        }
    }
}
