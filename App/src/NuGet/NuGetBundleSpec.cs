namespace BlueHeighliner.Bundler;

/// <summary>
/// What a bundle that adds NuGet packages to the offline feed wants mirrored.
/// </summary>
internal sealed record NuGetBundleSpec
{
    /// <summary>Gets the embedded package list, such as <c>avalonia/packages.txt</c>.</summary>
    public required string PackageList { get; init; }

    /// <summary>Gets the dotnet template packs to install (kept in the bundle next to the feed, not in it).</summary>
    public IReadOnlyList<string> TemplatePackages { get; init; } = [];

    /// <summary>Gets the package ids whose references are removed from those template packs.</summary>
    public IReadOnlyList<string> TemplateStrip { get; init; } = [];

    /// <summary>Gets the method names those packages provide and the template code calls, which are removed as well.</summary>
    public IReadOnlyList<string> TemplateStripCalls { get; init; } = [];

    /// <summary>Gets the short names of templates to create and restore, so the packages they reference are mirrored.</summary>
    public IReadOnlyList<string> WarmTemplates { get; init; } = [];
}
