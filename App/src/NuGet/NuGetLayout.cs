namespace BlueHeighliner.Bundler;

/// <summary>
/// Puts loose <c>.nupkg</c> files into the folder layout of a NuGet feed: <c>id/version/id.version.nupkg</c> with its
/// <c>.sha512</c> and <c>.nuspec</c>. NuGet only treats a folder as a hierarchical feed when the hash files are there.
/// </summary>
internal interface INuGetLayout
{
    /// <summary>
    /// Reads the id and version of a package.
    /// </summary>
    /// <param name="nupkg">The package file.</param>
    /// <returns>The id and version as written in the package.</returns>
    (string Id, string Version) Identify(string nupkg);

    /// <summary>
    /// Adds a package file to a feed folder.
    /// </summary>
    /// <param name="feed">The feed folder.</param>
    /// <param name="nupkg">The package file.</param>
    /// <returns>The folder the package was placed in.</returns>
    string Place(string feed, string nupkg);
}

/// <inheritdoc />
internal sealed class NuGetLayout : INuGetLayout
{
    /// <inheritdoc />
    public (string Id, string Version) Identify(string nupkg)
    {
        using ZipArchive zip = ZipFile.OpenRead(nupkg);
        XElement metadata = Metadata(zip);
        return (metadata.Elements().First(element => element.Name.LocalName == "id").Value.Trim(), metadata.Elements().First(element => element.Name.LocalName == "version").Value.Trim());
    }

    /// <inheritdoc />
    public string Place(string feed, string nupkg)
    {
        (string id, string version) = Identify(nupkg);
        string folder = Path.Combine(feed, id.ToLowerInvariant(), version.ToLowerInvariant());
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, $"{id.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg");
        File.Copy(nupkg, target, overwrite: true);
        File.WriteAllText(target + ".sha512", Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(target))));
        using ZipArchive zip = ZipFile.OpenRead(nupkg);
        ZipArchiveEntry spec = zip.Entries.First(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !entry.FullName.Contains('/'));
        spec.ExtractToFile(Path.Combine(folder, $"{id.ToLowerInvariant()}.nuspec"), overwrite: true);
        return folder;
    }

    private XElement Metadata(ZipArchive zip)
    {
        ZipArchiveEntry spec = zip.Entries.First(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !entry.FullName.Contains('/'));
        using Stream stream = spec.Open();
        return XDocument.Load(stream).Root!.Elements().First(element => element.Name.LocalName == "metadata");
    }
}
