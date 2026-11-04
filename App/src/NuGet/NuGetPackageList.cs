namespace BlueHeighliner.Bundler;

/// <summary>
/// Reads package lists: one NuGet id per line as <c>Id</c> (latest stable), <c>Id@1.2.3</c> (that version), <c>?Id</c>
/// (optional) or <c>=Id</c> (just that package); <c>#</c> starts a comment.
/// </summary>
internal interface INuGetPackageList
{
    /// <summary>
    /// Reads a package list.
    /// </summary>
    /// <param name="text">The list.</param>
    /// <returns>The packages in list order.</returns>
    IReadOnlyList<NuGetPackage> Parse(string text);

    /// <summary>
    /// Tells whether a package is a runtime or apphost pack for one platform, which cannot be restored as an ordinary reference.
    /// </summary>
    /// <param name="id">The package id.</param>
    /// <returns><see langword="true" /> for a platform pack.</returns>
    bool IsPlatformPack(string id);
}

/// <inheritdoc />
internal sealed partial class NuGetPackageList : INuGetPackageList
{
    /// <inheritdoc />
    public IReadOnlyList<NuGetPackage> Parse(string text)
    {
        List<NuGetPackage> packages = [];
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            bool optional = line.StartsWith('?');
            bool alone = line.StartsWith('=');
            line = line.TrimStart('?', '=');
            string[] parts = line.Split('@', 2);
            packages.Add(new() { Id = parts[0], Version = parts.Length > 1 ? parts[1] : null, Optional = optional, Alone = alone });
        }

        return packages;
    }

    /// <inheritdoc />
    public bool IsPlatformPack(string id)
        => PlatformPattern().IsMatch(id);

    [GeneratedRegex(@"\.(linux|linux-musl|win|osx)-(x64|arm64|arm|x86)$")]
    private static partial Regex PlatformPattern();
}
