namespace BlueHeighliner.Bundler;

/// <summary>
/// The newest stable CPython, with pip, venv, the headers and Tcl/Tk, from the python-build-standalone project, which builds a Python that needs
/// nothing from the system but the C library. It goes into <c>/opt/python/&lt;version&gt;</c> with the commands <c>python</c>, <c>python3.N</c>, <c>pip</c>
/// and <c>pip3.N</c> in <c>/usr/local/bin</c>. The command <c>python3</c> stays the system's Python, which the operating system itself needs.
/// </summary>
internal sealed class PythonBundle(IReleaseSources sources, IDownloads downloads, IToolInstaller tool, IDecisions decisions, IVersions versions, IProcessRunner runner, ILog log) : BundleBase
{
    private readonly string root = "/opt/python";

    /// <inheritdoc />
    public override string Name => "python";

    /// <inheritdoc />
    public override string Description => "Write and run Python programs with the newest Python, pip and virtual environments, with no network.";

    /// <inheritdoc />
    public override int Order => 26;

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        log.Step("Python");
        ArchiveRelease release = await sources.LatestPython(cancellation);
        await downloads.Place(await downloads.Fetch(release.Url, release.FileName, release.Hash, cancellation), Path.Combine(directory, "python", release.FileName), cancellation);
        return new() { Components = [$"Python {release.Version} (linux x64, python-build-standalone)"] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        string bundled = versions.Find(context.Info.Components.First(component => component.StartsWith("Python ", StringComparison.Ordinal)));
        string? have = tool.InstalledVersion(root);
        log.Step($"Python {bundled}");
        if (decisions.Install("Python", have, bundled))
        {
            string archive = Directory.EnumerateFiles(Path.Combine(context.Payload, "python"), "cpython-*.tar.gz").First();
            string minor = string.Join('.', bundled.Split('.').Take(2));
            await tool.Install(
                context.State,
                archive,
                root,
                bundled,
                new Dictionary<string, string> { ["python"] = "bin/python3", [$"python{minor}"] = $"bin/python{minor}", ["pip"] = "bin/pip3", [$"pip{minor}"] = $"bin/pip{minor}" },
                cancellation);
        }

        ProcessResult python = await runner.Try("python", ["--version"]);
        ProcessResult pip = await runner.Try("python", ["-m", "pip", "--version"]);
        log.Info($"{python.Text} | {pip.Text}");
    }

    /// <inheritdoc />
    public override UninstallScript Script(UninstallContext context)
    {
        UninstallScript script = new();
        if (!context.State.IsOwned("tool"))
        {
            script.Info("Python was already on this computer and was not installed by this bundle, so it is left in place");
        }

        tool.Uninstall(script, context.State);
        return script;
    }
}
