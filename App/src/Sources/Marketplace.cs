namespace BlueHeighliner.Bundler;

/// <summary>
/// Downloads VS Code extensions from the Visual Studio Marketplace.
/// </summary>
internal interface IMarketplace
{
    /// <summary>
    /// Downloads the latest stable release of each extension, for linux-x64 where it is platform specific, together with the
    /// extensions they depend on, and writes <c>extensions.tsv</c> (id, version, file) to the folder.
    /// </summary>
    /// <param name="directory">The folder for the <c>.vsix</c> files.</param>
    /// <param name="ids">The extension ids.</param>
    /// <param name="cancellation">Stops the download when cancelled.</param>
    /// <returns>The extensions with dependencies before the extensions that need them, which is the order to install in.</returns>
    Task<IReadOnlyList<ExtensionPackage>> Fetch(string directory, IReadOnlyList<string> ids, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class Marketplace(IHttpFetcher http, IVersions versions, ILog log) : IMarketplace
{
    private static readonly string platform = "linux-x64";

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExtensionPackage>> Fetch(string directory, IReadOnlyList<string> ids, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(directory);
        HashSet<string> done = [];
        List<ExtensionPackage> order = [];
        foreach (string id in ids)
        {
            await Visit(id, directory, done, order, cancellation);
        }

        await File.WriteAllTextAsync(Path.Combine(directory, "extensions.tsv"), string.Concat(order.Select(package => $"{package.Id}\t{package.Version}\t{package.FileName}\n")), cancellation);
        return order;
    }

    private async Task Visit(string id, string directory, HashSet<string> done, List<ExtensionPackage> order, CancellationToken cancellation)
    {
        if (!done.Add(id.ToLowerInvariant()))
        {
            return;
        }

        (string version, string url) = await Latest(id, cancellation);
        string fileName = $"{id.ToLowerInvariant()}-{version}.vsix";
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path) || !IsZip(path))
        {
            log.Info($"{id} {version}");
            await http.Download(url, path, null, cancellation);
        }

        using ZipArchive zip = ZipFile.OpenRead(path);
        ZipArchiveEntry entry = zip.GetEntry("extension/package.json") ?? throw new InvalidOperationException($"{fileName} has no extension/package.json");
        using StreamReader reader = new(entry.Open());
        JsonNode manifest = JsonNode.Parse(await reader.ReadToEndAsync(cancellation))!;
        foreach (string property in new[] { "extensionDependencies", "extensionPack" })
        {
            foreach (JsonNode? dependency in manifest[property]?.AsArray() ?? [])
            {
                await Visit(dependency!.GetValue<string>(), directory, done, order, cancellation);
            }
        }

        order.Add(new(id.ToLowerInvariant(), version, fileName));
    }

    private async Task<(string Version, string Url)> Latest(string id, CancellationToken cancellation)
    {
        JsonObject query = new()
        {
            ["filters"] = new JsonArray(new JsonObject { ["criteria"] = new JsonArray(new JsonObject { ["filterType"] = 7, ["value"] = id }) }),
            ["flags"] = 1 | 2 | 16 | 32 | 128,
        };

        JsonNode answer = await http.PostJson("https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery", query, cancellation);
        JsonArray extensions = answer["results"]![0]!["extensions"]!.AsArray();
        if (extensions.Count == 0)
        {
            throw new InvalidOperationException($"extension {id} was not found in the Marketplace");
        }

        List<(string Version, string Url)> candidates = [];
        foreach (JsonNode? version in extensions[0]!["versions"]!.AsArray())
        {
            string? targetPlatform = version!["targetPlatform"]?.GetValue<string>();
            bool preRelease = version["properties"]?.AsArray().Any(property => property!["key"]!.GetValue<string>() == "Microsoft.VisualStudio.Code.PreRelease" && property["value"]!.GetValue<string>() == "true") == true;
            string? url = version["files"]?.AsArray().FirstOrDefault(file => file!["assetType"]!.GetValue<string>() == "Microsoft.VisualStudio.Services.VSIXPackage")?["source"]?.GetValue<string>();
            if ((targetPlatform is null || targetPlatform == platform) && !preRelease && url is not null)
            {
                candidates.Add((version["version"]!.GetValue<string>(), url));
            }
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException($"extension {id} has no stable release for {platform}");
        }

        return candidates.OrderBy(candidate => candidate.Version, Comparer<string>.Create(versions.Compare)).Last();
    }

    private bool IsZip(string path)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(path);
            return zip.Entries.Count > 0;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
