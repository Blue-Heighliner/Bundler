namespace BlueHeighliner.Bundler;

/// <summary>
/// The latest stable git, from the official git-core PPA built for Ubuntu 24.04.
/// </summary>
internal sealed class GitBundle(IAptBuilder apt, IAptInstaller installer, IProcessRunner runner, ILog log) : BundleBase
{
    /// <inheritdoc />
    public override string Name => "git";

    /// <inheritdoc />
    public override string Description => "Version control for your projects with the current git release.";

    /// <inheritdoc />
    public override int Order => 10;

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        await apt.Prepare([await apt.PpaSource("git-core/ppa", cancellation)], cancellation);
        IReadOnlyList<DebPackage> packages = await apt.Download(Path.Combine(directory, "debs"), new() { Services = ["git"] }, cancellation);
        DebPackage git = packages.First(package => package.Package == "git");
        return new() { Components = [$"git {git.Version}"] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        await installer.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);
        log.Info(await runner.Capture("git", ["--version"]));
    }

    /// <inheritdoc />
    public override Task Uninstall(UninstallContext context, CancellationToken cancellation = default)
        => installer.RemoveAdded(context.State, cancellation);
}
