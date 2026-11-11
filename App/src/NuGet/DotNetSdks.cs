namespace BlueHeighliner.Bundler;

/// <summary>
/// The .NET SDKs: the latest stable ones of the channels that bundles ship, private copies of them used while building to mirror
/// NuGet packages, and the ones installed on the computer.
/// </summary>
internal interface IDotNetSdks
{
    /// <summary>
    /// Finds the latest stable SDK release of each .NET channel the bundles support (10.0, the long term support release, and
    /// 11.0), oldest channel first, looking them up once.
    /// </summary>
    /// <param name="cancellation">Stops the lookup when cancelled.</param>
    /// <returns>The releases.</returns>
    Task<IReadOnlyList<DotNetRelease>> Releases(CancellationToken cancellation = default);

    /// <summary>
    /// Downloads the SDK tarball of a release into the build cache, checking its checksum.
    /// </summary>
    /// <param name="release">The release.</param>
    /// <param name="cancellation">Stops the download when cancelled.</param>
    /// <returns>The path of the tarball.</returns>
    Task<string> Tarball(DotNetRelease release, CancellationToken cancellation = default);

    /// <summary>
    /// Runs the private copy of an SDK, unpacking it into the build cache first when needed. Telemetry and first-run
    /// messages are off.
    /// </summary>
    /// <param name="release">The SDK to run.</param>
    /// <param name="arguments">The <c>dotnet</c> arguments.</param>
    /// <param name="workingDirectory">The folder to run in.</param>
    /// <param name="environment">More environment variables.</param>
    /// <param name="check">Whether a non-zero exit code throws.</param>
    /// <param name="cancellation">Stops the program when cancelled.</param>
    /// <returns>The result.</returns>
    Task<ProcessResult> Run(DotNetRelease release, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string?>? environment = null, bool check = true, CancellationToken cancellation = default);

    /// <summary>
    /// Finds the SDK versions installed on this computer: the ones the <c>dotnet</c> on the PATH finds and the ones in the folder this app installs into.
    /// </summary>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The versions, lowest first; empty when there are none.</returns>
    Task<IReadOnlyList<string>> InstalledVersions(CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class DotNetSdks(IPaths paths, IReleaseSources sources, IHttpFetcher http, IProcessRunner runner, IVersions versions, IDotNetFiles files, ILog log) : IDotNetSdks
{
    private readonly string[] channels = ["10.0", "11.0"];

    private IReadOnlyList<DotNetRelease>? releases;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DotNetRelease>> Releases(CancellationToken cancellation = default)
    {
        if (releases is null)
        {
            List<DotNetRelease> found = [];
            foreach (string channel in channels)
            {
                found.Add(await sources.LatestDotNet(channel, cancellation));
            }

            releases = found;
        }

        return releases;
    }

    /// <inheritdoc />
    public async Task<string> Tarball(DotNetRelease release, CancellationToken cancellation = default)
    {
        string path = Path.Combine(paths.Downloads, release.FileName);
        await http.Download(release.Url, path, release.Hash, cancellation);
        return path;
    }

    /// <inheritdoc />
    public async Task<ProcessResult> Run(DotNetRelease release, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string?>? environment = null, bool check = true, CancellationToken cancellation = default)
    {
        string directory = await Prepare(release, cancellation);
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
    public async Task<IReadOnlyList<string>> InstalledVersions(CancellationToken cancellation = default)
    {
        List<string> found = [];
        foreach (string program in new[] { "dotnet", Path.Combine(files.Root, "dotnet") })
        {
            ProcessResult result = await runner.Run(new() { FileName = program, Arguments = ["--list-sdks"], Check = false }, cancellation);
            found.AddRange(result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ')[0]));
        }

        return [.. found.Distinct().Order(Comparer<string>.Create(versions.Compare))];
    }

    private async Task<string> Prepare(DotNetRelease release, CancellationToken cancellation)
    {
        string directory = Path.Combine(paths.Cache, "sdk", release.Version);
        if (File.Exists(Path.Combine(directory, "dotnet")))
        {
            return directory;
        }

        string tarball = await Tarball(release, cancellation);
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
