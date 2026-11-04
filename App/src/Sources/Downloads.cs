namespace BlueHeighliner.Bundler;

/// <summary>
/// The download cache: files that are downloaded once and reused by later builds, and linked into a bundle's staging folder
/// instead of copied so a big file is not stored twice.
/// </summary>
internal interface IDownloads
{
    /// <summary>
    /// Downloads a file into the cache, or finds it there already.
    /// </summary>
    /// <param name="url">The address.</param>
    /// <param name="fileName">The file name to keep it under.</param>
    /// <param name="expected">The checksum it must have, or <see langword="null" />.</param>
    /// <param name="cancellation">Stops the download when cancelled.</param>
    /// <returns>The path of the cached file.</returns>
    Task<string> Fetch(string url, string fileName, HashSpec? expected, CancellationToken cancellation = default);

    /// <summary>
    /// Puts a file into a folder as a hard link where the disk allows it, as a copy otherwise.
    /// </summary>
    /// <param name="source">The file.</param>
    /// <param name="destination">Where it goes.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    Task Place(string source, string destination, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class Downloads(IPaths paths, IHttpFetcher http, IProcessRunner runner) : IDownloads
{
    /// <inheritdoc />
    public async Task<string> Fetch(string url, string fileName, HashSpec? expected, CancellationToken cancellation = default)
    {
        string path = Path.Combine(paths.Downloads, fileName);
        await http.Download(url, path, expected, cancellation);
        return path;
    }

    /// <inheritdoc />
    public async Task Place(string source, string destination, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Delete(destination);
        ProcessResult linked = await runner.Run(new() { FileName = "ln", Arguments = [source, destination], Check = false }, cancellation);
        if (!linked.Success)
        {
            File.Copy(source, destination, overwrite: true);
        }
    }
}
