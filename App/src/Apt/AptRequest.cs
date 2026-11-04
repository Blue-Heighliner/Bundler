namespace BlueHeighliner.Bundler;

/// <summary>
/// The packages a bundle asks apt for, which decides what ends up in the bundle's local repository.
/// </summary>
internal sealed record AptRequest
{
    /// <summary>Gets the packages that are the bundle's services: names, or paths of local <c>.deb</c> files. At install they are upgraded when the bundle's version is newer.</summary>
    public IReadOnlyList<string> Services { get; init; } = [];

    /// <summary>Gets the packages that only have to exist: at install they are installed when missing and never upgraded on their own.</summary>
    public IReadOnlyList<string> Support { get; init; } = [];
}
