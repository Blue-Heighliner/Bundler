namespace BlueHeighliner.Bundler;

/// <summary>
/// Finds the built bundle files in the Bundles folder, one file <c>&lt;name&gt;.bundle</c> for each bundle.
/// </summary>
internal interface IBundleFiles
{
    /// <summary>
    /// Finds the file of a bundle.
    /// </summary>
    /// <param name="name">The bundle name.</param>
    /// <returns>The full path, or <see langword="null" /> when the bundle has not been built.</returns>
    string? Find(string name);

    /// <summary>
    /// Gives the path a bundle's file is stored at, whether it exists or not.
    /// </summary>
    /// <param name="name">The bundle name.</param>
    /// <returns>The full path of the file.</returns>
    string PathFor(string name);
}

/// <inheritdoc />
internal sealed class BundleFiles(IPaths paths) : IBundleFiles
{
    /// <inheritdoc />
    public string? Find(string name)
        => File.Exists(PathFor(name)) ? PathFor(name) : null;

    /// <inheritdoc />
    public string PathFor(string name)
        => Path.Combine(paths.Bundles, $"{name}.bundle");
}
