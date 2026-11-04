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
    /// Builds each bundle into the bundles folder as <c>&lt;name&gt;.bundle</c>, replacing the file that is there. Everything is
    /// downloaded in its latest stable version. A bundle whose file already holds exactly that is left as it is.
    /// </summary>
    /// <param name="names">The bundles; <c>all</c> means every bundle.</param>
    /// <param name="cancellation">Stops the build when cancelled (a model download continues next time).</param>
    /// <returns>A task that completes when all are built.</returns>
    Task Build(IReadOnlyList<string> names, CancellationToken cancellation = default);

    /// <summary>
    /// Installs each bundle from its file in the bundles folder, after the bundles it needs. Needs administrator rights. A
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
    /// Uninstalls the bundles that are installed and then installs all of them again, from their files in the bundles folder.
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
internal sealed class BundleEngine(IBundleCatalog catalog, IBundleFiles files, IBundleArchive archive, IStateStore states, IUsers users, IPaths paths, IMachine machine, ILog log) : IBundleEngine
{
    /// <inheritdoc />
    public IReadOnlyList<BundleStatus> List()
        => catalog.All.Select(Status).ToList();

    /// <inheritdoc />
    public async Task Build(IReadOnlyList<string> names, CancellationToken cancellation = default)
    {
        RequireLinux();
        List<IBundle> selected = [.. names.SelectMany(name => name == "all" ? catalog.All.Select(bundle => bundle.Name) : [name]).Distinct().Select(catalog.Get)];
        foreach (IBundle bundle in selected)
        {
            await BuildOne(bundle, cancellation);
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
        foreach (string name in plan.Where(name => files.Find(name) is null))
        {
            throw new InvalidOperationException($"{name} is not built, so it cannot be installed. Build it on an online computer and put {name}.bundle in {paths.Bundles}.");
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
        string? file = files.Find(bundle.Name);
        return new()
        {
            Name = bundle.Name,
            Description = bundle.Description,
            Target = bundle.Target,
            Requires = bundle.Requires,
            State = states.Get(bundle.Name).IsInstalled ? BundleStanding.Installed : file is null ? BundleStanding.NotBuilt : BundleStanding.Available,
            FileModified = file is null ? null : File.GetLastWriteTime(file),
        };
    }

    private async Task BuildOne(IBundle bundle, CancellationToken cancellation)
    {
        string? existing = files.Find(bundle.Name);
        string staging = Path.Combine(paths.Staging, bundle.Name);
        log.Step($"Building {bundle.Name}: {bundle.Description}");
        IReadOnlyDictionary<string, string> known = existing is null ? new Dictionary<string, string>() : archive.ReadChecksums(existing);
        if (existing is not null && await bundle.IsUnchanged(known, cancellation))
        {
            log.Info($"{bundle.Name} is up to date: {Path.GetFileName(existing)} already holds the latest versions, so it is left as it is");
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
            log.Info($"{bundle.Name} is up to date: {Path.GetFileName(existing)} already holds the latest versions, so it is left as it is");
            return;
        }

        string file = files.PathFor(bundle.Name);
        log.Step($"Packing {Path.GetFileName(file)}");
        await archive.Pack(
            staging,
            file,
            new()
            {
                Name = bundle.Name,
                Built = DateTimeOffset.Now.ToString("O"),
                BuiltOn = BuiltOn(),
                Components = result.Components,
            },
            checksums,
            cancellation);
        Directory.Delete(staging, recursive: true);
        log.Step($"Built {bundle.Name}: {file} ({new FileInfo(file).Length / 1_000_000} MB)");
        log.Info($"Copy it to the bundles folder next to the app on the offline computer, then install {bundle.Name} there. A computer that has {bundle.Name} installed needs it reinstalled to use the new file.");
    }

    private async Task InstallOne(IBundle bundle, CancellationToken cancellation)
    {
        string file = files.Find(bundle.Name)!;
        BundleInfo info = archive.ReadInfo(file) ?? throw new InvalidDataException($"{file} is not a readable bundle file");
        IBundleState state = states.Get(bundle.Name);
        if (state.IsInstalled)
        {
            log.Info($"{bundle.Name} is already installed, skipped. To install this file, reinstall {bundle.Name}.");
            return;
        }

        long size = new FileInfo(file).Length;
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
                throw new InvalidDataException($"{file} is damaged or incomplete ({damaged.Count} files do not match their checksum), copy it again");
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
            throw new PlatformNotSupportedException("Bundler builds and installs bundles on Linux only (Ubuntu 26.04).");
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
