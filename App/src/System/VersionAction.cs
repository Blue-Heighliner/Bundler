namespace BlueHeighliner.Bundler;

/// <summary>
/// What to do with a component given what is installed and what a bundle brings.
/// </summary>
internal enum VersionAction
{
    /// <summary>Nothing is installed: install it.</summary>
    Install,

    /// <summary>The bundle's version is newer: replace the installed one.</summary>
    Upgrade,

    /// <summary>The same version is installed: skip it.</summary>
    Same,

    /// <summary>A newer version is installed: keep it, never replace it with an older one.</summary>
    Newer,
}
