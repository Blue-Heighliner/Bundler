namespace BlueHeighliner.Bundler;

/// <summary>
/// One line of a package list.
/// </summary>
internal sealed record NuGetPackage
{
    /// <summary>Gets the package id.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the wanted version, or <see langword="null" /> for the latest stable one.</summary>
    public string? Version { get; init; }

    /// <summary>Gets a value indicating whether the package is skipped with a warning when it has no stable release.</summary>
    public bool Optional { get; init; }

    /// <summary>Gets a value indicating whether only this package is fetched, without its dependencies (for dotnet tools such as <c>dotnet-ef</c>).</summary>
    public bool Alone { get; init; }
}
