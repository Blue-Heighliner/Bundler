namespace BlueHeighliner.Bundler;

/// <summary>
/// A .NET SDK release.
/// </summary>
internal sealed record DotNetRelease
{
    /// <summary>Gets the channel, such as <c>10.0</c>.</summary>
    public required string Channel { get; init; }

    /// <summary>Gets the SDK version.</summary>
    public required string Version { get; init; }

    /// <summary>Gets the runtime version that ships with the SDK.</summary>
    public required string Runtime { get; init; }

    /// <summary>Gets the download address of the linux-x64 tarball.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the checksum of the tarball.</summary>
    public required HashSpec Hash { get; init; }

    /// <summary>Gets the file name to keep the tarball under.</summary>
    public required string FileName { get; init; }
}
