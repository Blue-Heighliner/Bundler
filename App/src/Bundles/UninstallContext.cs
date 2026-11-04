namespace BlueHeighliner.Bundler;

/// <summary>
/// What an uninstall needs to know.
/// </summary>
internal sealed record UninstallContext
{
    /// <summary>Gets the records of this bundle's install.</summary>
    public required IBundleState State { get; init; }

    /// <summary>Gets the accounts to clean up: the current ones and the ones the bundle was installed for.</summary>
    public required IReadOnlyList<string> Users { get; init; }
}
