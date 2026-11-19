namespace BlueHeighliner.Bundler;

/// <summary>
/// The .NET 10.0 and 11.0 SDKs side by side in one folder, and the offline NuGet feed that restore uses by default (machine-wide and
/// for every user), so C# can be written and built without internet whichever of the two SDKs and target frameworks a project uses.
/// </summary>
internal sealed class DotNetBundle(
    IDotNetSdks sdks,
    IDownloads downloads,
    IAptBuilder apt,
    IAptInstaller aptInstaller,
    INuGetBundleBuilder nugetBuilder,
    INuGetFeed feed,
    IDecisions decisions,
    IVersions versions,
    IDotNetFiles files,
    IUsers users,
    ITextBlocks blocks,
    IProcessRunner runner,
    ILog log) : BundleBase
{
    private static readonly string[] templates = ["console", "classlib", "worker", "webapi", "web", "mvc", "razor", "blazor", "grpc"];

    private static readonly NuGetBundleSpec spec = new() { PackageList = "dotnet/packages.txt", WarmTemplates = templates };

    /// <inheritdoc />
    public override string Name => "dotnet";

    /// <inheritdoc />
    public override string Description => "Write and build C# without internet: the .NET 10 and .NET 11 SDKs and an offline package feed.";

    /// <inheritdoc />
    public override int Order => 20;

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        log.Step(".NET SDKs");
        IReadOnlyList<DotNetRelease> releases = await sdks.Releases(cancellation);
        foreach (DotNetRelease release in releases)
        {
            await downloads.Place(await sdks.Tarball(release, cancellation), Path.Combine(directory, "dotnet", release.FileName), cancellation);
        }

        log.Step("Ubuntu packages (the libraries .NET needs)");
        await apt.Prepare([], cancellation);
        string icu = await apt.Find(@"libicu[0-9]+", cancellation) ?? throw new InvalidOperationException("could not find the ICU library in the Ubuntu 24.04 archive");
        string ssl = await apt.Find(@"libssl[0-9]+(t64)?", cancellation) ?? throw new InvalidOperationException("could not find the OpenSSL library in the Ubuntu 24.04 archive");
        await apt.Download(Path.Combine(directory, "debs"), new() { Support = ["ca-certificates", "tzdata", "zlib1g", "libstdc++6", "libgcc-s1", icu, ssl] }, cancellation);

        log.Step("Offline NuGet feed");
        NuGetBuildResult nuget = await nugetBuilder.Build(spec, directory, cancellation);
        return new() { Components = [.. releases.Select(release => $".NET SDK {release.Version} (runtime {release.Runtime})"), nuget.Components[^1]] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        log.Step("Ubuntu packages");
        await aptInstaller.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);
        await InstallDotNet(context, cancellation);

        log.Step("Offline NuGet feed");
        feed.Add(Path.Combine(context.Payload, "nuget"), context.State);
        feed.WriteConfig(files.NuGetConfig);
        if (File.Exists(Path.Combine(context.Payload, "nuget-licenses.tsv")))
        {
            File.Copy(Path.Combine(context.Payload, "nuget-licenses.tsv"), Path.Combine(feed.Directory, "LICENSES.tsv"), overwrite: true);
        }

        log.Step("NuGet settings for each user");
        if (context.Users.Count == 0)
        {
            log.Warn("no normal user accounts found, so no NuGet settings were written. Run the install again after creating one.");
        }

        foreach (string user in context.Users)
        {
            await SetUpUser(user, cancellation);
        }
    }

    /// <inheritdoc />
    public override UninstallScript Script(UninstallContext context)
    {
        UninstallScript script = new();
        foreach (string user in context.Users.Where(user => Directory.Exists(users.Home(user))))
        {
            script.Add("restore-backup", Path.Combine(users.Home(user), ".nuget", "NuGet", "NuGet.Config"), "bundler");
        }

        script.Remove(files.NuGetConfig);
        script.RemoveEmpty(Path.GetDirectoryName(files.NuGetConfig)!);
        script.RemoveEmpty(Path.GetDirectoryName(Path.GetDirectoryName(files.NuGetConfig)!)!);
        script.RemoveTree(feed.Directory);
        script.RemoveEmpty(Path.GetDirectoryName(feed.Directory)!);
        AddDotNetRemoval(script, context);
        script.Add("block-remove", "/etc/environment", "dotnet");
        script.Add("apt-remove-added");
        return script;
    }

    private async Task InstallDotNet(InstallContext context, CancellationToken cancellation)
    {
        log.Step(".NET SDKs");
        IReadOnlyList<string> installed = await sdks.InstalledVersions(cancellation);
        List<string> wanted = [];
        foreach (string tarball in Directory.EnumerateFiles(Path.Combine(context.Payload, "dotnet"), "dotnet-sdk-*-linux-x64.tar.gz").Order(Comparer<string>.Create(versions.Compare)))
        {
            string bundled = Path.GetFileName(tarball)["dotnet-sdk-".Length..^"-linux-x64.tar.gz".Length];
            string major = bundled.Split('.')[0];
            string? have = installed.Where(version => version.Split('.')[0] == major).LastOrDefault();
            if (decisions.Install($".NET {major} SDK", have, bundled))
            {
                wanted.Add(tarball);
            }
        }

        if (wanted.Count > 0)
        {
            await Unpack(wanted, context.State, cancellation);
        }

        if (!File.Exists(files.Link) && new FileInfo(files.Link).LinkTarget is null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(files.Link)!);
            File.CreateSymbolicLink(files.Link, Path.Combine(files.Root, "dotnet"));
            context.State.Write("dotnet-link", files.Link);
        }

        blocks.Add("/etc/environment", "dotnet", ["DOTNET_CLI_TELEMETRY_OPTOUT=1", "DOTNET_NOLOGO=1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"]);
        log.Info((await runner.Try(Path.Combine(files.Root, "dotnet"), ["--list-sdks"])).Text);
    }

    private async Task Unpack(List<string> tarballs, IBundleState state, CancellationToken cancellation)
    {
        string staging = files.Root + ".new";
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.CreateDirectory(staging);
        foreach (string tarball in tarballs)
        {
            await using FileStream file = File.OpenRead(tarball);
            await using GZipStream gzip = new(file, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gzip, staging, overwriteFiles: true, cancellation);
        }

        bool newerHost = HighestHost(staging) is string staged && (HighestHost(files.Root) is not string existing || versions.Compare(staged, existing) > 0);
        Directory.CreateDirectory(files.Root);
        Merge(staging, files.Root, string.Empty, newerHost, state);
        Directory.Delete(staging, recursive: true);
    }

    private void Merge(string source, string destination, string relative, bool replaceHost, IBundleState state)
    {
        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            string path = Path.Combine(relative, Path.GetFileName(directory));
            string target = Path.Combine(destination, Path.GetFileName(directory));
            if (Directory.Exists(target))
            {
                Merge(directory, target, path, replaceHost, state);
            }
            else
            {
                Directory.Move(directory, target);
                state.Add("dotnet-added", path);
            }
        }

        foreach (string file in Directory.EnumerateFiles(source))
        {
            string path = Path.Combine(relative, Path.GetFileName(file));
            string target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                File.Move(file, target);
                state.Add("dotnet-added", path);
            }
            else if (replaceHost && path == "dotnet")
            {
                File.Move(file, target, overwrite: true);
            }
        }
    }

    private string? HighestHost(string root)
    {
        string folder = Path.Combine(root, "host", "fxr");
        return Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).Select(Path.GetFileName).Order(Comparer<string?>.Create((left, right) => versions.Compare(left!, right!))).LastOrDefault() : null;
    }

    private async Task SetUpUser(string user, CancellationToken cancellation)
    {
        log.Info($"user {user}");
        string nuget = Path.Combine(users.Home(user), ".nuget", "NuGet", "NuGet.Config");
        if (File.Exists(nuget) && !File.ReadAllText(nuget).Contains("bundler", StringComparison.Ordinal) && !File.Exists(nuget + ".bundler.bak"))
        {
            File.Copy(nuget, nuget + ".bundler.bak");
        }

        await users.MakeDirectory(user, Path.GetDirectoryName(nuget)!, cancellation);
        feed.WriteConfig(nuget);
        await users.Own(user, [nuget], cancellation);
    }

    private void AddDotNetRemoval(UninstallScript script, UninstallContext context)
    {
        string dotnet = Path.Combine(files.Root, "dotnet");
        if (context.State.IsOwned("dotnet"))
        {
            script.RemoveTree(files.Root);
        }
        else
        {
            foreach (string path in context.State.Lines("dotnet-added").Reverse())
            {
                string full = Path.Combine(files.Root, path);
                script.Add("rm-path", full);
                script.Add("rm-empty-parents", full, files.Root);
            }

            if (!context.State.Lines("dotnet-added").Any())
            {
                script.Info(".NET was already on this computer and was not installed by this bundle, so it is left in place");
            }

            script.RemoveEmpty(files.Root);
        }

        string? linked = context.State.Read("dotnet-link")?.Trim();
        if (!string.IsNullOrEmpty(linked))
        {
            script.RemoveLink(linked, dotnet);
        }

        if (context.State.IsOwned("dotnet"))
        {
            script.RemoveLink(files.Link, dotnet);
        }
    }
}
