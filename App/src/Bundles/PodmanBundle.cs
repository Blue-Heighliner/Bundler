namespace BlueHeighliner.Bundler;

/// <summary>
/// The Podman container engine with what rootless containers need. The version is the one in the Ubuntu 24.04 archive:
/// upstream is at 6.x, but for Linux it only publishes the remote client, which needs a Podman service somewhere else, so
/// the newest complete engine packaged for 24.04 is used.
/// </summary>
internal sealed class PodmanBundle(IAptBuilder apt, IAptInstaller installer, IProcessRunner runner, ILog log) : BundleBase
{
    private static readonly string[] support = ["uidmap", "passt", "slirp4netns", "aardvark-dns", "catatonit", "dbus-user-session"];

    private static readonly string[] shown = ["conmon", "crun", "netavark", "aardvark-dns", "passt", "golang-github-containers-common"];

    /// <inheritdoc />
    public override string Name => "podman";

    /// <inheritdoc />
    public override string Description => "Run containers without a background service or root access.";

    /// <inheritdoc />
    public override int Order => 80;

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        await apt.Prepare([], cancellation);
        IReadOnlyList<DebPackage> packages = await apt.Download(Path.Combine(directory, "debs"), new() { Services = ["podman"], Support = support }, cancellation);
        DebPackage podman = packages.First(package => package.Package == "podman");
        List<string> components = [$"podman {podman.Version}"];
        components.AddRange(shown.Select(name => packages.FirstOrDefault(package => package.Package == name)).Where(package => package is not null).Select(package => $"{package!.Package} {package.Version}"));
        return new() { Components = components };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        await installer.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);
        foreach (string user in context.Users)
        {
            await GiveRange(user, context.State, cancellation);
        }

        log.Info(await runner.Capture("podman", ["--version"]));
    }

    /// <inheritdoc />
    public override async Task Uninstall(UninstallContext context, CancellationToken cancellation = default)
    {
        foreach (string line in context.State.Lines("subids"))
        {
            string[] parts = line.Split(' ');
            if (parts.Length == 2 && int.TryParse(parts[1], out int start))
            {
                await runner.Try("usermod", ["--del-subuids", $"{start}-{start + 65535}", "--del-subgids", $"{start}-{start + 65535}", parts[0]]);
            }
        }

        await installer.RemoveAdded(context.State, cancellation);
        log.Warn("container images and volumes of each user stay in ~/.local/share/containers; delete that folder if you do not need them.");
    }

    private async Task GiveRange(string user, IBundleState state, CancellationToken cancellation)
    {
        if (HasRange("/etc/subuid", user) && HasRange("/etc/subgid", user))
        {
            return;
        }

        long start = new[] { "/etc/subuid", "/etc/subgid" }.Where(File.Exists).SelectMany(File.ReadAllLines)
            .Select(line => line.Split(':')).Where(fields => fields.Length == 3 && long.TryParse(fields[1], out _) && long.TryParse(fields[2], out _))
            .Select(fields => long.Parse(fields[1]) + long.Parse(fields[2])).Append(100000).Max();
        await runner.Run(new() { FileName = "usermod", Arguments = ["--add-subuids", $"{start}-{start + 65535}", "--add-subgids", $"{start}-{start + 65535}", user] }, cancellation);
        state.Add("subids", $"{user} {start}");
        log.Info($"{user} got the subordinate id range {start}-{start + 65535}");
    }

    private bool HasRange(string file, string user)
        => File.Exists(file) && File.ReadLines(file).Any(line => line.StartsWith(user + ":", StringComparison.Ordinal));
}
