namespace BlueHeighliner.Bundler;

/// <summary>
/// The offline NuGet feed on the computer, <c>/opt/bundler/nuget-feed</c>, and the dotnet templates kept for each user.
/// Packages are only ever added: older versions stay.
/// </summary>
internal interface INuGetFeed
{
    /// <summary>Gets the feed folder.</summary>
    string Directory { get; }

    /// <summary>Gets a value indicating whether the feed exists (the dotnet bundle creates it).</summary>
    bool Exists { get; }

    /// <summary>
    /// Copies the package versions the feed does not have yet and records each one it created in the bundle's state, so an
    /// uninstall removes only what this bundle added. A version the feed has already (from another bundle) is recorded as
    /// shared, so that the other bundle's uninstall does not take it away from this one.
    /// </summary>
    /// <param name="source">The folder with the bundle's packages.</param>
    /// <param name="state">The bundle's installed-state records.</param>
    /// <returns>How many package versions were added and how many the bundle holds.</returns>
    (int Added, int Total) Add(string source, IBundleState state);

    /// <summary>
    /// Removes the package versions this bundle added. One that another installed bundle shares is handed over to that bundle
    /// and stays until it is uninstalled too.
    /// </summary>
    /// <param name="state">The bundle's installed-state records.</param>
    void Remove(IBundleState state);

    /// <summary>
    /// Writes a <c>NuGet.Config</c> that uses only the offline feed.
    /// </summary>
    /// <param name="path">The file to write.</param>
    void WriteConfig(string path);

    /// <summary>
    /// Finds the highest version of a package in a folder laid out as a feed.
    /// </summary>
    /// <param name="root">The feed folder.</param>
    /// <param name="id">The package id.</param>
    /// <returns>The version, or <see langword="null" /> when the package is not there.</returns>
    string? HighestVersion(string root, string id);

    /// <summary>
    /// Finds the version of a template pack a user has installed.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <param name="id">The template pack id.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The version, or <see langword="null" /> when the user does not have it.</returns>
    Task<string?> TemplateVersion(string user, string id, CancellationToken cancellation = default);

    /// <summary>
    /// Installs a template pack for a user, replacing an older version of it.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <param name="nupkg">The template pack file.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns><see langword="true" /> when it was installed.</returns>
    Task<bool> InstallTemplate(string user, string nupkg, CancellationToken cancellation = default);

    /// <summary>
    /// Removes a template pack from a user.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <param name="id">The template pack id.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the pack is removed.</returns>
    Task UninstallTemplate(string user, string id, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class NuGetFeed(IPaths paths, IUsers users, IVersions versions) : INuGetFeed
{
    private static readonly string recordFile = "nuget-added";

    private static readonly string sharedFile = "nuget-shared";

    /// <inheritdoc />
    string INuGetFeed.Directory => paths.NuGetFeed;

    /// <inheritdoc />
    public bool Exists => System.IO.Directory.Exists(paths.NuGetFeed);

    /// <inheritdoc />
    public (int Added, int Total) Add(string source, IBundleState state)
    {
        System.IO.Directory.CreateDirectory(paths.NuGetFeed);
        int added = 0;
        string[] packages = System.IO.Directory.EnumerateFiles(source, "*.nupkg", SearchOption.AllDirectories).Select(path => Path.GetDirectoryName(path)!).Distinct().ToArray();
        foreach (string directory in packages)
        {
            string relative = Path.GetRelativePath(source, directory).Replace('\\', '/');
            string target = Path.Combine(paths.NuGetFeed, relative);
            if (System.IO.Directory.Exists(target) && System.IO.Directory.EnumerateFiles(target, "*.nupkg").Any())
            {
                if (!state.Lines(recordFile).Contains(relative))
                {
                    state.Add(sharedFile, relative);
                }

                continue;
            }

            System.IO.Directory.CreateDirectory(target);
            foreach (string file in System.IO.Directory.EnumerateFiles(directory))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            }

            state.Add(recordFile, relative);
            added++;
        }

        MakeReadable(paths.NuGetFeed);
        return (added, packages.Length);
    }

    /// <inheritdoc />
    public void Remove(IBundleState state)
    {
        foreach (string relative in state.Lines(recordFile))
        {
            if (HandOver(relative, state))
            {
                continue;
            }

            string target = Path.Combine(paths.NuGetFeed, relative);
            if (System.IO.Directory.Exists(target))
            {
                System.IO.Directory.Delete(target, recursive: true);
            }

            string? parent = Path.GetDirectoryName(target);
            if (parent is not null && System.IO.Directory.Exists(parent) && !System.IO.Directory.EnumerateFileSystemEntries(parent).Any())
            {
                System.IO.Directory.Delete(parent);
            }
        }
    }

    /// <inheritdoc />
    public void WriteConfig(string path)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!-- bundler: restore uses only the offline feed on this computer. Remove this file or add sources to change that. -->\n<configuration>\n  <packageSources>\n    <clear />\n    <add key=\"bundler\" value=\"{paths.NuGetFeed}\" />\n  </packageSources>\n</configuration>\n");
    }

    /// <inheritdoc />
    public string? HighestVersion(string root, string id)
    {
        string folder = Path.Combine(root, id.ToLowerInvariant());
        return System.IO.Directory.Exists(folder)
            ? System.IO.Directory.EnumerateDirectories(folder).Select(Path.GetFileName).OrderBy(version => version, Comparer<string?>.Create((a, b) => versions.Compare(a!, b!))).LastOrDefault()
            : null;
    }

    /// <inheritdoc />
    public async Task<string?> TemplateVersion(string user, string id, CancellationToken cancellation = default)
    {
        ProcessResult result = await users.RunAs(user, false, "dotnet", ["new", "uninstall"], cancellation);
        bool found = false;
        foreach (string raw in result.Output.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
            }
            else if (found && line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
            {
                return line["Version:".Length..].Trim();
            }
            else if (found && line.Length == 0)
            {
                found = false;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<bool> InstallTemplate(string user, string nupkg, CancellationToken cancellation = default)
    {
        // The state folder is under root's home, which the user cannot read, so the user installs the pack from a copy outside it.
        DirectoryInfo staged = System.IO.Directory.CreateTempSubdirectory("bundler-template-");
        try
        {
            string copy = Path.Combine(staged.FullName, Path.GetFileName(nupkg));
            File.Copy(nupkg, copy);
            File.SetUnixFileMode(staged.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            return (await users.RunAs(user, false, "dotnet", ["new", "install", copy, "--force"], cancellation)).Success;
        }
        finally
        {
            staged.Delete(recursive: true);
        }
    }

    /// <inheritdoc />
    public async Task UninstallTemplate(string user, string id, CancellationToken cancellation = default)
        => await users.RunAs(user, false, "dotnet", ["new", "uninstall", id], cancellation);

    private bool HandOver(string relative, IBundleState state)
    {
        string own = Path.GetFullPath(state.Directory);
        string? root = Path.GetDirectoryName(own.TrimEnd(Path.DirectorySeparatorChar));
        if (root is null || !System.IO.Directory.Exists(root))
        {
            return false;
        }

        foreach (string other in System.IO.Directory.EnumerateDirectories(root).Where(other => Path.GetFullPath(other) != own).Order(StringComparer.Ordinal))
        {
            string shared = Path.Combine(other, sharedFile);
            if (File.Exists(shared) && File.ReadAllLines(shared).Contains(relative))
            {
                File.AppendAllLines(Path.Combine(other, recordFile), [relative]);
                File.WriteAllLines(shared, File.ReadAllLines(shared).Where(line => line != relative));
                return true;
            }
        }

        return false;
    }

    private void MakeReadable(string directory)
    {
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        foreach (string entry in System.IO.Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
        {
            File.SetUnixFileMode(entry, System.IO.Directory.Exists(entry)
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }
}
