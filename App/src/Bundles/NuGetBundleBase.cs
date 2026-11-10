namespace BlueHeighliner.Bundler;

/// <summary>
/// A bundle that only adds NuGet packages (and maybe template packs) to the offline feed.
/// </summary>
internal abstract class NuGetBundleBase(INuGetBundleBuilder builder, INuGetBundleInstaller installer) : BundleBase
{
    /// <summary>Gets what the bundle mirrors.</summary>
    protected abstract NuGetBundleSpec Spec { get; }

    /// <inheritdoc />
    public override IReadOnlyList<string> Requires { get; } = ["dotnet"];

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        NuGetBuildResult result = await builder.Build(Spec, directory, cancellation);
        return new() { Components = result.Components };
    }

    /// <inheritdoc />
    public override Task Install(InstallContext context, CancellationToken cancellation = default)
        => installer.Install(Spec, context, cancellation);

    /// <inheritdoc />
    public override Task Uninstall(UninstallContext context, CancellationToken cancellation = default)
        => installer.Uninstall(context, cancellation);
}
