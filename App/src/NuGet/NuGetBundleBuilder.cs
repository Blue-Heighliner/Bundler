namespace BlueHeighliner.Bundler;

/// <summary>
/// Mirrors the NuGet packages of a bundle into a folder as a feed, together with their dependencies and the packages the
/// SDK's templates reference.
/// </summary>
internal interface INuGetBundleBuilder
{
    /// <summary>
    /// Mirrors the packages. The folder gets <c>nuget/</c> (the feed), <c>templates/</c> (template packs, when there are any) and
    /// <c>nuget-licenses.tsv</c>.
    /// </summary>
    /// <param name="spec">What to mirror.</param>
    /// <param name="directory">The bundle's staging folder.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>What was mirrored.</returns>
    Task<NuGetBuildResult> Build(NuGetBundleSpec spec, string directory, CancellationToken cancellation = default);

    /// <summary>
    /// Finds the version of the template pack <c>Id.Version.nupkg</c> in a folder.
    /// </summary>
    /// <param name="directory">The folder.</param>
    /// <param name="id">The pack id.</param>
    /// <returns>The version, or <see langword="null" /> when the pack is not there.</returns>
    string? TemplateVersion(string directory, string id);
}

/// <inheritdoc />
internal sealed class NuGetBundleBuilder(
    IResources resources,
    INuGetPackageList list,
    IReleaseSources sources,
    INuGetMirror mirror,
    IHttpFetcher http,
    ITemplatePatcher patcher,
    INuGetLicenses licenses,
    INuGetFeed feed,
    IDotNetSdks sdks,
    ILog log) : INuGetBundleBuilder
{
    /// <inheritdoc />
    public async Task<NuGetBuildResult> Build(NuGetBundleSpec spec, string directory, CancellationToken cancellation = default)
    {
        await sdks.Latest(cancellation);
        log.Step("Looking up the packages");
        Dictionary<string, string> latest = [];
        List<NuGetPackage> wanted = [];
        foreach (NuGetPackage package in list.Parse(resources.Text(spec.PackageList)))
        {
            string? version = await sources.LatestNuGet(package.Id, package.Version is string range && range.Contains('*') ? range : null, cancellation);
            if (version is null)
            {
                if (package.Optional)
                {
                    log.Warn($"{package.Id} has no stable release on nuget.org, skipped");
                    continue;
                }

                throw new InvalidOperationException($"package {package.Id} has no stable release on nuget.org (check {spec.PackageList})");
            }

            latest[package.Id] = version;
            wanted.Add(package);
        }

        await mirror.Begin(cancellation);
        try
        {
            List<NuGetPackage> restore = wanted.Where(package => !package.Alone && !list.IsPlatformPack(package.Id)).ToList();
            log.Step($"Downloading {restore.Count} packages and everything they depend on");
            await mirror.Restore(restore, cancellation);
            foreach (NuGetPackage package in wanted)
            {
                await mirror.FetchExact(package.Id, package.Version is string exact && !exact.Contains('*') ? exact : latest[package.Id], cancellation);
            }

            await BuildTemplates(spec, directory, cancellation);
            if (spec.WarmTemplates.Count > 0)
            {
                log.Step("Downloading the packages the templates use");
                await mirror.RestoreTemplates(spec.WarmTemplates, cancellation);
            }

            await mirror.Collect(Path.Combine(directory, "nuget"));
        }
        finally
        {
            mirror.End();
        }

        IReadOnlyList<LicenseRow> rows = licenses.Report(Path.Combine(directory, "nuget"));
        await File.WriteAllTextAsync(Path.Combine(directory, "nuget-licenses.tsv"), licenses.Format(rows), cancellation);
        if (rows.Any(row => row.NeedsReview))
        {
            log.Warn("these packages have a license that is not on the permissive list, read their terms before using the bundle:");
            foreach (LicenseRow row in rows.Where(row => row.NeedsReview))
            {
                log.Warn($"    {row.Id} {row.Version}: {row.License}");
            }
        }

        string feedFolder = Path.Combine(directory, "nuget");
        List<string> components = wanted.Select(package => $"NuGet {package.Id} {feed.HighestVersion(feedFolder, package.Id)}").ToList();
        components.AddRange(spec.TemplatePackages.Select(id => $"dotnet template pack {id} {TemplateVersion(Path.Combine(directory, "templates"), id)}"));
        components.Add($"{Directory.EnumerateFiles(feedFolder, "*.nupkg", SearchOption.AllDirectories).Count()} packages in total, with dependencies");
        return new() { Components = components };
    }

    /// <inheritdoc />
    public string? TemplateVersion(string directory, string id)
    {
        string? file = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, $"{id}.*.nupkg").Select(Path.GetFileNameWithoutExtension).FirstOrDefault(name => char.IsDigit(name![id.Length + 1])) : null;
        return file?[(id.Length + 1)..];
    }

    private async Task BuildTemplates(NuGetBundleSpec spec, string directory, CancellationToken cancellation)
    {
        if (spec.TemplatePackages.Count == 0)
        {
            return;
        }

        string folder = Path.Combine(directory, "templates");
        Directory.CreateDirectory(folder);
        foreach (string id in spec.TemplatePackages)
        {
            string version = await sources.LatestNuGet(id, null, cancellation) ?? throw new InvalidOperationException($"template pack {id} has no stable release");
            string file = Path.Combine(folder, $"{id}.{version}.nupkg");
            string lower = id.ToLowerInvariant();
            await http.Download($"https://api.nuget.org/v3-flatcontainer/{lower}/{version.ToLowerInvariant()}/{lower}.{version.ToLowerInvariant()}.nupkg", file, null, cancellation);
            if (spec.TemplateStrip.Count > 0)
            {
                log.Info($"removing the references to {string.Join(", ", spec.TemplateStrip)} from the {id} templates");
                patcher.Patch(file, spec.TemplateStrip, spec.TemplateStripCalls);
            }

            await mirror.InstallTemplate(file, cancellation);
        }
    }
}
