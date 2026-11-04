namespace BlueHeighliner.Bundler;

/// <summary>
/// The .NET SDKs: the latest stable one that bundles ship, a private copy of it used while building to mirror NuGet packages,
/// and the one installed on the computer.
/// </summary>
internal interface IDotNetSdks
{
    /// <summary>
    /// Finds the latest stable SDK release, looking it up once.
    /// </summary>
    /// <param name="cancellation">Stops the lookup when cancelled.</param>
    /// <returns>The release.</returns>
    Task<DotNetRelease> Latest(CancellationToken cancellation = default);

    /// <summary>
    /// Downloads the latest SDK tarball into the build cache, checking its checksum.
    /// </summary>
    /// <param name="cancellation">Stops the download when cancelled.</param>
    /// <returns>The path of the tarball.</returns>
    Task<string> Tarball(CancellationToken cancellation = default);

    /// <summary>
    /// Runs the private copy of the latest SDK, unpacking it into the build cache first when needed. Telemetry and first-run
    /// messages are off.
    /// </summary>
    /// <param name="arguments">The <c>dotnet</c> arguments.</param>
    /// <param name="workingDirectory">The folder to run in.</param>
    /// <param name="environment">More environment variables.</param>
    /// <param name="check">Whether a non-zero exit code throws.</param>
    /// <param name="cancellation">Stops the program when cancelled.</param>
    /// <returns>The result.</returns>
    Task<ProcessResult> Run(IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string?>? environment = null, bool check = true, CancellationToken cancellation = default);

    /// <summary>
    /// Finds the highest SDK version installed on this computer: the one on the PATH, or the one this app installs.
    /// </summary>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The version, or <see langword="null" /> when there is none.</returns>
    Task<string?> InstalledVersion(CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class DotNetSdks(IPaths paths, IReleaseSources sources, IHttpFetcher http, IProcessRunner runner, IVersions versions, ILog log) : IDotNetSdks
{
    private static readonly string installedRoot = "/usr/share/dotnet";

    private DotNetRelease? latest;

    /// <inheritdoc />
    public async Task<DotNetRelease> Latest(CancellationToken cancellation = default)
        => latest ??= await sources.LatestDotNet(cancellation);

    /// <inheritdoc />
    public async Task<string> Tarball(CancellationToken cancellation = default)
    {
        DotNetRelease release = await Latest(cancellation);
        string path = Path.Combine(paths.Downloads, release.FileName);
        await http.Download(release.Url, path, release.Hash, cancellation);
        return path;
    }

    /// <inheritdoc />
    public async Task<ProcessResult> Run(IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string?>? environment = null, bool check = true, CancellationToken cancellation = default)
    {
        string directory = await Prepare(cancellation);
        Dictionary<string, string?> variables = new()
        {
            ["DOTNET_ROOT"] = directory,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_MULTILEVEL_LOOKUP"] = "0",
            ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
            ["PATH"] = directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
        };

        foreach ((string name, string? value) in environment ?? new Dictionary<string, string?>())
        {
            variables[name] = value;
        }

        return await runner.Run(new() { FileName = Path.Combine(directory, "dotnet"), Arguments = arguments, WorkingDirectory = workingDirectory, Environment = variables, Check = check }, cancellation);
    }

    /// <inheritdoc />
    public async Task<string?> InstalledVersion(CancellationToken cancellation = default)
    {
        List<string> found = [];
        foreach (string program in new[] { "dotnet", Path.Combine(installedRoot, "dotnet") })
        {
            ProcessResult result = await runner.Run(new() { FileName = program, Arguments = ["--list-sdks"], Check = false }, cancellation);
            found.AddRange(result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ')[0]));
        }

        return found.OrderBy(version => version, Comparer<string>.Create(versions.Compare)).LastOrDefault();
    }

    private async Task<string> Prepare(CancellationToken cancellation)
    {
        DotNetRelease release = await Latest(cancellation);
        string directory = Path.Combine(paths.Cache, "sdk", release.Version);
        if (File.Exists(Path.Combine(directory, "dotnet")))
        {
            return directory;
        }

        string tarball = await Tarball(cancellation);
        log.Step($"Unpacking .NET SDK {release.Version} for the build");
        string temporary = directory + ".tmp";
        if (Directory.Exists(temporary))
        {
            Directory.Delete(temporary, recursive: true);
        }

        Directory.CreateDirectory(temporary);
        await using FileStream file = File.OpenRead(tarball);
        await using GZipStream gzip = new(file, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzip, temporary, overwriteFiles: true, cancellation);
        Directory.Move(temporary, directory);
        return directory;
    }
}
