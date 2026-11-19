namespace BlueHeighliner.Bundler;

/// <summary>
/// A bundle of one program, or a few that belong together, and their libraries from the Ubuntu 24.04 archive.
/// </summary>
internal abstract class AptProgramBundle(IAptBuilder apt, IAptInstaller installer, ILog log) : BundleBase
{
    /// <summary>Gets the name of the Ubuntu package of the program.</summary>
    protected abstract string Package { get; }

    /// <summary>Gets every package that is a service of the bundle: installed or upgraded, and reported with its version. Just the program unless a bundle says otherwise.</summary>
    protected virtual IReadOnlyList<string> Services => [Package];

    /// <summary>Gets the packages that only come along when the computer does not have them, such as optional parts and plug-ins.</summary>
    protected virtual IReadOnlyList<string> Support => [];

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        await apt.Prepare([], cancellation);
        IReadOnlyList<DebPackage> packages = await apt.Download(Path.Combine(directory, "debs"), new() { Services = Services, Support = Support }, cancellation);
        return new() { Components = [.. Services.Select(service => packages.First(package => package.Package == service)).Select(found => $"{found.Package} {found.Version}")] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        await installer.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);
        foreach (string service in Services)
        {
            log.Info($"{service} {await installer.InstalledVersion(service, cancellation)}");
        }
    }

    /// <inheritdoc />
    public override UninstallScript Script(UninstallContext context)
    {
        UninstallScript script = new();
        script.Add("apt-remove-added");
        return script;
    }
}
