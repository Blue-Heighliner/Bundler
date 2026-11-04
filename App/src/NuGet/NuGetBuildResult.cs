namespace BlueHeighliner.Bundler;

/// <summary>
/// What mirroring packages produced.
/// </summary>
internal sealed record NuGetBuildResult
{
    /// <summary>Gets one line per listed package, per template pack, and a total.</summary>
    public required IReadOnlyList<string> Components { get; init; }
}
