namespace BlueHeighliner.Bundler;

/// <summary>
/// Opens the records of installed bundles.
/// </summary>
internal interface IStateStore
{
    /// <summary>
    /// Opens the records of a bundle.
    /// </summary>
    /// <param name="bundle">The bundle name.</param>
    /// <returns>The records.</returns>
    IBundleState Get(string bundle);
}

/// <inheritdoc />
internal sealed class StateStore(IPaths paths) : IStateStore
{
    /// <inheritdoc />
    public IBundleState Get(string bundle)
        => new BundleState(Path.Combine(paths.State, bundle));
}
