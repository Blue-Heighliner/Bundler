namespace BlueHeighliner.Bundler;

/// <summary>
/// Mirrors NuGet packages with a real <c>dotnet restore</c> of the latest SDK, so a feed holds exactly what a restore on the
/// target asks for, including transitive dependencies and the runtime packs. Works in an isolated scratch area (its own
/// package folder, nuget.org as the only source) outside any git repository, because some templates write files such as
/// <c>global.json</c> into the root of the repository they run in.
/// </summary>
internal interface INuGetMirror
{
    /// <summary>
    /// Starts with an empty scratch area.
    /// </summary>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the area is ready.</returns>
    Task Begin(CancellationToken cancellation = default);

    /// <summary>
    /// Restores packages (and everything they depend on) for the SDK's own target framework and the runtime packs of
    /// linux-x64 and win-x64, so all of it lands in the scratch package folder.
    /// </summary>
    /// <param name="packages">The packages, each at its wanted version or the latest stable one.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the restore is done.</returns>
    Task Restore(IReadOnlyList<NuGetPackage> packages, CancellationToken cancellation = default);

    /// <summary>
    /// Downloads one package without its dependencies into the scratch package folder.
    /// </summary>
    /// <param name="id">The package id.</param>
    /// <param name="version">The exact version.</param>
    /// <param name="cancellation">Stops the download when cancelled.</param>
    /// <returns>A task that completes when the package is in place.</returns>
    Task FetchExact(string id, string version, CancellationToken cancellation = default);

    /// <summary>
    /// Installs a template pack into the scratch area's own template store.
    /// </summary>
    /// <param name="nupkg">The template pack file.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the pack is installed.</returns>
    Task InstallTemplate(string nupkg, CancellationToken cancellation = default);

    /// <summary>
    /// Creates a project from each template and restores it, which pulls in the packages the templates reference at the
    /// versions the SDK knows.
    /// </summary>
    /// <param name="templates">The template short names; one that does not exist is skipped with a warning.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when all are restored.</returns>
    Task RestoreTemplates(IReadOnlyList<string> templates, CancellationToken cancellation = default);

    /// <summary>
    /// Copies every package of the scratch package folder into a folder as a v3 feed.
    /// </summary>
    /// <param name="destination">The feed folder.</param>
    /// <returns>A task that completes when the packages are copied.</returns>
    Task Collect(string destination);

    /// <summary>Removes the scratch area.</summary>
    void End();
}

/// <inheritdoc />
internal sealed class NuGetMirror(IPaths paths, IMachine machine, IDotNetSdks sdks, IHttpFetcher http, INuGetLayout layout, ILog log) : INuGetMirror
{
    private static readonly string[] runtimes = ["linux-x64", "win-x64"];

    private string Work => Path.Combine(paths.Scratch, "bundler-nuget-work-" + machine.UserName);

    private string Config => Path.Combine(Work, "NuGet.Config");

    /// <inheritdoc />
    public async Task Begin(CancellationToken cancellation = default)
    {
        End();
        foreach (string folder in new[] { "packages", "home", "proj", "run", "dl" })
        {
            Directory.CreateDirectory(Path.Combine(Work, folder));
        }

        await File.WriteAllTextAsync(
            Config,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<configuration>\n  <packageSources>\n    <clear />\n    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" protocolVersion=\"3\" />\n  </packageSources>\n</configuration>\n",
            cancellation);
    }

    /// <inheritdoc />
    public async Task Restore(IReadOnlyList<NuGetPackage> packages, CancellationToken cancellation = default)
    {
        DotNetRelease release = await sdks.Latest(cancellation);
        string references = string.Concat(packages.Select(package => $"    <PackageReference Include=\"{package.Id}\" Version=\"{package.Version ?? "*"}\" />\n"));
        string project = Path.Combine(Work, "proj", "Mirror.csproj");
        await File.WriteAllTextAsync(
            project,
            $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net{release.Channel}</TargetFramework>\n    <RuntimeIdentifiers>{string.Join(';', runtimes)}</RuntimeIdentifiers>\n    <SelfContained>true</SelfContained>\n    <NuGetAudit>false</NuGetAudit>\n    <DisableImplicitNuGetFallbackFolder>true</DisableImplicitNuGetFallbackFolder>\n  </PropertyGroup>\n  <ItemGroup>\n{references}  </ItemGroup>\n</Project>\n",
            cancellation);
        await Dotnet(["restore", project, "--configfile", Config, "--force", "-v", "q"], check: true, cancellation);
    }

    /// <inheritdoc />
    public async Task FetchExact(string id, string version, CancellationToken cancellation = default)
    {
        string lowerId = id.ToLowerInvariant();
        string lowerVersion = version.ToLowerInvariant();
        string file = Path.Combine(Work, "dl", $"{lowerId}.{lowerVersion}.nupkg");
        await http.Download($"https://api.nuget.org/v3-flatcontainer/{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg", file, null, cancellation);
        layout.Place(Path.Combine(Work, "packages"), file);
    }

    /// <inheritdoc />
    public Task InstallTemplate(string nupkg, CancellationToken cancellation = default)
        => Dotnet(["new", "install", nupkg], check: true, cancellation);

    /// <inheritdoc />
    public async Task RestoreTemplates(IReadOnlyList<string> templates, CancellationToken cancellation = default)
    {
        foreach (string template in templates)
        {
            log.Info($"template {template}");
            string folder = Path.Combine(Work, "tpl");
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            Directory.CreateDirectory(folder);
            if (!(await Dotnet(["new", template, "-o", folder, "--no-restore"], check: false, cancellation)).Success)
            {
                log.Warn($"template {template} is not available, skipped");
                continue;
            }

            if (!(await Dotnet(["restore", folder, "--configfile", Config, "-v", "q"], check: false, cancellation)).Success)
            {
                log.Warn($"restore of template {template} failed");
            }
        }
    }

    /// <inheritdoc />
    public Task Collect(string destination)
    {
        string packages = Path.Combine(Work, "packages");
        foreach (string directory in Directory.EnumerateFiles(packages, "*.nupkg", SearchOption.AllDirectories).Select(path => Path.GetDirectoryName(path)!).Distinct())
        {
            string target = Path.Combine(destination, Path.GetRelativePath(packages, directory));
            Directory.CreateDirectory(target);
            foreach (string file in Directory.EnumerateFiles(directory).Where(file => file.EndsWith(".nupkg") || file.EndsWith(".nupkg.sha512") || file.EndsWith(".nuspec")))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void End()
    {
        if (Directory.Exists(Work))
        {
            Directory.Delete(Work, recursive: true);
        }
    }

    private Task<ProcessResult> Dotnet(IReadOnlyList<string> arguments, bool check, CancellationToken cancellation)
        => sdks.Run(
            arguments,
            Path.Combine(Work, "run"),
            new Dictionary<string, string?> { ["NUGET_PACKAGES"] = Path.Combine(Work, "packages"), ["DOTNET_CLI_HOME"] = Path.Combine(Work, "home"), ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(Work, "http") },
            check,
            cancellation);
}
