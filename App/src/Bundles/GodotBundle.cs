namespace BlueHeighliner.Bundler;

/// <summary>
/// The Godot game engine, .NET edition (C# support), with the matching export templates, linked into each user's Godot
/// folder. Creates the <c>godot</c> command and a menu entry.
/// </summary>
internal sealed class GodotBundle(
    IReleaseSources sources,
    IDownloads downloads,
    INuGetLayout layout,
    INuGetLicenses licenses,
    INuGetFeed feed,
    IAptBuilder apt,
    IAptInstaller aptInstaller,
    IDecisions decisions,
    IVersions versions,
    IUsers users,
    IProcessRunner runner,
    ILog log) : BundleBase
{
    private static readonly string root = "/opt/godot";

    private static readonly string[] libraries =
    [
        "unzip", "libx11-6", "libxcursor1", "libxinerama1", "libxrandr2", "libxi6", "libxext6", "libxrender1", "libxkbcommon0", "libgl1", "libegl1",
        "libasound2t64", "libpulse0", "libudev1", "libfontconfig1", "libdbus-1-3", "libwayland-client0", "libwayland-cursor0", "libwayland-egl1", "libdecor-0-0", "libvulkan1",
    ];

    /// <inheritdoc />
    public override string Name => "godot";

    /// <inheritdoc />
    public override string Description => "Make games with the Godot engine and C#.";

    /// <inheritdoc />
    public override int Order => 70;

    /// <inheritdoc />
    public override IReadOnlyList<string> Requires { get; } = ["dotnet"];

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        log.Step("Godot (.NET edition)");
        GitHubRelease release = await sources.LatestGitHub("godotengine/godot", [@"^Godot_v.*_mono_linux_x86_64\.zip$", @"_mono_export_templates\.tpz$"], cancellation);
        foreach (DownloadAsset asset in release.Assets)
        {
            await downloads.Place(await downloads.Fetch(asset.Url, asset.FileName, asset.Hash, cancellation), Path.Combine(directory, "godot", asset.FileName), cancellation);
        }

        log.Step("Godot's NuGet packages (Godot.NET.Sdk, GodotSharp) for the offline feed");
        string temporary = Path.Combine(directory, "nupkgs.tmp");
        using (ZipArchive editor = ZipFile.OpenRead(Path.Combine(directory, "godot", release.Assets[0].FileName)))
        {
            foreach (ZipArchiveEntry entry in editor.Entries.Where(entry => entry.FullName.Contains("/GodotSharp/Tools/nupkgs/", StringComparison.Ordinal) && entry.FullName.EndsWith(".nupkg", StringComparison.Ordinal) && !entry.FullName.EndsWith(".snupkg", StringComparison.Ordinal)))
            {
                Directory.CreateDirectory(temporary);
                entry.ExtractToFile(Path.Combine(temporary, entry.Name), overwrite: true);
            }
        }

        foreach (string file in Directory.EnumerateFiles(temporary))
        {
            layout.Place(Path.Combine(directory, "nuget"), file);
        }

        Directory.Delete(temporary, recursive: true);
        IReadOnlyList<LicenseRow> rows = licenses.Report(Path.Combine(directory, "nuget"));
        await File.WriteAllTextAsync(Path.Combine(directory, "nuget-licenses.tsv"), licenses.Format(rows), cancellation);
        foreach (LicenseRow row in rows.Where(row => row.NeedsReview))
        {
            log.Warn($"{row.Id} {row.Version} has a license that is not on the permissive list: {row.License}");
        }

        log.Step("Ubuntu packages");
        await apt.Prepare([], cancellation);
        await apt.Download(Path.Combine(directory, "debs"), new() { Support = libraries }, cancellation);

        string version = release.Tag.Replace("-stable", string.Empty, StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(directory, "version.txt"), version, cancellation);
        return new() { Components = [$"Godot {release.Tag} (.NET, linux x86_64)", $"Godot export templates {release.Tag} (.NET)"] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        await aptInstaller.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);
        string version = (await File.ReadAllTextAsync(Path.Combine(context.Payload, "version.txt"), cancellation)).Trim();
        log.Step($"Godot {version}");
        ProcessResult existing = await runner.Try("godot", ["--headless", "--version"]);
        string have = existing.Success ? versions.Find(existing.Output.Split('\n')[0]) : string.Empty;
        if (decisions.Install("Godot", have.Length == 0 ? null : have, version))
        {
            await InstallEditor(context, version, cancellation);
        }

        log.Step("Godot NuGet packages");
        feed.Add(Path.Combine(context.Payload, "nuget"), context.State);
        log.Info((await runner.Try("godot", ["--headless", "--version"])).Text.Split('\n')[0]);
    }

    /// <inheritdoc />
    public override Task Uninstall(UninstallContext context, CancellationToken cancellation = default)
    {
        if (context.State.IsOwned("godot"))
        {
            string templates = context.State.Read("templates-version")?.Trim() ?? string.Empty;
            foreach (string user in context.Users.Where(user => Directory.Exists(users.Home(user))))
            {
                string link = Path.Combine(users.Home(user), ".local", "share", "godot", "export_templates", templates);
                if (templates.Length > 0 && new FileInfo(link).LinkTarget is not null)
                {
                    File.DeleteIfPresent(link);
                }
            }

            File.DeleteIfPresent("/usr/local/bin/godot");
            File.DeleteIfPresent("/usr/share/applications/godot.desktop");
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        else
        {
            log.Info("Godot was already on this computer and was not installed by this bundle, so it is left in place");
        }

        feed.Remove(context.State);
        return aptInstaller.RemoveAdded(context.State, cancellation);
    }

    private async Task InstallEditor(InstallContext context, string version, CancellationToken cancellation)
    {
        string editor = Directory.EnumerateFiles(Path.Combine(context.Payload, "godot"), "Godot_v*_mono_linux_x86_64.zip").First();
        string templates = Directory.EnumerateFiles(Path.Combine(context.Payload, "godot"), "Godot_v*_export_templates.tpz").First();
        string destination = Path.Combine(root, version);
        Directory.CreateDirectory(root);
        string temporary = Path.Combine(root, ".unpack");
        if (Directory.Exists(temporary))
        {
            Directory.Delete(temporary, recursive: true);
        }

        ZipFile.ExtractToDirectory(editor, temporary);
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        Directory.Move(Directory.EnumerateDirectories(temporary).First(), destination);
        Directory.Delete(temporary, recursive: true);
        Readable(destination);
        string binary = Directory.EnumerateFiles(destination, "Godot_v*_mono_linux.x86_64").First();
        File.SetUnixFileMode(binary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.DeleteIfPresent("/usr/local/bin/godot");
        File.CreateSymbolicLink("/usr/local/bin/godot", binary);
        await File.WriteAllTextAsync(
            "/usr/share/applications/godot.desktop",
            "[Desktop Entry]\nName=Godot Engine\nGenericName=Game engine\nComment=2D and 3D game engine with C# support\nExec=/usr/local/bin/godot %f\nIcon=applications-games\nTerminal=false\nType=Application\nMimeType=application/x-godot-project;\nCategories=Development;IDE;\nStartupWMClass=Godot\n",
            cancellation);
        foreach (string other in Directory.EnumerateDirectories(root).Where(other => other != destination && other != Path.Combine(root, "export_templates")))
        {
            Directory.Delete(other, recursive: true);
        }

        log.Step("Export templates");
        string templateVersion = ReadTemplateVersion(templates);
        string templateRoot = Path.Combine(root, "export_templates");
        Directory.CreateDirectory(templateRoot);
        foreach (string other in Directory.EnumerateDirectories(templateRoot).Where(other => Path.GetFileName(other) != templateVersion))
        {
            Directory.Delete(other, recursive: true);
        }

        string templateFolder = Path.Combine(templateRoot, templateVersion);
        if (Directory.Exists(templateFolder))
        {
            Directory.Delete(templateFolder, recursive: true);
        }

        ZipFile.ExtractToDirectory(templates, temporary);
        Directory.Move(Path.Combine(temporary, "templates"), templateFolder);
        Directory.Delete(temporary, recursive: true);
        Readable(templateRoot);
        context.State.Write("templates-version", templateVersion);
        foreach (string user in context.Users)
        {
            await LinkTemplates(user, templateRoot, templateVersion, cancellation);
        }

        context.State.MarkOwned("godot");
    }

    private async Task LinkTemplates(string user, string templateRoot, string templateVersion, CancellationToken cancellation)
    {
        string folder = Path.Combine(users.Home(user), ".local", "share", "godot", "export_templates");
        await users.MakeDirectory(user, folder, cancellation);
        foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
        {
            if (new FileInfo(entry).LinkTarget is string target && target.StartsWith(templateRoot + "/", StringComparison.Ordinal))
            {
                File.DeleteIfPresent(entry);
            }
        }

        string link = Path.Combine(folder, templateVersion);
        File.CreateSymbolicLink(link, Path.Combine(templateRoot, templateVersion));
        await users.Own(user, [link], cancellation);
    }

    private string ReadTemplateVersion(string templates)
    {
        using ZipArchive zip = ZipFile.OpenRead(templates);
        using StreamReader reader = new((zip.GetEntry("templates/version.txt") ?? throw new InvalidOperationException("the export templates file has no version.txt")).Open());
        return reader.ReadToEnd().Trim();
    }

    private void Readable(string folder)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
        {
            UnixFileMode current = File.GetUnixFileMode(entry);
            File.SetUnixFileMode(entry, current | UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead | (Directory.Exists(entry) ? UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute : UnixFileMode.None));
        }
    }
}
