namespace BlueHeighliner.Bundler;

/// <summary>
/// One line of a package list.
/// </summary>
internal sealed record NuGetPackage
{
    /// <summary>Gets the package id.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the wanted version: an exact one; a version prefix with a star such as <c>10.*</c> for the latest stable release in
    /// that range; <c>dotnet</c> for the latest stable release that goes with the .NET release being built for (for a package
    /// that follows the .NET release train, such as EF Core: 10.x for net10.0, 11.x for net11.0); or <see langword="null" />
    /// for the latest stable one.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>Gets a value indicating whether the package is skipped with a warning when it has no stable release.</summary>
    public bool Optional { get; init; }

    /// <summary>Gets a value indicating whether only this package is fetched, without its dependencies (for dotnet tools such as <c>dotnet-ef</c>).</summary>
    public bool Alone { get; init; }
}
