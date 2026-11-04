namespace BlueHeighliner.Bundler;

/// <summary>
/// What an install needs to know.
/// </summary>
internal sealed record InstallContext
{
    /// <summary>Gets the folder the bundle was unpacked into. Files may be moved out of it: it is deleted afterwards.</summary>
    public required string Payload { get; init; }

    /// <summary>Gets the records of this bundle's install.</summary>
    public required IBundleState State { get; init; }

    /// <summary>Gets the accounts to set up VS Code, templates and so on for.</summary>
    public required IReadOnlyList<string> Users { get; init; }

    /// <summary>Gets what the bundle says about itself.</summary>
    public required BundleInfo Info { get; init; }
}
