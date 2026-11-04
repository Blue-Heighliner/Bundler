namespace BlueHeighliner.Bundler;

/// <summary>
/// A stable GitHub release with the assets that were asked for.
/// </summary>
internal sealed record GitHubRelease
{
    /// <summary>Gets the release tag.</summary>
    public required string Tag { get; init; }

    /// <summary>Gets the matching assets, in the order their patterns were given.</summary>
    public required IReadOnlyList<DownloadAsset> Assets { get; init; }
}
