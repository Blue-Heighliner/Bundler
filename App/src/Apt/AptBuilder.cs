namespace BlueHeighliner.Bundler;

/// <summary>
/// Collects Ubuntu packages for a bundle. A private apt root in the build cache (no root rights needed) is pointed at the
/// Ubuntu 24.04 archive whatever Ubuntu the build computer runs; it works out which <c>.deb</c> files the packages need on a fresh
/// 24.04 system and downloads them into a small local repository that the install uses without any network.
/// </summary>
internal interface IAptBuilder
{
    /// <summary>
    /// Creates the private apt root and refreshes its package lists.
    /// </summary>
    /// <param name="extraSources">Source lines to add to the standard Ubuntu ones, such as a PPA from <see cref="PpaSource" />.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the lists are current.</returns>
    Task Prepare(IReadOnlyList<string> extraSources, CancellationToken cancellation = default);

    /// <summary>
    /// Fetches the signing key of a Launchpad PPA and gives its source line.
    /// </summary>
    /// <param name="ppa">The PPA, <c>owner/name</c>.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The source line.</returns>
    Task<string> PpaSource(string ppa, CancellationToken cancellation = default);

    /// <summary>
    /// Fetches the signing key of a third-party apt repository and gives its source line.
    /// </summary>
    /// <param name="keyUrl">Where the key is.</param>
    /// <param name="name">A name for the key file.</param>
    /// <param name="uri">The repository address.</param>
    /// <param name="suite">The suite.</param>
    /// <param name="component">The component.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The source line.</returns>
    Task<string> ExternalSource(string keyUrl, string name, string uri, string suite, string component, CancellationToken cancellation = default);

    /// <summary>
    /// Asks whether a package is in the package lists.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns><see langword="true" /> when it exists.</returns>
    Task<bool> Exists(string package, CancellationToken cancellation = default);

    /// <summary>
    /// Finds the highest-sorting package whose name matches, for names that change with the release (such as <c>libicu78</c>).
    /// </summary>
    /// <param name="pattern">A regular expression for the whole name.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The name, or <see langword="null" /> when none matches.</returns>
    Task<string?> Find(string pattern, CancellationToken cancellation = default);

    /// <summary>
    /// Finds every package whose whole name matches a regular expression.
    /// </summary>
    /// <param name="pattern">The regular expression.</param>
    /// <param name="cancellation">Stops the search when cancelled.</param>
    /// <returns>The names, in version order (lowest first); empty when there are none.</returns>
    Task<IReadOnlyList<string>> FindAll(string pattern, CancellationToken cancellation = default);

    /// <summary>
    /// Downloads the packages and everything they need that a fresh Ubuntu 24.04 does not have, and writes the repository
    /// index. Essential and required packages are left out, but only at the version 24.04 was released with: once such a
    /// package has been updated, a computer without the update could not satisfy an exact version requirement (perl needs
    /// perl-base at its own version), so the updated one is included. The names of the services and support packages are
    /// written to <c>top-level.txt</c> and <c>support.txt</c>.
    /// </summary>
    /// <param name="directory">The folder that becomes the bundle's repository.</param>
    /// <param name="request">The packages.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>Every package in the folder.</returns>
    Task<IReadOnlyList<DebPackage>> Download(string directory, AptRequest request, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed partial class AptBuilder(IPaths paths, IProcessRunner runner, IHttpFetcher http, IDebIndex index, IVersions versions, ILog log) : IAptBuilder
{
    private static readonly string keyring = "/usr/share/keyrings/ubuntu-archive-keyring.gpg";

    private string Root => Path.Combine(paths.Cache, "apt-" + Release);

    private string Release => "noble";

    private string Mirror => "http://us.archive.ubuntu.com/ubuntu";

    /// <inheritdoc />
    public async Task Prepare(IReadOnlyList<string> extraSources, CancellationToken cancellation = default)
    {
        if (!File.Exists(keyring))
        {
            throw new InvalidOperationException($"{keyring} not found: install the ubuntu-keyring package");
        }

        foreach (string folder in new[] { "state/lists/partial", "cache/archives/partial", "none" })
        {
            Directory.CreateDirectory(Path.Combine(Root, folder));
        }

        await File.WriteAllTextAsync(Path.Combine(Root, "status"), string.Empty, cancellation);
        IEnumerable<string> ubuntu = new[] { Release, Release + "-updates", Release + "-security" }
            .Select(pocket => $"deb [arch=amd64 signed-by={keyring}] {Mirror} {pocket} main restricted universe multiverse");
        await File.WriteAllLinesAsync(Path.Combine(Root, "sources.list"), ubuntu.Concat(extraSources), cancellation);

        log.Step($"Refreshing the Ubuntu {Release} package lists");
        ProcessResult result = await runner.Run(new() { FileName = "apt-get", Arguments = [.. Options, "-qq", "update"], Check = false }, cancellation);
        if (!result.Success)
        {
            throw new InvalidOperationException($"could not refresh the package lists (is this computer online?): {result.Error.Trim()}");
        }
    }

    /// <inheritdoc />
    public async Task<string> PpaSource(string ppa, CancellationToken cancellation = default)
    {
        string[] parts = ppa.Split('/');
        JsonNode archive = await http.GetJson($"https://api.launchpad.net/1.0/~{parts[0]}/+archive/ubuntu/{parts[1]}", cancellation: cancellation);
        string fingerprint = archive["signing_key_fingerprint"]!.GetValue<string>();
        string keyFile = await Key($"https://keyserver.ubuntu.com/pks/lookup?op=get&search=0x{fingerprint}", $"{parts[0]}-{parts[1]}", cancellation);
        return $"deb [arch=amd64 signed-by={keyFile}] https://ppa.launchpadcontent.net/{parts[0]}/{parts[1]}/ubuntu {Release} main";
    }

    /// <inheritdoc />
    public async Task<string> ExternalSource(string keyUrl, string name, string uri, string suite, string component, CancellationToken cancellation = default)
        => $"deb [arch=amd64 signed-by={await Key(keyUrl, name, cancellation)}] {uri} {suite} {component}";

    /// <inheritdoc />
    public async Task<bool> Exists(string package, CancellationToken cancellation = default)
        => (await runner.Run(new() { FileName = "apt-cache", Arguments = [.. Options, "show", "--no-all-versions", package], Check = false }, cancellation)).Success;

    /// <inheritdoc />
    public async Task<string?> Find(string pattern, CancellationToken cancellation = default)
        => (await FindAll(pattern, cancellation)).LastOrDefault();

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FindAll(string pattern, CancellationToken cancellation = default)
    {
        ProcessResult names = await runner.Run(new() { FileName = "apt-cache", Arguments = [.. Options, "pkgnames"] }, cancellation);
        Regex expression = new($"^(?:{pattern})$");
        return [.. names.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(name => expression.IsMatch(name)).OrderBy(name => name, Comparer<string>.Create(versions.Compare))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DebPackage>> Download(string directory, AptRequest request, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(directory);
        List<string> arguments = [];
        List<string> localNames = [];
        Dictionary<string, string> names = [];
        foreach (string package in request.Services.Concat(request.Support))
        {
            if (package.EndsWith(".deb", StringComparison.Ordinal))
            {
                File.Copy(package, Path.Combine(directory, Path.GetFileName(package)), overwrite: true);
                arguments.Add(Path.GetFullPath(package));
                names[package] = index.Field(await index.Control(package, cancellation), "Package");
                localNames.Add(names[package]);
            }
            else
            {
                arguments.Add(package);
                names[package] = package;
            }
        }

        log.Step($"Working out the packages needed on a fresh Ubuntu {Release}: {string.Join(' ', request.Services.Concat(request.Support).Select(Path.GetFileName))}");
        ProcessResult simulation = await runner.Run(new() { FileName = "apt-get", Arguments = [.. Options, "-s", "-qq", "install", .. arguments], Check = false }, cancellation);
        List<(string Name, string Version)> plan = PlanPattern().Matches(simulation.Output)
            .Select(match => (match.Groups["name"].Value.Replace(":amd64", string.Empty), match.Groups["version"].Value)).ToList();
        if (plan.Count == 0)
        {
            throw new InvalidOperationException($"apt could not resolve: {string.Join(' ', arguments)}\n{simulation.Error.Trim()}");
        }

        HashSet<string> baseSystem = await BaseSystem(plan.Select(item => item.Name), cancellation);
        Dictionary<string, string> releaseVersions = await ReleaseVersions(plan.Select(item => item.Name).Except(localNames), cancellation);
        HashSet<string> serviceNames = [.. request.Services.Select(package => names[package])];
        HashSet<(string Name, string Version)> extras = [];
        foreach ((string name, string version) in plan)
        {
            bool isRelease = releaseVersions.TryGetValue(name, out string? released) && released == version;
            if ((baseSystem.Contains(name) && isRelease) || localNames.Contains(name))
            {
                continue;
            }

            log.Info($"{name} {version}");
            await runner.Run(new() { FileName = "apt-get", Arguments = [.. Options, "-qq", "download", $"{name}={version}"], WorkingDirectory = directory }, cancellation);
            if (!isRelease && released is not null && !baseSystem.Contains(name) && !serviceNames.Contains(name))
            {
                log.Info($"{name} {released} (the release version, for a computer that has not updated it)");
                await runner.Run(new() { FileName = "apt-get", Arguments = [.. Options, "-qq", "download", $"{name}={released}"], WorkingDirectory = directory }, cancellation);
                extras.Add((name, released));
            }
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*%3a*.deb"))
        {
            File.Move(file, file.Replace("%3a", "-"), overwrite: true);
        }

        await File.WriteAllLinesAsync(Path.Combine(directory, "top-level.txt"), request.Services.Select(package => names[package]), cancellation);
        await File.WriteAllLinesAsync(Path.Combine(directory, "support.txt"), request.Support.Select(package => names[package]), cancellation);
        return [.. (await index.Write(directory, cancellation)).Where(package => !extras.Contains((package.Package, package.Version)))];
    }

    [GeneratedRegex(@"^Inst (?<name>\S+) \((?<version>\S+) ", RegexOptions.Multiline)]
    private static partial Regex PlanPattern();

    private IReadOnlyList<string> Options
        =>
        [
            "-o", $"Dir::State={Root}/state", "-o", $"Dir::State::lists={Root}/state/lists", "-o", $"Dir::State::status={Root}/status",
            "-o", $"Dir::Cache={Root}/cache", "-o", $"Dir::Cache::archives={Root}/cache/archives",
            "-o", $"Dir::Etc::sourcelist={Root}/sources.list", "-o", $"Dir::Etc::sourceparts={Root}/none",
            "-o", $"Dir::Etc::preferences={Root}/none/prefs", "-o", $"Dir::Etc::preferencesparts={Root}/none",
            "-o", "Debug::NoLocking=true", "-o", "APT::Architecture=amd64", "-o", "APT::Architectures::=amd64",
            "-o", "Acquire::Languages=none", "-o", "APT::Install-Recommends=false", "-o", "APT::Get::List-Cleanup=false",
        ];

    private async Task<string> Key(string url, string name, CancellationToken cancellation)
    {
        string path = Path.Combine(Root, "keys", name + ".asc");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, await http.GetString(url, cancellation: cancellation), cancellation);
        return path;
    }

    private async Task<HashSet<string>> BaseSystem(IEnumerable<string> names, CancellationToken cancellation)
    {
        ProcessResult shown = await runner.Run(new() { FileName = "apt-cache", Arguments = [.. Options, "show", "--no-all-versions", .. names], Check = false }, cancellation);
        HashSet<string> baseSystem = [];
        foreach (string paragraph in shown.Output.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (index.Field(paragraph, "Essential") == "yes" || index.Field(paragraph, "Priority") == "required")
            {
                baseSystem.Add(index.Field(paragraph, "Package"));
            }
        }

        return baseSystem;
    }

    private async Task<Dictionary<string, string>> ReleaseVersions(IEnumerable<string> names, CancellationToken cancellation)
    {
        Dictionary<string, string> released = [];
        string[] wanted = [.. names.Distinct()];
        if (wanted.Length == 0)
        {
            return released;
        }

        ProcessResult madison = await runner.Run(new() { FileName = "apt-cache", Arguments = [.. Options, "madison", .. wanted], Check = false }, cancellation);
        foreach (string line in madison.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('|');
            string[] source = fields.Length >= 3 ? fields[2].Trim().Split(' ') : [];
            if (source.Length >= 2 && source[1].StartsWith(Release + "/", StringComparison.Ordinal))
            {
                string name = fields[0].Trim();
                string version = fields[1].Trim();
                if (!released.TryGetValue(name, out string? known) || versions.CompareDebian(version, known) > 0)
                {
                    released[name] = version;
                }
            }
        }

        return released;
    }
}
