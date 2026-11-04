namespace BlueHeighliner.Bundler;

/// <summary>
/// The latest stable Blender from Blender's own Linux release archive (the Ubuntu archive is a release or two behind), installed
/// in <c>/opt/blender</c> with a <c>blender</c> command and a menu entry.
/// </summary>
internal sealed class ThreeDBundle(IReleaseSources sources, IDownloads downloads, IAptBuilder apt, IAptInstaller aptInstaller, IDecisions decisions, IVersions versions, IProcessRunner runner, ILog log) : BundleBase
{
    private static readonly string root = "/opt/blender";

    private static readonly string[] libraries =
    [
        "xz-utils", "libx11-6", "libxi6", "libxxf86vm1", "libxfixes3", "libxrender1", "libxkbcommon0", "libsm6", "libice6", "libgl1", "libegl1",
        "libwayland-client0", "libwayland-cursor0", "libwayland-egl1", "libdecor-0-0", "libdbus-1-3", "libfontconfig1",
    ];

    /// <inheritdoc />
    public override string Name => "3d";

    /// <inheritdoc />
    public override string Description => "Model, sculpt, texture and animate 3D objects with Blender, and export them for games and apps.";

    /// <inheritdoc />
    public override int Order => 90;

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        log.Step("Blender");
        BlenderRelease release = await sources.LatestBlender(cancellation);
        await downloads.Place(await downloads.Fetch(release.Url, release.FileName, release.Hash, cancellation), Path.Combine(directory, "blender", release.FileName), cancellation);
        log.Step("Ubuntu packages");
        await apt.Prepare([], cancellation);
        await apt.Download(Path.Combine(directory, "debs"), new() { Support = libraries }, cancellation);
        await File.WriteAllTextAsync(Path.Combine(directory, "version.txt"), release.Version, cancellation);
        return new() { Components = [$"Blender {release.Version} (linux x64)"] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        await aptInstaller.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);
        string version = (await File.ReadAllTextAsync(Path.Combine(context.Payload, "version.txt"), cancellation)).Trim();
        log.Step($"Blender {version}");
        ProcessResult existing = await runner.Try("blender", ["--version"]);
        string have = existing.Success ? versions.Find(existing.Output.Split('\n')[0]) : string.Empty;
        if (decisions.Install("Blender", have.Length == 0 ? null : have, version))
        {
            await InstallBlender(context, version, cancellation);
        }

        log.Info((await runner.Try("blender", ["--version"])).Text.Split('\n')[0]);
    }

    /// <inheritdoc />
    public override Task Uninstall(UninstallContext context, CancellationToken cancellation = default)
    {
        if (context.State.IsOwned("blender"))
        {
            File.Delete("/usr/local/bin/blender");
            File.Delete("/usr/share/applications/blender.desktop");
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        else
        {
            log.Info("Blender was already on this computer and was not installed by this bundle, so it is left in place");
        }

        return aptInstaller.RemoveAdded(context.State, cancellation);
    }

    private async Task InstallBlender(InstallContext context, string version, CancellationToken cancellation)
    {
        string archive = Directory.EnumerateFiles(Path.Combine(context.Payload, "blender"), "blender-*-linux-x64.tar.xz").First();
        string destination = Path.Combine(root, version);
        string temporary = Path.Combine(root, ".unpack");
        Directory.CreateDirectory(root);
        if (Directory.Exists(temporary))
        {
            Directory.Delete(temporary, recursive: true);
        }

        Directory.CreateDirectory(temporary);
        await runner.Execute("tar", ["-xJf", archive, "-C", temporary]);
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        Directory.Move(Directory.EnumerateDirectories(temporary).First(), destination);
        Directory.Delete(temporary, recursive: true);
        File.Delete("/usr/local/bin/blender");
        File.CreateSymbolicLink("/usr/local/bin/blender", Path.Combine(destination, "blender"));
        await File.WriteAllTextAsync(
            "/usr/share/applications/blender.desktop",
            $"[Desktop Entry]\nName=Blender\nGenericName=3D modeler\nComment=3D modeling, animation, rendering and more\nExec=/usr/local/bin/blender %f\nIcon={destination}/blender.svg\nTerminal=false\nType=Application\nMimeType=application/x-blender;\nCategories=Graphics;3DGraphics;\nStartupWMClass=Blender\n",
            cancellation);
        foreach (string other in Directory.EnumerateDirectories(root).Where(other => other != destination))
        {
            Directory.Delete(other, recursive: true);
        }

        context.State.MarkOwned("blender");
    }
}
