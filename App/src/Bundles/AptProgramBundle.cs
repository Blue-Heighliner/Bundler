namespace BlueHeighliner.Bundler;

/// <summary>
/// A bundle of one program and its libraries from the Ubuntu 26.04 archive.
/// </summary>
internal abstract class AptProgramBundle(IAptBuilder apt, IAptInstaller installer, ILog log) : BundleBase
{
    /// <summary>Gets the name of the Ubuntu package of the program.</summary>
    protected abstract string Package { get; }

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        await apt.Prepare([], cancellation);
        IReadOnlyList<DebPackage> packages = await apt.Download(Path.Combine(directory, "debs"), new() { Services = [Package] }, cancellation);
        DebPackage found = packages.First(package => package.Package == Package);
        return new() { Components = [$"{found.Package} {found.Version}"] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        await installer.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);
        log.Info($"{Package} {await installer.InstalledVersion(Package, cancellation)}");
    }

    /// <inheritdoc />
    public override Task Uninstall(UninstallContext context, CancellationToken cancellation = default)
        => installer.RemoveAdded(context.State, cancellation);
}
