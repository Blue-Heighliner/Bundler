namespace BlueHeighliner.Bundler;

/// <summary>
/// Where a bundle stands on this computer.
/// </summary>
internal enum BundleStanding
{
    /// <summary>There is no bundle file for it in the Bundles folder.</summary>
    NotBuilt,

    /// <summary>A bundle file is there and it is not installed.</summary>
    Available,

    /// <summary>It is installed.</summary>
    Installed,
}
