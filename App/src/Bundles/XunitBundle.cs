namespace BlueHeighliner.Bundler;

/// <summary>
/// The xunit v3 packages (and no xunit 2.x), Moq and coverlet.collector, with the <c>dotnet new xunit3</c> template.
/// </summary>
internal sealed class XunitBundle(INuGetBundleBuilder builder, INuGetBundleInstaller installer) : NuGetBundleBase(builder, installer)
{
    private static readonly string[] version2Packages = ["xunit", "xunit.core", "xunit.assert", "xunit.extensibility.core", "xunit.extensibility.execution", "xunit.runner.console", "xunit.runner.utility"];

    private readonly INuGetBundleBuilder builder = builder;

    /// <inheritdoc />
    public override string Name => "xunit";

    /// <inheritdoc />
    public override string Description => "Unit testing for C# projects: xunit v3 with mocking and code coverage.";

    /// <inheritdoc />
    public override int Order => 40;

    /// <inheritdoc />
    protected override NuGetBundleSpec Spec { get; } = new() { PackageList = "xunit/packages.txt", TemplatePackages = ["xunit.v3.templates"], WarmTemplates = ["xunit3"] };

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        BuildResult result = await base.Build(directory, cancellation);
        foreach (string id in version2Packages.Where(id => Directory.Exists(Path.Combine(directory, "nuget", id))))
        {
            throw new InvalidOperationException($"the xunit 2.x package {id} ended up in the bundle, but this bundle is v3 only. Find out which package depends on it.");
        }

        return result;
    }
}
