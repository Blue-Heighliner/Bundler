namespace BlueHeighliner.Bundler;

/// <summary>
/// The GIMP photo editor and the Krita painting program, with their libraries, from the Ubuntu 26.04 archive.
/// </summary>
internal sealed class TwoDBundle(IAptBuilder apt, IAptInstaller installer, ILog log) : BundleBase
{
    private static readonly string[] programs = ["gimp", "krita"];

    /// <inheritdoc />
    public override string Name => "2d";

    /// <inheritdoc />
    public override string Description => "Edit photos and paint digital art: GIMP for retouching and graphics, Krita for drawing and textures.";

    /// <inheritdoc />
    public override int Order => 85;

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        await apt.Prepare([], cancellation);
        IReadOnlyList<DebPackage> packages = await apt.Download(Path.Combine(directory, "debs"), new() { Services = programs }, cancellation);
        DebPackage[] found = [.. programs.Select(name => packages.First(package => package.Package == name))];
        return new() { Components = [.. found.Select(package => $"{package.Package} {package.Version}")] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        await installer.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);
        foreach (string program in programs)
        {
            log.Info($"{program} {await installer.InstalledVersion(program, cancellation)}");
        }
    }

    /// <inheritdoc />
    public override Task Uninstall(UninstallContext context, CancellationToken cancellation = default)
        => installer.RemoveAdded(context.State, cancellation);
}
