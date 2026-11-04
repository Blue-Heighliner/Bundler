namespace BlueHeighliner.Bundler;

/// <summary>
/// A Blender release on download.blender.org.
/// </summary>
internal sealed record BlenderRelease
{
    /// <summary>Gets the version, such as <c>5.2.2</c>.</summary>
    public required string Version { get; init; }

    /// <summary>Gets the download address of the Linux x64 archive.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the file name of the archive.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the SHA-256 checksum Blender publishes for the archive.</summary>
    public required HashSpec Hash { get; init; }
}
