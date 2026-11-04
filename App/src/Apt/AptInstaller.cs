namespace BlueHeighliner.Bundler;

/// <summary>
/// Installs and removes the Ubuntu packages of a bundle on the offline computer. Apt only ever sees the bundle's own local
/// repository, so nothing tries to reach the network.
/// </summary>
internal interface IAptInstaller
{
    /// <summary>
    /// Finds the installed version of a package.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The version, or <see langword="null" /> when the package is not installed.</returns>
    Task<string?> InstalledVersion(string package, CancellationToken cancellation = default);

    /// <summary>
    /// Installs the bundle's service packages that are missing or older than the bundle's version (a newer or equal one is
    /// kept), and the support packages that are missing. What was added is recorded in the bundle's state for the uninstall.
    /// </summary>
    /// <param name="directory">The unpacked repository folder.</param>
    /// <param name="state">The bundle's installed-state records.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the packages are installed.</returns>
    Task InstallRecorded(string directory, IBundleState state, CancellationToken cancellation = default);

    /// <summary>
    /// Removes the packages an install added, those that nothing else needs any more (including ones requested by name such
    /// as zstd). Apt never removes kernels by itself, so kernel packages on the list go too, except the running kernel. A package that something else still needs stays, and is remembered in <c>apt-leftover</c> next to the bundles' records, so whichever uninstall comes after that other thing is gone removes it too.
    /// </summary>
    /// <param name="state">The bundle's installed-state records.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the packages are removed.</returns>
    Task RemoveAdded(IBundleState state, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed partial class AptInstaller(IProcessRunner runner, IDebIndex index, IVersions versions, ILog log) : IAptInstaller
{
    private static readonly string recordFile = "apt-new";

    private static readonly string leftoverName = "apt-leftover";

    private static readonly Dictionary<string, string?> noninteractive = new() { ["DEBIAN_FRONTEND"] = "noninteractive" };

    /// <inheritdoc />
    public async Task<string?> InstalledVersion(string package, CancellationToken cancellation = default)
    {
        ProcessResult result = await runner.Run(new() { FileName = "dpkg-query", Arguments = ["-W", "-f", "${db:Status-Status} ${Version}\n", package], Check = false }, cancellation);
        string? line = result.Output.Split('\n').FirstOrDefault(entry => entry.StartsWith("installed ", StringComparison.Ordinal));
        return line?["installed ".Length..].Trim();
    }

    /// <inheritdoc />
    public async Task InstallRecorded(string directory, IBundleState state, CancellationToken cancellation = default)
    {
        IReadOnlyList<DebPackage> available = index.Read(directory);
        List<string> todo = [];
        foreach (string package in Names(directory, "top-level.txt"))
        {
            string bundleVersion = available.FirstOrDefault(entry => entry.Package == package)?.Version ?? string.Empty;
            string? installed = await InstalledVersion(package, cancellation);
            if (installed is null || versions.CompareDebian(bundleVersion, installed) > 0)
            {
                todo.Add(package);
            }

            Report(package, installed, bundleVersion);
        }

        foreach (string package in Names(directory, "support.txt"))
        {
            if (await InstalledVersion(package, cancellation) is null)
            {
                todo.Add(package);
            }
        }

        if (todo.Count == 0)
        {
            log.Info("no Ubuntu packages to install");
            return;
        }

        foreach (string package in await InstallLocal(directory, todo, cancellation))
        {
            state.Add(recordFile, package);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAdded(IBundleState state, CancellationToken cancellation = default)
    {
        string running = File.ReadAllText("/proc/sys/kernel/osrelease").Trim();
        string leftoverFile = Path.Combine(Path.GetDirectoryName(state.Directory.TrimEnd(Path.DirectorySeparatorChar))!, leftoverName);
        List<string> candidates = [.. state.Lines(recordFile), .. File.Exists(leftoverFile) ? File.ReadAllLines(leftoverFile) : []];
        candidates = [.. candidates.Where(package => package.Length > 0).Distinct()];
        for (int pass = 0; pass < 4; pass++)
        {
            List<string> present = await Installed(candidates, cancellation);
            if (present.Count == 0)
            {
                break;
            }

            await runner.Run(new() { FileName = "apt-mark", Arguments = ["auto", .. present], Check = false }, cancellation);
            ProcessResult simulation = await runner.Run(
                new() { FileName = "apt-get", Arguments = ["-s", "-o", "APT::AutoRemove::RecommendsImportant=false", "-o", "APT::AutoRemove::SuggestsImportant=false", "autoremove"], Check = false },
                cancellation);
            HashSet<string> removable = RemovePattern().Matches(simulation.Output).Select(match => match.Groups["name"].Value).ToHashSet();
            List<string> remove = present
                .Where(package => (removable.Contains(package) || KernelPattern().IsMatch(package)) && !package.Contains(running, StringComparison.Ordinal))
                .Distinct().Order(StringComparer.Ordinal).ToList();
            if (remove.Count == 0)
            {
                break;
            }

            await runner.Run(new() { FileName = "apt-get", Arguments = ["remove", "-y", "--purge", .. remove], Environment = noninteractive, StreamOutput = true, Check = false }, cancellation);
        }

        List<string> remaining = await Installed(candidates, cancellation);
        if (remaining.Count == 0)
        {
            File.Delete(leftoverFile);
        }
        else
        {
            await File.WriteAllLinesAsync(leftoverFile, remaining, cancellation);
        }
    }

    [GeneratedRegex(@"^Remv (?<name>\S+)", RegexOptions.Multiline)]
    private static partial Regex RemovePattern();

    [GeneratedRegex(@"^linux-.*-[0-9]+\.[0-9]+\.[0-9]+-[0-9]+-[a-z0-9]+$")]
    private static partial Regex KernelPattern();

    private async Task<List<string>> Installed(IEnumerable<string> packages, CancellationToken cancellation)
    {
        List<string> installed = [];
        foreach (string package in packages)
        {
            if (await InstalledVersion(package, cancellation) is not null)
            {
                installed.Add(package);
            }
        }

        return installed;
    }

    private IReadOnlyList<string> Names(string directory, string file)
        => File.Exists(Path.Combine(directory, file)) ? File.ReadAllLines(Path.Combine(directory, file)).Where(line => line.Length > 0).ToList() : [];

    private void Report(string package, string? installed, string bundleVersion)
        => log.Info(versions.CompareDebian(bundleVersion, installed ?? string.Empty) switch
        {
            _ when installed is null => $"{package} {bundleVersion}: not on this computer, installing",
            > 0 => $"{package}: replacing {installed} with the newer {bundleVersion}",
            0 => $"{package} {bundleVersion}: already installed, skipped",
            _ => $"{package}: {installed} is installed and newer than the {bundleVersion} in the bundle, kept",
        });

    private async Task<IReadOnlyList<string>> InstallLocal(string directory, IReadOnlyList<string> packages, CancellationToken cancellation)
    {
        string work = Path.Combine(Directory.Exists("/run") ? "/run" : Path.GetTempPath(), "bundler-apt-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(work, "lists", "partial"));
        Directory.CreateDirectory(Path.Combine(work, "archives", "partial"));
        Directory.CreateDirectory(Path.Combine(work, "none"));
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(work, "repo"), Path.GetFullPath(directory));
            await File.WriteAllTextAsync(Path.Combine(work, "sources.list"), $"deb [trusted=yes] file:{work}/repo ./\n", cancellation);
            string[] options =
            [
                "-o", $"Dir::Etc::sourcelist={work}/sources.list", "-o", $"Dir::Etc::sourceparts={work}/none", "-o", $"Dir::State::lists={work}/lists",
                "-o", $"Dir::Cache::archives={work}/archives", "-o", "Dir::Cache::pkgcache=", "-o", "Dir::Cache::srcpkgcache=",
                "-o", "APT::Sandbox::User=root", "-o", $"Dir::Etc::preferences={work}/none/prefs", "-o", $"Dir::Etc::preferencesparts={work}/none",
            ];

            HashSet<string> before = await InstalledNames(cancellation);
            await runner.Run(new() { FileName = "apt-get", Arguments = [.. options, "-qq", "update"], Environment = noninteractive, StreamOutput = true }, cancellation);
            await runner.Run(
                new() { FileName = "apt-get", Arguments = [.. options, "install", "-y", "--no-install-recommends", "-o", "Dpkg::Options::=--force-confold", .. packages], Environment = noninteractive, StreamOutput = true },
                cancellation);
            return (await InstalledNames(cancellation)).Except(before).Order(StringComparer.Ordinal).ToList();
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    private async Task<HashSet<string>> InstalledNames(CancellationToken cancellation)
        => (await runner.Run(new() { FileName = "dpkg-query", Arguments = ["-W", "-f", "${Package}\n"] }, cancellation)).Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
}
