namespace BlueHeighliner.Bundler;

/// <summary>
/// Decides what happens to each service of a bundle given what is already on the computer, and says so in the log: a missing
/// service is installed, an older one is replaced, the same version is skipped, and a newer one is kept (an older version
/// never replaces a newer one).
/// </summary>
internal interface IDecisions
{
    /// <summary>
    /// Decides what to do with a service and logs the decision.
    /// </summary>
    /// <param name="name">What the service is called in the log.</param>
    /// <param name="have">The installed version, or <see langword="null" /> when it is not installed.</param>
    /// <param name="bundle">The version the bundle brings.</param>
    /// <returns><see langword="true" /> when the bundle's version is to be installed.</returns>
    bool Install(string name, string? have, string bundle);
}

/// <inheritdoc />
internal sealed class Decisions(IVersions versions, ILog log) : IDecisions
{
    /// <inheritdoc />
    public bool Install(string name, string? have, string bundle)
    {
        VersionAction action = versions.Decide(have, bundle);
        log.Info(action switch
        {
            VersionAction.Install => $"{name} {bundle}: not on this computer, installing",
            VersionAction.Upgrade => $"{name}: replacing {have} with the newer {bundle}",
            VersionAction.Same => $"{name} {bundle}: already installed, skipped",
            _ => $"{name}: {have} is installed and newer than the {bundle} in the bundle, kept",
        });
        return action is VersionAction.Install or VersionAction.Upgrade;
    }
}
