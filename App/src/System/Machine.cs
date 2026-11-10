namespace BlueHeighliner.Bundler;

/// <summary>
/// The facts about the machine and process the app runs in. Kept behind an interface so everything that depends on
/// them can be tested without touching the real environment.
/// </summary>
internal interface IMachine
{
    /// <summary>Gets a value indicating whether the app runs on Linux, the only platform bundles can be built and installed on.</summary>
    bool IsLinux { get; }

    /// <summary>Gets a value indicating whether the process runs with administrator (root) rights.</summary>
    bool IsPrivileged { get; }

    /// <summary>Gets the name of the user running the process.</summary>
    string UserName { get; }

    /// <summary>Gets the full path of the running executable, or <see langword="null" /> when it is unknown.</summary>
    string? ProcessPath { get; }

    /// <summary>Gets the words that start this application again: the executable, or <c>dotnet</c> and the assembly when run through the SDK.</summary>
    IReadOnlyList<string> SelfCommand { get; }

    /// <summary>Gets the folder the application lives in.</summary>
    string ApplicationDirectory { get; }

    /// <summary>Gets the name and version of the system this computer runs, such as <c>Ubuntu 24.04</c>, or <see langword="null" /> when it cannot be read.</summary>
    string? SystemName { get; }

    /// <summary>Gets the folder the app keeps its cache and install records in: <c>BlueHeighliner.Bundler</c> inside root's app data folder, because the app always runs as root.</summary>
    string DataDirectory { get; }

    /// <summary>Gets the variables a graphical program needs to reach the user's display (<c>DISPLAY</c>, <c>XAUTHORITY</c> and so on) that are set, so a copy of the app started as root can show its window.</summary>
    IReadOnlyDictionary<string, string> DisplayEnvironment { get; }

    /// <summary>Gets the home folder of the user running the process.</summary>
    string HomeDirectory { get; }

    /// <summary>
    /// Finds a program on the PATH.
    /// </summary>
    /// <param name="name">The program name.</param>
    /// <returns>The full path with symbolic links resolved, or <see langword="null" /> when it is not found.</returns>
    string? FindExecutable(string name);
}

/// <inheritdoc />
internal sealed class Machine : IMachine
{
    /// <inheritdoc />
    public bool IsLinux => OperatingSystem.IsLinux();

    /// <inheritdoc />
    public bool IsPrivileged => Environment.IsPrivilegedProcess;

    /// <inheritdoc />
    public string UserName => Environment.UserName;

    /// <inheritdoc />
    public string? ProcessPath => Environment.ProcessPath;

    /// <inheritdoc />
    public IReadOnlyList<string> SelfCommand
        => Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet"
            ? [Environment.ProcessPath!, Environment.GetCommandLineArgs()[0]]
            : [Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0]];

    /// <inheritdoc />
    public string? SystemName
        => File.Exists("/etc/os-release") ? SystemNameOf(File.ReadAllText("/etc/os-release")) : null;

    /// <inheritdoc />
    public string ApplicationDirectory => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    /// <inheritdoc />
    public string DataDirectory
        => OperatingSystem.IsLinux()
            ? Path.Combine(RootHome(), ".config", "BlueHeighliner.Bundler")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlueHeighliner.Bundler");

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> DisplayEnvironment { get; } = new[] { "DISPLAY", "XAUTHORITY", "WAYLAND_DISPLAY", "XDG_RUNTIME_DIR" }
        .Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name)))
        .Where(pair => !string.IsNullOrEmpty(pair.Value))
        .ToDictionary(pair => pair.Name, pair => pair.Value!);

    /// <inheritdoc />
    public string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Reads the name and version out of the text of an <c>os-release</c> file.
    /// </summary>
    /// <param name="osRelease">The file's text.</param>
    /// <returns>The name and version, such as <c>Ubuntu 24.04</c>, or <see langword="null" /> when either is missing.</returns>
    public string? SystemNameOf(string osRelease)
    {
        Dictionary<string, string> fields = osRelease.Split('\n').Select(line => line.Split('=', 2)).Where(parts => parts.Length == 2).ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim().Trim('"'), StringComparer.Ordinal);
        return fields.TryGetValue("NAME", out string? name) && fields.TryGetValue("VERSION_ID", out string? version) ? $"{name} {version}" : null;
    }

    /// <inheritdoc />
    public string? FindExecutable(string name)
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(folder, name);
            if (File.Exists(candidate))
            {
                return File.ResolveLinkTarget(candidate, returnFinalTarget: true)?.FullName ?? candidate;
            }
        }

        return null;
    }

    private string RootHome()
    {
        string? line = File.Exists("/etc/passwd") ? File.ReadLines("/etc/passwd").FirstOrDefault(entry => entry.StartsWith("root:", StringComparison.Ordinal)) : null;
        string[]? fields = line?.Split(':');
        return fields is { Length: >= 6 } && fields[5].Length > 0 ? fields[5] : "/root";
    }
}
