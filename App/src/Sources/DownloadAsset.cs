namespace BlueHeighliner.Bundler;

/// <summary>
/// A file that can be downloaded.
/// </summary>
internal sealed record DownloadAsset
{
    /// <summary>Gets the file name.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the download address.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the checksum the publisher gives, if any.</summary>
    public HashSpec? Hash { get; init; }
}
