namespace BlueHeighliner.Bundler;

/// <summary>
/// The newest stable Node.js with npm and npx, from nodejs.org. It goes into <c>/opt/node/&lt;version&gt;</c> with the commands <c>node</c>,
/// <c>npm</c> and <c>npx</c> linked into <c>/usr/local/bin</c>.
/// </summary>
internal sealed class NodeBundle(IReleaseSources sources, IDownloads downloads, IToolInstaller tool, IDecisions decisions, IVersions versions, IProcessRunner runner, ILog log) : BundleBase
{
    private readonly string root = "/opt/node";

    /// <inheritdoc />
    public override string Name => "node";

    /// <inheritdoc />
    public override string Description => "Run JavaScript programs and manage packages with Node.js and npm.";

    /// <inheritdoc />
    public override int Order => 27;

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        log.Step("Node.js");
        ArchiveRelease release = await sources.LatestNode(cancellation);
        await downloads.Place(await downloads.Fetch(release.Url, release.FileName, release.Hash, cancellation), Path.Combine(directory, "node", release.FileName), cancellation);
        return new() { Components = [$"Node.js {release.Version} (linux x64)"] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        string bundled = versions.Find(context.Info.Components.First(component => component.StartsWith("Node.js ", StringComparison.Ordinal)));
        string? have = tool.InstalledVersion(root);
        log.Step($"Node.js {bundled}");
        if (decisions.Install("Node.js", have, bundled))
        {
            string archive = Directory.EnumerateFiles(Path.Combine(context.Payload, "node"), "node-v*-linux-x64.tar.xz").First();
            await tool.Install(
                context.State,
                archive,
                root,
                bundled,
                new Dictionary<string, string> { ["node"] = "bin/node", ["npm"] = "bin/npm", ["npx"] = "bin/npx" },
                cancellation);
        }

        ProcessResult node = await runner.Try("node", ["--version"]);
        ProcessResult npm = await runner.Try("npm", ["--version"]);
        log.Info($"node {node.Text} | npm {npm.Text}");
    }

    /// <inheritdoc />
    public override Task Uninstall(UninstallContext context, CancellationToken cancellation = default)
    {
        if (!context.State.IsOwned("tool"))
        {
            log.Info("Node.js was already on this computer and was not installed by this bundle, so it is left in place");
        }

        tool.Uninstall(context.State);
        return Task.CompletedTask;
    }
}
