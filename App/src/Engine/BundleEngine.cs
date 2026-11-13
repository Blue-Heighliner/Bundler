namespace BlueHeighliner.Bundler;

/// <summary>
/// Builds, installs and uninstalls bundles, and tells where each stands.
/// </summary>
internal interface IBundleEngine
{
    /// <summary>
    /// Lists every bundle with its state.
    /// </summary>
    /// <returns>One row per bundle in list order.</returns>
    IReadOnlyList<BundleStatus> List();

    /// <summary>
    /// Builds each bundle into the Bundles folder as <c>&lt;name&gt;.bundle</c>, replacing the file that is there. Everything is
    /// downloaded in its latest stable version. A bundle whose file already holds exactly that is left as it is, unless its files
    /// are not laid out the way the size limit asks, in which case they are only cut up again (nothing is downloaded).
    /// </summary>
    /// <param name="names">The bundles; <c>all</c> means every bundle.</param>
    /// <param name="partLimit">The most bytes one file may hold, or <see langword="null" /> for no limit. A bundle bigger than the limit is written as parts <c>&lt;name&gt;.bundle.001</c>, <c>.002</c> and so on of exactly the limit each, the last one holding the rest. At least 1 MB.</param>
    /// <param name="cancellation">Stops the build when cancelled (a model download continues next time).</param>
    /// <returns>A task that completes when all are built.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The limit is below 1 MB.</exception>
    Task Build(IReadOnlyList<string> names, long? partLimit = null, CancellationToken cancellation = default);

    /// <summary>
    /// Installs each bundle from its file in the Bundles folder, after the bundles it needs. Needs administrator rights. A
    /// bundle that is installed already is skipped: to install a different file of it, reinstall it.
    /// </summary>
    /// <param name="names">The bundles.</param>
    /// <param name="options">How to run.</param>
    /// <param name="cancellation">Stops the install when cancelled.</param>
    /// <returns>A task that completes when all are installed.</returns>
    Task Install(IReadOnlyList<string> names, RunOptions options, CancellationToken cancellation = default);

    /// <summary>
    /// Removes what the bundles installed, dependents first. Needs administrator rights. Refuses while another installed
    /// bundle needs one of them.
    /// </summary>
    /// <param name="names">The bundles.</param>
    /// <param name="options">How to run.</param>
    /// <param name="cancellation">Stops the uninstall when cancelled.</param>
    /// <returns>A task that completes when all are removed.</returns>
    Task Uninstall(IReadOnlyList<string> names, RunOptions options, CancellationToken cancellation = default);

    /// <summary>
    /// Uninstalls the bundles that are installed and then installs all of them again, from their files in the Bundles folder.
    /// Needs administrator rights. Everything is checked before anything is removed: every file must be there and no other
    /// installed bundle may need one that is removed. Asks once for all.
    /// </summary>
    /// <param name="names">The bundles.</param>
    /// <param name="options">How to run.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when all are installed again.</returns>
    Task Reinstall(IReadOnlyList<string> names, RunOptions options, CancellationToken cancellation = default);

    /// <summary>
    /// Works out what an install of some bundles installs: those bundles and the missing bundles they need, needs first.
    /// </summary>
    /// <param name="names">The bundles asked for.</param>
    /// <returns>The bundles to install in order.</returns>
    IReadOnlyList<string> InstallPlan(IReadOnlyList<string> names);
}

/// <inheritdoc />
internal sealed class BundleEngine(IBundleCatalog catalog, IBundleFiles files, IBundleArchive archive, IStateStore states, IUsers users, IPaths paths, IMachine machine, IByteSizes sizes, ILog log) : IBundleEngine
{
    private readonly long minimumPart = 1_000_000;

    /// <inheritdoc />
    public IReadOnlyList<BundleStatus> List()
        => catalog.All.Select(Status).ToList();

    /// <inheritdoc />
    public async Task Build(IReadOnlyList<string> names, long? partLimit = null, CancellationToken cancellation = default)
    {
        if (partLimit < minimumPart)
        {
            throw new ArgumentOutOfRangeException(nameof(partLimit), $"the size limit has to be at least {minimumPart / 1_000_000} MB");
        }

        RequireLinux();
        List<IBundle> selected = [.. names.SelectMany(name => name == "all" ? catalog.All.Select(bundle => bundle.Name) : [name]).Distinct().Select(catalog.Get)];
        foreach (IBundle bundle in selected)
        {
            await BuildOne(bundle, partLimit, cancellation);
        }
    }

    /// <inheritdoc />
    public async Task Install(IReadOnlyList<string> names, RunOptions options, CancellationToken cancellation = default)
    {
        RequireLinux();
        RequirePrivileged();
        IReadOnlyList<string> plan = InstallPlan(names);
        RequireFiles(plan);

        if (plan.Count > names.Count)
        {
            log.Info($"{string.Join(", ", names)} needs {string.Join(", ", plan.Except(names))} first, installing in this order: {string.Join(", ", plan)}");
        }

        await Confirm(options, $"Install {string.Join(", ", plan)} on this computer?");
        foreach (string name in plan)
        {
            await InstallOne(catalog.Get(name), cancellation);
        }
    }

    /// <inheritdoc />
    public async Task Uninstall(IReadOnlyList<string> names, RunOptions options, CancellationToken cancellation = default)
    {
        RequireLinux();
        RequirePrivileged();
        List<IBundle> selected = Selection(names);
        foreach (IBundle bundle in selected.Where(bundle => !states.Get(bundle.Name).IsInstalled && !Directory.Exists(states.Get(bundle.Name).Directory)))
        {
            throw new InvalidOperationException($"{bundle.Name} is not installed");
        }

        CheckDependents(selected);
        await Confirm(options, $"Uninstall {string.Join(", ", selected.Select(bundle => bundle.Name))} from this computer?");
        await RemoveAll(selected, cancellation);
    }

    /// <inheritdoc />
    public async Task Reinstall(IReadOnlyList<string> names, RunOptions options, CancellationToken cancellation = default)
    {
        RequireLinux();
        RequirePrivileged();
        List<IBundle> selected = Selection(names);
        List<IBundle> installed = selected.Where(bundle => states.Get(bundle.Name).IsInstalled || Directory.Exists(states.Get(bundle.Name).Directory)).ToList();
        IReadOnlyList<string> plan = InstallPlan(names);
        RequireFiles(plan);
        CheckDependents(installed);
        await Confirm(options, $"Reinstall {string.Join(", ", selected.Select(bundle => bundle.Name))}? What is installed is removed first.");
        await RemoveAll(installed, cancellation);
        foreach (string name in plan)
        {
            await InstallOne(catalog.Get(name), cancellation);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> InstallPlan(IReadOnlyList<string> names)
    {
        List<string> plan = [];
        foreach (string name in names)
        {
            Plan(catalog.Get(name), plan);
        }

        return plan;
    }

    private List<IBundle> Selection(IReadOnlyList<string> names)
        => names.Select(catalog.Get).Distinct().OrderByDescending(bundle => bundle.Order).ToList();

    private void RequireFiles(IReadOnlyList<string> plan)
    {
        foreach (string name in plan)
        {
            BundleFile file = files.Find(name) ?? throw new InvalidOperationException($"{name} is not built, so it cannot be installed. Build it on an online computer and put {name}.bundle (or all of its parts, {name}.bundle.001 and so on) in {paths.Bundles}.");
            if (file.Problem is not null)
            {
                throw new InvalidOperationException($"{name} cannot be installed: {file.Problem}. Put all of its parts in {paths.Bundles}.");
            }
        }
    }

    private void CheckDependents(List<IBundle> selected)
    {
        foreach (IBundle bundle in selected)
        {
            string[] dependents = catalog.All.Where(other => other.Requires.Contains(bundle.Name) && states.Get(other.Name).IsInstalled && !selected.Contains(other)).Select(other => other.Name).ToArray();
            if (dependents.Length > 0)
            {
                throw new InvalidOperationException($"{string.Join(", ", dependents)} {(dependents.Length == 1 ? "needs" : "need")} {bundle.Name}. Uninstall {string.Join(", ", dependents)} first.");
            }
        }
    }

    private async Task RemoveAll(List<IBundle> selected, CancellationToken cancellation)
    {
        foreach (IBundle bundle in selected)
        {
            log.Step($"Uninstalling {bundle.Name}");
            IBundleState state = states.Get(bundle.Name);
            IReadOnlyList<string> accounts = users.Normal().Concat(state.Users).Distinct().ToList();
            await bundle.Uninstall(new() { State = state, Users = accounts }, cancellation);
            state.End();
            log.Step($"Uninstalled {bundle.Name}");
        }
    }

    private void Plan(IBundle bundle, List<string> plan)
    {
        foreach (string required in bundle.Requires.Where(required => !states.Get(required).IsInstalled))
        {
            Plan(catalog.Get(required), plan);
        }

        if (!plan.Contains(bundle.Name))
        {
            plan.Add(bundle.Name);
        }
    }

    private BundleStatus Status(IBundle bundle)
    {
        BundleFile? file = files.Find(bundle.Name);
        return new()
        {
            Name = bundle.Name,
            Description = bundle.Description,
            Target = bundle.Target,
            Requires = bundle.Requires,
            State = states.Get(bundle.Name).IsInstalled ? BundleStanding.Installed : file is null ? BundleStanding.NotBuilt : BundleStanding.Available,
            FileModified = file?.Modified,
            FileCount = file?.Paths.Count ?? 0,
            FileProblem = file?.Problem,
        };
    }

    private async Task BuildOne(IBundle bundle, long? partLimit, CancellationToken cancellation)
    {
        BundleFile? existing = files.Find(bundle.Name);
        if (existing?.Problem is not null)
        {
            log.Warn($"the files of {bundle.Name} in the Bundles folder cannot be used ({existing.Problem}), so it is built again");
            existing = null;
        }

        string staging = Path.Combine(paths.Staging, bundle.Name);
        log.Step($"Building {bundle.Name}: {bundle.Description}");
        IReadOnlyDictionary<string, string> known = existing is null ? new Dictionary<string, string>() : archive.ReadChecksums(existing);
        if (existing is not null && await bundle.IsUnchanged(known, cancellation))
        {
            await KeepOrCutUp(bundle, existing, partLimit, cancellation);
            return;
        }

        Directory.CreateDirectory(staging);
        foreach (string entry in Directory.EnumerateFileSystemEntries(staging).Where(entry => !bundle.Resumable.Contains(Path.GetFileName(entry))))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }

        BuildResult result = await bundle.Build(staging, cancellation);
        log.Step("Checking whether the content changed");
        IReadOnlyDictionary<string, string> checksums = await archive.ComputeChecksums(staging, cancellation);
        if (existing is not null && checksums.Count == known.Count && checksums.All(pair => known.TryGetValue(pair.Key, out string? other) && other == pair.Value))
        {
            Directory.Delete(staging, recursive: true);
            await KeepOrCutUp(bundle, existing, partLimit, cancellation);
            return;
        }

        log.Step($"Packing {bundle.Name}.bundle{(partLimit is long limit ? $" (files of at most {sizes.Format(limit)})" : string.Empty)}");
        BundleInfo info = new()
        {
            Name = bundle.Name,
            Built = DateTimeOffset.Now.ToString("O"),
            BuiltOn = BuiltOn(),
            Components = result.Components,
        };
        BundleFile written = await files.Write(bundle.Name, partLimit, output => archive.Pack(staging, output, info, checksums, cancellation), cancellation);
        Directory.Delete(staging, recursive: true);
        Report(written);
    }

    private async Task KeepOrCutUp(IBundle bundle, BundleFile existing, long? partLimit, CancellationToken cancellation)
    {
        if (existing.Matches(partLimit))
        {
            log.Info($"{bundle.Name} is up to date: {Describe(existing)} already holds the latest versions, so it is left as it is");
            return;
        }

        long free = new DriveInfo(paths.Bundles).AvailableFreeSpace;
        if (free < existing.Length + 100_000_000)
        {
            throw new InvalidOperationException($"{bundle.Name} is up to date but its files are not laid out for the size limit, and there is not enough free space in {paths.Bundles} to cut it up again: {sizes.Format(free)} free, {sizes.Format(existing.Length + 100_000_000)} needed");
        }

        log.Step($"{bundle.Name} is up to date, cutting its files up again for the size limit (nothing is downloaded)");
        BundleFile written = await files.Write(
            bundle.Name,
            partLimit,
            async output =>
            {
                await using Stream input = files.OpenRead(existing);
                await input.CopyToAsync(output, 1 << 20, cancellation);
            },
            cancellation);
        Report(written);
    }

    private void Report(BundleFile written)
    {
        log.Step($"Built {written.Name}: {Describe(written)}");
        log.Info($"Copy {(written.IsSplit ? "all its parts" : "it")} to the Bundles folder next to the app on the offline computer, then install {written.Name} there. A computer that has {written.Name} installed needs it reinstalled to use the new file{(written.IsSplit ? "s" : string.Empty)}.");
    }

    private string Describe(BundleFile file)
        => file.IsSplit ? $"{file.Paths.Count} parts, {Path.GetFileName(file.Paths[0])} to {Path.GetFileName(file.Paths[^1])} ({sizes.Format(file.Length)})" : $"{Path.GetFileName(file.Paths[0])} ({sizes.Format(file.Length)})";

    private async Task InstallOne(IBundle bundle, CancellationToken cancellation)
    {
        BundleFile file = files.Find(bundle.Name)!;
        BundleInfo info = archive.ReadInfo(file) ?? throw new InvalidDataException($"{Describe(file)} is not a readable bundle file");
        IBundleState state = states.Get(bundle.Name);
        if (state.IsInstalled)
        {
            log.Info($"{bundle.Name} is already installed, skipped. To install this file, reinstall {bundle.Name}.");
            return;
        }

        if (machine.SystemName is string system && !string.Equals(system, bundle.Target, StringComparison.OrdinalIgnoreCase))
        {
            log.Warn($"{bundle.Name} is made for {bundle.Target} but this computer runs {system}: its Ubuntu packages may not fit, and the install may fail.");
        }

        long size = file.Length;
        long free = new DriveInfo(paths.Unpack).AvailableFreeSpace;
        if (free < size + 500_000_000)
        {
            throw new InvalidOperationException($"not enough free space in {paths.Unpack} to unpack {bundle.Name}: {free / 1_000_000} MB free, {(size + 500_000_000) / 1_000_000} MB needed.");
        }

        string work = Path.Combine(paths.Unpack, "bundler." + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            log.Step($"Unpacking {bundle.Name} ({size / 1_000_000} MB)");
            await archive.Extract(file, work, cancellation);
            log.Step($"Checking the files of {bundle.Name}");
            IReadOnlyList<string> damaged = await archive.Verify(work, cancellation);
            if (damaged.Count > 0)
            {
                throw new InvalidDataException($"{Describe(file)} is damaged or incomplete ({damaged.Count} files do not match their checksum), copy it again");
            }

            log.Step($"Installing {bundle.Name}");
            IReadOnlyList<string> accounts = users.Normal();
            await bundle.Install(new() { Payload = work, State = state, Users = accounts, Info = info }, cancellation);
            state.Begin(accounts);
            log.Step($"Installed {bundle.Name}");
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, recursive: true);
            }
        }
    }

    private async Task Confirm(RunOptions options, string question)
    {
        if (options.Confirm is not null && !await options.Confirm(question))
        {
            throw new OperationCanceledException("cancelled");
        }
    }

    private void RequireLinux()
    {
        if (!machine.IsLinux)
        {
            throw new PlatformNotSupportedException("Bundler builds and installs bundles on Linux only (Ubuntu 24.04).");
        }
    }

    private void RequirePrivileged()
    {
        if (!machine.IsPrivileged)
        {
            throw new UnauthorizedAccessException("installing and uninstalling need administrator rights");
        }
    }

    private string BuiltOn()
    {
        string? line = File.Exists("/etc/os-release") ? File.ReadLines("/etc/os-release").FirstOrDefault(entry => entry.StartsWith("PRETTY_NAME=", StringComparison.Ordinal)) : null;
        return line?["PRETTY_NAME=".Length..].Trim('"') ?? "unknown";
    }
}
