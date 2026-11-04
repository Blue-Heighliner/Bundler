namespace BlueHeighliner.Bundler;

/// <summary>
/// A VS Code release.
/// </summary>
internal sealed record VsCodeRelease
{
    /// <summary>Gets the product version.</summary>
    public required string Version { get; init; }

    /// <summary>Gets the download address of the amd64 <c>.deb</c>.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the checksum of the package.</summary>
    public required HashSpec Hash { get; init; }

    /// <summary>Gets the file name to keep the package under.</summary>
    public required string FileName { get; init; }
}
