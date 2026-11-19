namespace BlueHeighliner.Bundler;

/// <summary>
/// The ChromaDB vector database and the Nomic embedding model (nomic-embed-text) that turns text into the vectors it stores. ChromaDB comes as Python
/// wheels, with the Ollama Python client that its Ollama embedding function needs, that install into a virtual environment of their own and run as a local-only systemd service on <c>127.0.0.1:8000</c>; the model goes into
/// Ollama, so programs get their embeddings from Ollama with no network.
/// </summary>
internal sealed class ChromaDbBundle(
    IPythonWheels python,
    IOllamaRegistry registry,
    IOllamaModelFiles modelFiles,
    IAptBuilder apt,
    IAptInstaller aptInstaller,
    IResources resources,
    IDecisions decisions,
    IVersions versions,
    ISystemd systemd,
    IProcessRunner runner,
    ILog log,
    string root = "/opt/chroma-db",
    string data = "/var/lib/chroma-db",
    string link = "/usr/local/bin/chroma",
    string unit = "/etc/systemd/system/chroma-db.service") : BundleBase
{
    private readonly string model = "nomic-embed-text:latest";

    /// <inheritdoc />
    public override string Name => "chroma-db";

    /// <inheritdoc />
    public override string Description => "Store and search text by meaning on this computer: the ChromaDB vector database as a background service, with the Nomic embedding model for Ollama.";

    /// <inheritdoc />
    public override int Order => 32;

    /// <inheritdoc />
    public override IReadOnlyList<string> Requires { get; } = ["ollama"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Resumable { get; } = ["ollama-models"];

    private string Python => Path.Combine(root, "venv", "bin", "python");

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        IReadOnlyList<PythonWheel> wheels = await python.Download(Path.Combine(directory, "wheels"), ["chromadb", "ollama"], cancellation);
        PythonWheel chroma = wheels.First(wheel => wheel.Name.Equals("chromadb", StringComparison.OrdinalIgnoreCase));
        await File.WriteAllTextAsync(Path.Combine(directory, "python-licenses.tsv"), python.Format(wheels), cancellation);
        foreach (PythonWheel wheel in wheels.Where(wheel => wheel.NeedsReview))
        {
            log.Warn($"{wheel.Name} {wheel.Version} has a license that is not on the permissive list: {wheel.License}");
        }

        log.Step($"Downloading {model} from the Ollama registry");
        string[] parts = model.Split(':');
        OllamaModel downloaded = await registry.Fetch(parts[0], parts[1], Path.Combine(directory, "ollama-models"), cancellation);
        await File.WriteAllTextAsync(Path.Combine(directory, "model.info"), $"model={model}\nrequires_ollama={downloaded.Requires}\nbytes={downloaded.Bytes}\n", cancellation);

        log.Step("Ubuntu packages (Python virtual environments)");
        await apt.Prepare([], cancellation);
        await apt.Download(Path.Combine(directory, "debs"), new() { Support = ["python3-venv"] }, cancellation);
        return new() { Components = [$"ChromaDB {chroma.Version}", $"{wheels.Count} Python packages (wheels for Python 3.12, linux x86-64)", $"{model} {downloaded.Parameters} {downloaded.Quantization}"] };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        log.Step("Ubuntu packages");
        await aptInstaller.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);

        log.Step("ChromaDB");
        string bundled = versions.Find(context.Info.Components.First(component => component.StartsWith("ChromaDB ", StringComparison.Ordinal)));
        string? have = await InstalledVersion(cancellation);
        bool changed = false;
        if (decisions.Install("ChromaDB", have, bundled))
        {
            Directory.CreateDirectory(root);
            await runner.Run(new() { FileName = "python3", Arguments = ["-m", "venv", "--clear", Path.Combine(root, "venv")] }, cancellation);
            await runner.Run(
                new() { FileName = Python, Arguments = ["-m", "pip", "install", "--no-index", "--find-links", Path.Combine(context.Payload, "wheels"), "--disable-pip-version-check", "--no-warn-script-location", $"chromadb=={bundled}", "ollama"], StreamOutput = true },
                cancellation);
            context.State.MarkOwned("chroma");
            changed = true;
        }

        string chroma = Path.Combine(root, "venv", "bin", "chroma");
        if (!File.Exists(link) && new FileInfo(link).LinkTarget is null)
        {
            File.CreateSymbolicLink(link, chroma);
            context.State.MarkOwned("link");
        }

        log.Step("Service account and data folder");
        if (!(await runner.Try("getent", ["group", "chroma"])).Success)
        {
            await runner.Run(new() { FileName = "groupadd", Arguments = ["-r", "chroma"] }, cancellation);
        }

        if (!(await runner.Try("id", ["chroma"])).Success)
        {
            await runner.Run(new() { FileName = "useradd", Arguments = ["-r", "-g", "chroma", "-d", data, "-s", "/usr/sbin/nologin", "chroma"] }, cancellation);
            context.State.MarkOwned("user");
        }

        Directory.CreateDirectory(data);
        await runner.Run(new() { FileName = "chown", Arguments = ["chroma:chroma", data] }, cancellation);
        Directory.CreateDirectory(Path.GetDirectoryName(unit)!);
        string text = resources.Text("chroma-db/chroma-db.service").Replace("/opt/chroma-db", root, StringComparison.Ordinal).Replace("/var/lib/chroma-db", data, StringComparison.Ordinal);
        if (!File.Exists(unit) || File.ReadAllText(unit) != text)
        {
            await File.WriteAllTextAsync(unit, text, cancellation);
            changed = true;
        }

        if (systemd.IsRunning)
        {
            await systemd.Reload(cancellation);
            await systemd.Control("enable", "chroma-db", cancellation);
            await systemd.Control(changed ? "restart" : "start", "chroma-db", cancellation);
            await WaitForServer(cancellation);
        }
        else
        {
            log.Info("systemd is not running here, the ChromaDB service starts at the next boot");
        }

        log.Step("Nomic embedding model");
        string name = await modelFiles.Install(context, cancellation);
        await modelFiles.Verify(name, cancellation);
        log.Info($"ChromaDB listens on 127.0.0.1:8000. Its default embedding model needs the internet, so create collections with the Ollama embedding function and {name}: chromadb.utils.embedding_functions.OllamaEmbeddingFunction(url=\"http://localhost:11434\", model_name=\"{name}\")");
    }

    /// <inheritdoc />
    public override UninstallScript Script(UninstallContext context)
    {
        UninstallScript script = new();
        script.Add("systemctl", "disable --now", "chroma-db");
        script.Remove(unit);
        script.Add("daemon-reload");
        if (context.State.IsOwned("link"))
        {
            script.Remove(link);
        }

        if (context.State.IsOwned("chroma"))
        {
            script.RemoveTree(root);
        }
        else
        {
            script.Info("ChromaDB was already on this computer and was not installed by this bundle, so it is left in place");
        }

        script.IfFiles(true, [data], then => then.Warn($"the ChromaDB data is still in {data} (the chroma user is kept). Delete that folder by hand if you do not need it."));
        if (context.State.IsOwned("user"))
        {
            script.IfFiles(false, [data], then =>
            {
                then.Add("run", "userdel", "chroma");
                then.RemoveTree(data);
            });
        }

        modelFiles.Uninstall(script, context.State, model);
        script.Add("apt-remove-added");
        return script;
    }

    private async Task<string?> InstalledVersion(CancellationToken cancellation)
    {
        if (!File.Exists(Python))
        {
            return null;
        }

        ProcessResult result = await runner.Run(new() { FileName = Python, Arguments = ["-c", "import importlib.metadata as m;print(m.version('chromadb'))"], Check = false }, cancellation);
        return result.Success ? result.Output.Trim() : null;
    }

    private async Task WaitForServer(CancellationToken cancellation)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            ProcessResult result = await runner.Run(new() { FileName = Python, Arguments = ["-c", "import urllib.request;urllib.request.urlopen('http://127.0.0.1:8000/api/v2/heartbeat',timeout=2)"], Check = false }, cancellation);
            if (result.Success)
            {
                log.Info("the ChromaDB service answers");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellation);
        }

        log.Warn("the ChromaDB service did not answer within 30 seconds (see journalctl -u chroma-db)");
    }
}
