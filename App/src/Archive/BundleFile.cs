namespace BlueHeighliner.Bundler;

/// <summary>
/// A bundle as it is in the Bundles folder: one <c>name.bundle</c> file, or its parts <c>name.bundle.001</c>, <c>name.bundle.002</c>
/// and so on, which together are the bytes of the one file.
/// </summary>
internal sealed record BundleFile
{
    /// <summary>Gets the name of the bundle.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the paths of the files, in order; one for a bundle that is not split.</summary>
    public required IReadOnlyList<string> Paths { get; init; }

    /// <summary>Gets the size of all the files together.</summary>
    public required long Length { get; init; }

    /// <summary>Gets when the newest of the files was last changed.</summary>
    public required DateTime Modified { get; init; }

    /// <summary>Gets a value indicating whether the bundle is in parts.</summary>
    public bool IsSplit { get; init; }

    /// <summary>Gets what is wrong with the parts (one is missing, or one is too small to be complete), or <see langword="null" /> when they are fine.</summary>
    public string? Problem { get; init; }

    /// <summary>
    /// Tells whether the files are laid out the way a build with a size limit would lay them out.
    /// </summary>
    /// <param name="limit">The largest size a file may have, or <see langword="null" /> for no limit, which is one file.</param>
    /// <returns><see langword="true" /> when a build with this limit would produce the same file sizes.</returns>
    public bool Matches(long? limit)
    {
        if (limit is not long maximum)
        {
            return !IsSplit;
        }

        long[] sizes = [.. Paths.Select(path => new FileInfo(path).Length)];
        return sizes.Length > 0 && sizes[..^1].All(size => size == maximum) && sizes[^1] <= maximum && (IsSplit || sizes.Length == 1);
    }
}
