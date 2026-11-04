namespace BlueHeighliner.Bundler;

/// <summary>
/// One row of the bundle list.
/// </summary>
internal sealed record BundleStatus
{
    /// <summary>Gets the bundle name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the one line description.</summary>
    public required string Description { get; init; }

    /// <summary>Gets the system the bundle is made for, such as <c>Ubuntu 26.04</c>.</summary>
    public required string Target { get; init; }

    /// <summary>Gets the bundles it needs.</summary>
    public required IReadOnlyList<string> Requires { get; init; }

    /// <summary>Gets where the bundle stands.</summary>
    public required BundleStanding State { get; init; }

    /// <summary>Gets when the bundle file in the bundles folder was last changed (local time), or <see langword="null" /> when there is none.</summary>
    public DateTime? FileModified { get; init; }
}
