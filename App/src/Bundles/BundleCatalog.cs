namespace BlueHeighliner.Bundler;

/// <summary>
/// All the bundles this app knows how to build and install.
/// </summary>
internal interface IBundleCatalog
{
    /// <summary>Gets every bundle in list order.</summary>
    IReadOnlyList<IBundle> All { get; }

    /// <summary>
    /// Finds a bundle by name.
    /// </summary>
    /// <param name="name">The bundle name.</param>
    /// <returns>The bundle.</returns>
    /// <exception cref="ArgumentException">There is no such bundle.</exception>
    IBundle Get(string name);
}

/// <inheritdoc />
internal sealed class BundleCatalog(IEnumerable<IBundle> bundles) : IBundleCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<IBundle> All { get; } = bundles.OrderBy(bundle => bundle.Order).ToList();

    /// <inheritdoc />
    public IBundle Get(string name)
        => All.FirstOrDefault(bundle => bundle.Name == name) ?? throw new ArgumentException($"unknown bundle '{name}' (the bundles are {string.Join(", ", All.Select(bundle => bundle.Name))})");
}
