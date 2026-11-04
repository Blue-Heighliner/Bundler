namespace BlueHeighliner.Bundler;

/// <summary>
/// Adds a bundle's NuGet packages to the offline feed and installs its dotnet template packs for each user.
/// </summary>
internal interface INuGetBundleInstaller
{
    /// <summary>
    /// Adds the packages to the feed (older versions stay, versions already there are not touched) and installs each template
    /// pack for every user who does not have it or has an older one.
    /// </summary>
    /// <param name="spec">What the bundle mirrors.</param>
    /// <param name="context">What the install needs.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the packages and templates are in place.</returns>
    /// <exception cref="InvalidOperationException">The offline feed does not exist yet.</exception>
    Task Install(NuGetBundleSpec spec, InstallContext context, CancellationToken cancellation = default);

    /// <summary>
    /// Removes the template packs and the package versions this bundle added.
    /// </summary>
    /// <param name="context">What the uninstall needs.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when they are removed.</returns>
    Task Uninstall(UninstallContext context, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class NuGetBundleInstaller(INuGetFeed feed, INuGetBundleBuilder builder, IDecisions decisions, IPaths paths, ILog log) : INuGetBundleInstaller
{
    /// <inheritdoc />
    public async Task Install(NuGetBundleSpec spec, InstallContext context, CancellationToken cancellation = default)
    {
        if (!feed.Exists)
        {
            throw new InvalidOperationException("the offline NuGet feed is missing (install the dev bundle first)");
        }

        log.Step("Adding packages to the offline NuGet feed");
        (int added, int total) = feed.Add(Path.Combine(context.Payload, "nuget"), context.State);
        log.Info($"{added} new package versions added, {total - added} were already in the feed (older versions are kept)");
        string licenses = Path.Combine(context.Payload, "nuget-licenses.tsv");
        if (File.Exists(licenses))
        {
            string table = Path.Combine(paths.NuGetFeed, "LICENSES.tsv");
            string[] known = File.Exists(table) ? File.ReadAllLines(table) : [];
            File.AppendAllLines(table, File.ReadAllLines(licenses).Where(line => line.Length > 0 && !known.Contains(line)));
        }

        if (spec.TemplatePackages.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(context.State.Directory, "templates"));
        foreach (string file in Directory.EnumerateFiles(Path.Combine(context.Payload, "templates"), "*.nupkg"))
        {
            File.Copy(file, Path.Combine(context.State.Directory, "templates", Path.GetFileName(file)), overwrite: true);
        }

        foreach (string id in spec.TemplatePackages)
        {
            string version = builder.TemplateVersion(Path.Combine(context.Payload, "templates"), id) ?? string.Empty;
            string file = Path.Combine(context.State.Directory, "templates", $"{id}.{version}.nupkg");
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            foreach (string user in context.Users)
            {
                string? have = await feed.TemplateVersion(user, id, cancellation);
                if (!decisions.Install($"dotnet templates {id} for {user}", have, version))
                {
                    continue;
                }

                if (await feed.InstallTemplate(user, file, cancellation))
                {
                    context.State.Add("templates-owned", $"{user} {id}");
                }
                else
                {
                    log.Warn($"could not install the template pack {id} for {user}");
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task Uninstall(UninstallContext context, CancellationToken cancellation = default)
    {
        foreach (string line in context.State.Lines("templates-owned"))
        {
            string[] parts = line.Split(' ', 2);
            if (parts.Length == 2)
            {
                await feed.UninstallTemplate(parts[0], parts[1], cancellation);
            }
        }

        feed.Remove(context.State);
    }
}
