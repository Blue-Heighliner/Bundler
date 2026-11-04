namespace BlueHeighliner.Bundler;

/// <summary>
/// What a finished build reports about a bundle.
/// </summary>
internal sealed record BuildResult
{
    /// <summary>Gets one line for every service in the bundle, with its version.</summary>
    public required IReadOnlyList<string> Components { get; init; }
}
