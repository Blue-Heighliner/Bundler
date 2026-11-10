namespace BlueHeighliner.Bundler;

/// <summary>
/// Looks up the latest stable release of each download source.
/// </summary>
internal interface IReleaseSources
{
    /// <summary>
    /// Finds the newest SDK of the newest .NET channel that is in active or maintenance support. Previews, release candidates
    /// and go-live builds of the next version do not count as stable.
    /// </summary>
    /// <param name="cancellation">Stops the lookup when cancelled.</param>
    /// <returns>The release.</returns>
    Task<DotNetRelease> LatestDotNet(CancellationToken cancellation = default);

    /// <summary>
    /// Finds the latest stable VS Code <c>.deb</c>.
    /// </summary>
    /// <param name="cancellation">Stops the lookup when cancelled.</param>
    /// <returns>The release.</returns>
    Task<VsCodeRelease> LatestVsCode(CancellationToken cancellation = default);

    /// <summary>
    /// Finds the latest release of a GitHub repository that is not a pre-release.
    /// </summary>
    /// <param name="repository">The repository, <c>owner/name</c>.</param>
    /// <param name="patterns">One regular expression per wanted asset; the first asset whose name matches is taken.</param>
    /// <param name="cancellation">Stops the lookup when cancelled.</param>
    /// <returns>The release.</returns>
    /// <exception cref="InvalidOperationException">The release is not stable or an asset is missing.</exception>
    Task<GitHubRelease> LatestGitHub(string repository, IReadOnlyList<string> patterns, CancellationToken cancellation = default);

    /// <summary>
    /// Finds the newest release of a GitHub repository whose tag matches a pattern and that is not a pre-release or a draft, for a
    /// repository whose "latest" release is not the newest one (it publishes several lines of versions) or that has to stay on one
    /// major version.
    /// </summary>
    /// <param name="repository">The repository, <c>owner/name</c>.</param>
    /// <param name="tagPattern">A regular expression a tag has to match, in which the first group is the version.</param>
    /// <param name="patterns">One regular expression per wanted asset; the first asset whose name matches is taken.</param>
    /// <param name="cancellation">Stops the lookup when cancelled.</param>
    /// <returns>The release.</returns>
    /// <exception cref="InvalidOperationException">No stable release matches or an asset is missing.</exception>
    Task<GitHubRelease> NewestGitHub(string repository, string tagPattern, IReadOnlyList<string> patterns, CancellationToken cancellation = default);

    /// <summary>
    /// Finds the newest Blender release that has a Linux x64 archive. download.blender.org only lists releases, never previews.
    /// </summary>
    /// <param name="cancellation">Stops the lookup when cancelled.</param>
    /// <returns>The release.</returns>
    /// <exception cref="InvalidOperationException">No release with a Linux x64 archive and a checksum is found.</exception>
    Task<BlenderRelease> LatestBlender(CancellationToken cancellation = default);

    /// <summary>
    /// Finds a release of a GitHub repository by its tag, for a component that is pinned to one version on purpose.
    /// </summary>
    /// <param name="repository">The repository, <c>owner/name</c>.</param>
    /// <param name="tag">The release tag.</param>
    /// <param name="patterns">One regular expression per wanted asset; the first asset whose name matches is taken.</param>
    /// <param name="cancellation">Stops the lookup when cancelled.</param>
    /// <returns>The release.</returns>
    /// <exception cref="InvalidOperationException">The release is a draft or an asset is missing.</exception>
    Task<GitHubRelease> GitHubTag(string repository, string tag, IReadOnlyList<string> patterns, CancellationToken cancellation = default);

    /// <summary>
    /// Finds the highest stable version of a NuGet package.
    /// </summary>
    /// <param name="packageId">The package id.</param>
    /// <param name="cancellation">Stops the lookup when cancelled.</param>
    /// <returns>The version, or <see langword="null" /> when the package does not exist or has no stable release.</returns>
    Task<string?> LatestNuGet(string packageId, CancellationToken cancellation = default);

    /// <summary>
    /// Reads the driver version NVIDIA lists as its latest stable release.
    /// </summary>
    /// <param name="cancellation">Stops the lookup when cancelled.</param>
    /// <returns>The version, such as <c>595.104.02</c>.</returns>
    Task<string> LatestNvidiaDriver(CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class ReleaseSources(IHttpFetcher http, IVersions versions) : IReleaseSources
{
    /// <inheritdoc />
    public async Task<DotNetRelease> LatestDotNet(CancellationToken cancellation = default)
    {
        JsonNode index = await http.GetJson("https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json", cancellation: cancellation);
        JsonNode channel = index["releases-index"]!.AsArray()
            .Where(node => node!["support-phase"]!.GetValue<string>() is "active" or "maintenance")
            .OrderBy(node => node!["channel-version"]!.GetValue<string>(), Comparer<string>.Create(versions.Compare))
            .Last()!;

        JsonNode releases = await http.GetJson(channel["releases.json"]!.GetValue<string>(), cancellation: cancellation);
        string sdkVersion = releases["latest-sdk"]!.GetValue<string>();
        foreach (JsonNode? release in releases["releases"]!.AsArray())
        {
            List<JsonNode?> sdks = [release!["sdk"], .. release["sdks"]?.AsArray() ?? []];
            foreach (JsonNode? sdk in sdks.Where(sdk => sdk?["version"]?.GetValue<string>() == sdkVersion))
            {
                foreach (JsonNode? file in sdk!["files"]?.AsArray() ?? [])
                {
                    if (file!["name"]!.GetValue<string>() == "dotnet-sdk-linux-x64.tar.gz")
                    {
                        return new()
                        {
                            Channel = channel["channel-version"]!.GetValue<string>(),
                            Version = sdkVersion,
                            Runtime = release["release-version"]!.GetValue<string>(),
                            Url = file["url"]!.GetValue<string>(),
                            Hash = new("sha512", file["hash"]!.GetValue<string>().ToLowerInvariant()),
                            FileName = $"dotnet-sdk-{sdkVersion}-linux-x64.tar.gz",
                        };
                    }
                }
            }
        }

        throw new InvalidOperationException($"no linux-x64 tarball found for .NET SDK {sdkVersion}");
    }

    /// <inheritdoc />
    public async Task<VsCodeRelease> LatestVsCode(CancellationToken cancellation = default)
    {
        JsonNode release = await http.GetJson("https://update.code.visualstudio.com/api/update/linux-deb-x64/stable/latest", cancellation: cancellation);
        string version = release["productVersion"]!.GetValue<string>();
        return new()
        {
            Version = version,
            Url = release["url"]!.GetValue<string>(),
            Hash = new("sha256", release["sha256hash"]!.GetValue<string>().ToLowerInvariant()),
            FileName = $"code_{version}_amd64.deb",
        };
    }

    /// <inheritdoc />
    public async Task<GitHubRelease> LatestGitHub(string repository, IReadOnlyList<string> patterns, CancellationToken cancellation = default)
    {
        JsonNode release = await http.GetJson($"https://api.github.com/repos/{repository}/releases/latest", null, cancellation);
        string tag = release["tag_name"]!.GetValue<string>();
        if (release["prerelease"]?.GetValue<bool>() == true || release["draft"]?.GetValue<bool>() == true)
        {
            throw new InvalidOperationException($"{repository}: the latest release is not a stable release");
        }

        return new() { Tag = tag, Assets = Assets(release, repository, tag, patterns) };
    }

    /// <inheritdoc />
    public async Task<GitHubRelease> GitHubTag(string repository, string tag, IReadOnlyList<string> patterns, CancellationToken cancellation = default)
    {
        JsonNode release = await http.GetJson($"https://api.github.com/repos/{repository}/releases/tags/{tag}", null, cancellation);
        if (release["draft"]?.GetValue<bool>() == true)
        {
            throw new InvalidOperationException($"{repository} {tag}: the release is a draft");
        }

        return new() { Tag = tag, Assets = Assets(release, repository, tag, patterns) };
    }

    /// <inheritdoc />
    public async Task<GitHubRelease> NewestGitHub(string repository, string tagPattern, IReadOnlyList<string> patterns, CancellationToken cancellation = default)
    {
        JsonNode releases = await http.GetJson($"https://api.github.com/repos/{repository}/releases?per_page=100", null, cancellation);
        (JsonNode release, string tag, string version)? newest = null;
        foreach (JsonNode? node in releases.AsArray())
        {
            string tag = node!["tag_name"]!.GetValue<string>();
            Match match = Regex.Match(tag, tagPattern);
            if (!match.Success || node["prerelease"]?.GetValue<bool>() == true || node["draft"]?.GetValue<bool>() == true)
            {
                continue;
            }

            string version = match.Groups[1].Value;
            if (newest is null || versions.Compare(version, newest.Value.version) > 0)
            {
                newest = (node, tag, version);
            }
        }

        if (newest is null)
        {
            throw new InvalidOperationException($"{repository}: no stable release has a tag that matches {tagPattern}");
        }

        return new() { Tag = newest.Value.tag, Assets = Assets(newest.Value.release, repository, newest.Value.tag, patterns) };
    }

    /// <inheritdoc />
    public async Task<BlenderRelease> LatestBlender(CancellationToken cancellation = default)
    {
        string root = "https://download.blender.org/release/";
        Comparer<string> order = Comparer<string>.Create(versions.Compare);
        string index = await http.GetString(root, cancellation: cancellation);
        foreach (string series in Regex.Matches(index, @"href=""Blender(\d+\.\d+)/""").Select(match => match.Groups[1].Value).Distinct().OrderDescending(order))
        {
            string folder = $"{root}Blender{series}/";
            string listing = await http.GetString(folder, cancellation: cancellation);
            string? version = Regex.Matches(listing, @"href=""blender-(\d+\.\d+\.\d+)-linux-x64\.tar\.xz""").Select(match => match.Groups[1].Value).Distinct().OrderDescending(order).FirstOrDefault();
            if (version is null)
            {
                continue;
            }

            string fileName = $"blender-{version}-linux-x64.tar.xz";
            string sums = await http.GetString($"{folder}blender-{version}.sha256", cancellation: cancellation);
            string hash = sums.Split('\n').Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(fields => fields.Length == 2 && fields[1].TrimStart('*') == fileName).Select(fields => fields[0]).FirstOrDefault()
                ?? throw new InvalidOperationException($"Blender {version}: no checksum for {fileName}");
            return new() { Version = version, Url = folder + fileName, FileName = fileName, Hash = new("sha256", hash.ToLowerInvariant()) };
        }

        throw new InvalidOperationException("no Blender release with a Linux x64 archive was found");
    }

    /// <inheritdoc />
    public async Task<string?> LatestNuGet(string packageId, CancellationToken cancellation = default)
    {
        try
        {
            JsonNode index = await http.GetJson($"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/index.json", cancellation: cancellation);
            return index["versions"]!.AsArray().Select(node => node!.GetValue<string>()).Where(version => !version.Contains('-'))
                .OrderBy(version => version, Comparer<string>.Create(versions.Compare)).LastOrDefault();
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string> LatestNvidiaDriver(CancellationToken cancellation = default)
    {
        string text = (await http.GetString("https://download.nvidia.com/XFree86/Linux-x86_64/latest.txt", cancellation: cancellation)).Split(' ', '\n')[0].Trim();
        if (!Regex.IsMatch(text, @"^\d+\.\d+"))
        {
            throw new InvalidOperationException($"unexpected answer from NVIDIA: '{text}'");
        }

        return text;
    }

    private List<DownloadAsset> Assets(JsonNode release, string repository, string tag, IReadOnlyList<string> patterns)
    {
        List<DownloadAsset> assets = [];
        foreach (string pattern in patterns)
        {
            JsonNode asset = release["assets"]!.AsArray().FirstOrDefault(node => Regex.IsMatch(node!["name"]!.GetValue<string>(), pattern))
                ?? throw new InvalidOperationException($"{repository} {tag}: no asset matches {pattern}");
            assets.Add(new()
            {
                FileName = asset["name"]!.GetValue<string>(),
                Url = asset["browser_download_url"]!.GetValue<string>(),
                Hash = HashSpec.Parse(asset["digest"]?.GetValue<string>()),
            });
        }

        return assets;
    }
}
