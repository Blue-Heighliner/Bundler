namespace BlueHeighliner.Bundler;

/// <summary>
/// Puts a program that comes as one archive into a folder of its own, <c>/opt/&lt;program&gt;/&lt;version&gt;</c>, and links its commands into
/// <c>/usr/local/bin</c>, so versions sit side by side and an uninstall takes out exactly what was put there.
/// </summary>
internal interface IToolInstaller
{
    /// <summary>
    /// Finds the newest version that is installed in a program's folder.
    /// </summary>
    /// <param name="root">The folder of the program, such as <c>/opt/node</c>.</param>
    /// <returns>The version, or <see langword="null" /> when there is none.</returns>
    string? InstalledVersion(string root);

    /// <summary>
    /// Unpacks an archive into the folder of a version (dropping the folder the archive has at its top) and links commands to files in it.
    /// A command that exists already and is not a link into the program's folder is left alone and reported.
    /// </summary>
    /// <param name="state">The state of the bundle, which records what was put where.</param>
    /// <param name="archive">The archive.</param>
    /// <param name="root">The folder of the program.</param>
    /// <param name="version">The version.</param>
    /// <param name="commands">The links to make: name in <c>/usr/local/bin</c> and path inside the version folder. A command whose file is not in the archive is skipped.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the version is in place.</returns>
    Task Install(IBundleState state, string archive, string root, string version, IReadOnlyDictionary<string, string> commands, CancellationToken cancellation = default);

    /// <summary>
    /// Removes the links and the version folder that <see cref="Install" /> made, and the program's folder when it is empty then.
    /// </summary>
    /// <param name="state">The state of the bundle.</param>
    void Uninstall(IBundleState state);
}

/// <inheritdoc />
internal sealed class ToolInstaller(IProcessRunner runner, IVersions versions, ILog log, string binaries = "/usr/local/bin") : IToolInstaller
{
    /// <inheritdoc />
    public string? InstalledVersion(string root)
        => Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>().Where(name => !name.EndsWith(".partial", StringComparison.Ordinal) && char.IsDigit(name[0]))
                .OrderBy(name => name, Comparer<string>.Create(versions.Compare)).LastOrDefault()
            : null;

    /// <inheritdoc />
    public async Task Install(IBundleState state, string archive, string root, string version, IReadOnlyDictionary<string, string> commands, CancellationToken cancellation = default)
    {
        string folder = Path.Combine(root, version);
        string partial = folder + ".partial";
        Directory.CreateDirectory(root);
        if (Directory.Exists(partial))
        {
            Directory.Delete(partial, recursive: true);
        }

        Directory.CreateDirectory(partial);
        await runner.Run(new() { FileName = "tar", Arguments = ["-xf", archive, "-C", partial, "--strip-components=1", "--no-same-owner"] }, cancellation);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.Move(partial, folder);
        state.Write("tool-folder", folder);
        state.MarkOwned("tool");
        log.Info($"unpacked into {folder}");

        Directory.CreateDirectory(binaries);
        foreach ((string name, string inside) in commands)
        {
            if (!File.Exists(Path.Combine(folder, inside)))
            {
                log.Info($"{name} is not in this version, so no command {name} is linked");
                continue;
            }

            string link = Path.Combine(binaries, name);
            string? existing = new FileInfo(link).LinkTarget;
            if ((File.Exists(link) || existing is not null) && !(existing is not null && existing.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            {
                log.Warn($"{link} is already there and is not a link into {root}, so it is left alone and {name} stays what it was");
                continue;
            }

            File.DeleteIfPresent(link);
            File.CreateSymbolicLink(link, Path.Combine(folder, inside));
            state.Add("links", link);
        }
    }

    /// <inheritdoc />
    public void Uninstall(IBundleState state)
    {
        string? folder = state.Read("tool-folder")?.Trim();
        foreach (string link in state.Lines("links"))
        {
            string? target = new FileInfo(link).LinkTarget;
            if (target is not null && folder is not null && target.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                File.Delete(link);
            }
        }

        if (!state.IsOwned("tool") || folder is null || !Directory.Exists(folder))
        {
            return;
        }

        Directory.Delete(folder, recursive: true);
        string root = Path.GetDirectoryName(folder)!;
        if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
        {
            Directory.Delete(root);
        }
    }
}
