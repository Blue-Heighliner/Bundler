namespace BlueHeighliner.Bundler;

/// <summary>
/// OpenCode, the AI coding agent that runs models on this computer (through Ollama, for example), as the <c>opencode</c> command.
/// Auto Remote (through its Live AI library) drives OpenCode 2.x, so the newest stable 2.x release is taken (the repository's "latest" release is a 1.x one).
/// It is installed in <c>/opt/open-code</c> with a link in <c>/usr/local/bin</c>.
/// </summary>
internal sealed class OpenCodeBundle(IReleaseSources sources, IDownloads downloads, IHttpFetcher http, IDecisions decisions, IVersions versions, IMachine machine, IProcessRunner runner, IOpenCodeFiles files, ILog log) : BundleBase
{
    private static readonly string repository = "anomalyco/opencode";

    /// <inheritdoc />
    public override string Name => "open-code";

    /// <inheritdoc />
    public override string Description => "Let an AI agent work on code from the command line with models that run on this computer: OpenCode.";

    /// <inheritdoc />
    public override int Order => 35;

    /// <inheritdoc />
    public override IReadOnlyList<string> Requires { get; } = ["git"];

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        log.Step("OpenCode");
        GitHubRelease release = await sources.NewestGitHub(repository, @"^v(2\.\d+\.\d+)$", [@"^opencode-linux-x64-baseline\.tar\.gz$"], cancellation);
        DownloadAsset archive = release.Assets[0];
        await downloads.Place(await downloads.Fetch(archive.Url, archive.FileName, archive.Hash, cancellation), Path.Combine(directory, archive.FileName), cancellation);
        await http.Download($"https://raw.githubusercontent.com/{repository}/{release.Tag}/LICENSE", Path.Combine(directory, "LICENSE"), null, cancellation);
        return new() { Components = [$"OpenCode {release.Tag.TrimStart('v')} (linux x64)"] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        string bundled = versions.Find(context.Info.Components.First(component => component.StartsWith("OpenCode ", StringComparison.Ordinal)));
        log.Step($"OpenCode {bundled}");
        string? have = null;
        if (machine.FindExecutable("opencode") is string existing)
        {
            ProcessResult result = await runner.Try(existing, ["--version"]);
            string found = result.Success ? versions.Find(result.Output + result.Error) : string.Empty;
            have = found.Length > 0 ? found : null;
        }

        if (decisions.Install("OpenCode", have, bundled))
        {
            await InstallFiles(context, cancellation);
        }

        log.Info((await runner.Try("opencode", ["--version"])).Text);
    }

    /// <inheritdoc />
    public override Task Uninstall(UninstallContext context, CancellationToken cancellation = default)
    {
        if (!context.State.IsOwned("opencode"))
        {
            log.Info("OpenCode was already on this computer and was not installed by this bundle, so it is left in place");
            return Task.CompletedTask;
        }

        if (new FileInfo(files.Link).LinkTarget == files.Binary)
        {
            File.DeleteIfPresent(files.Link);
        }

        if (Directory.Exists(files.Root))
        {
            Directory.Delete(files.Root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private async Task InstallFiles(InstallContext context, CancellationToken cancellation)
    {
        string archive = Directory.EnumerateFiles(context.Payload, "opencode-linux-x64*.tar.gz").First();
        string staging = Path.Combine(context.Payload, "extracted");
        Directory.CreateDirectory(staging);
        await using (FileStream file = File.OpenRead(archive))
        await using (GZipStream gzip = new(file, CompressionMode.Decompress))
        {
            await TarFile.ExtractToDirectoryAsync(gzip, staging, overwriteFiles: true, cancellation);
        }

        string binary = files.Binary;
        Directory.CreateDirectory(files.Root);
        File.Copy(Path.Combine(staging, "opencode"), binary + ".new", overwrite: true);
        File.SetUnixFileMode(binary + ".new", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.Move(binary + ".new", binary, overwrite: true);
        File.Copy(Path.Combine(context.Payload, "LICENSE"), Path.Combine(files.Root, "LICENSE"), overwrite: true);
        Directory.CreateDirectory(Path.GetDirectoryName(files.Link)!);
        File.DeleteIfPresent(files.Link);
        File.CreateSymbolicLink(files.Link, binary);
        context.State.MarkOwned("opencode");
    }
}
