namespace BlueHeighliner.Bundler;

/// <summary>
/// A release of a program that comes as one archive for Linux x64, with the checksum its publisher states.
/// </summary>
internal sealed record ArchiveRelease
{
    /// <summary>Gets the version, such as <c>26.13.1</c>.</summary>
    public required string Version { get; init; }

    /// <summary>Gets where the archive is downloaded from.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the file name of the archive.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the checksum the publisher states for the archive.</summary>
    public required HashSpec Hash { get; init; }
}
