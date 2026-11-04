namespace BlueHeighliner.Bundler;

/// <summary>
/// SQLite NuGet packages in the offline feed and DB Browser for SQLite (the Ubuntu 26.04 package, which is the latest stable
/// upstream release).
/// </summary>
internal sealed class SqliteBundle(INuGetBundleBuilder builder, INuGetBundleInstaller installer, IAptBuilder apt, IAptInstaller aptInstaller, ILog log) : NuGetBundleBase(builder, installer)
{
    private readonly INuGetBundleInstaller installer = installer;

    /// <inheritdoc />
    public override string Name => "sqlite";

    /// <inheritdoc />
    public override string Description => "Use SQLite databases from C# and look inside them with a graphical tool.";

    /// <inheritdoc />
    public override int Order => 65;

    /// <inheritdoc />
    protected override NuGetBundleSpec Spec { get; } = new() { PackageList = "sqlite/packages.txt" };

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        BuildResult nuget = await base.Build(directory, cancellation);
        log.Step("DB Browser for SQLite");
        await apt.Prepare([], cancellation);
        IReadOnlyList<DebPackage> packages = await apt.Download(Path.Combine(directory, "debs"), new() { Services = ["sqlitebrowser"] }, cancellation);
        DebPackage browser = packages.First(package => package.Package == "sqlitebrowser");
        return new() { Components = [$"DB Browser for SQLite {browser.Version}", .. nuget.Components] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        await base.Install(context, cancellation);
        log.Step("DB Browser for SQLite");
        await aptInstaller.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);
    }

    /// <inheritdoc />
    public override async Task Uninstall(UninstallContext context, CancellationToken cancellation = default)
    {
        await installer.Uninstall(context, cancellation);
        await aptInstaller.RemoveAdded(context.State, cancellation);
    }
}
