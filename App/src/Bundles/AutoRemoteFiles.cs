namespace BlueHeighliner.Bundler;

/// <summary>
/// Where Auto Remote is installed and the text of the files the bundle writes for it: the systemd user unit of the server, the
/// desktop entry of the client, the server's first config and its first AI environment.
/// </summary>
internal interface IAutoRemoteFiles
{
    /// <summary>Gets the folder everything is installed in.</summary>
    string Root { get; }

    /// <summary>Gets the server executable.</summary>
    string Server { get; }

    /// <summary>Gets the desktop client executable.</summary>
    string Client { get; }

    /// <summary>Gets the icon of the desktop client.</summary>
    string Icon { get; }

    /// <summary>Gets the desktop entry that puts the client in the app finder.</summary>
    string DesktopEntry { get; }

    /// <summary>Gets the file that records which version is installed.</summary>
    string VersionFile { get; }

    /// <summary>Gets the text of the server's systemd user unit.</summary>
    string UnitText { get; }

    /// <summary>Gets the text of the desktop entry.</summary>
    string DesktopEntryText { get; }

    /// <summary>Gets the model new workspaces run on.</summary>
    string LocalModel { get; }

    /// <summary>Gets the text of the server's first AI environment (<c>environment.json</c>): the local model for every capability on the local Ollama, and local models as the provider of new workspaces.</summary>
    string EnvironmentText { get; }

    /// <summary>
    /// Gives the port the server of the user at a position in the list of users listens on, so that the servers of several users
    /// on one computer do not fight over one port.
    /// </summary>
    /// <param name="position">The position of the user, from zero.</param>
    /// <returns>The port; the first user gets the server's default.</returns>
    int PortFor(int position);

    /// <summary>
    /// Makes the first config of a server.
    /// </summary>
    /// <param name="key">The key a client has to send to connect.</param>
    /// <param name="port">The port, or <see langword="null" /> for the server's default.</param>
    /// <returns>The JSON text.</returns>
    string ConfigText(string key, int? port);

    /// <summary>
    /// Makes a new random key for a server.
    /// </summary>
    /// <returns>A key that is safe to put in a file and a URL.</returns>
    string NewKey();
}

/// <inheritdoc />
internal sealed class AutoRemoteFiles : IAutoRemoteFiles
{
    private readonly int defaultPort = 52679;

    private readonly int localContext = 16384;

    /// <inheritdoc />
    public string Root { get; } = "/opt/auto-remote";

    /// <inheritdoc />
    public string Server => Path.Combine(Root, "server", "Markwardt.AutoRemote.Server");

    /// <inheritdoc />
    public string Client => Path.Combine(Root, "desktop", "Markwardt.AutoRemote.DesktopClient");

    /// <inheritdoc />
    public string Icon { get; } = "/usr/share/icons/hicolor/1024x1024/apps/auto-remote.png";

    /// <inheritdoc />
    public string DesktopEntry { get; } = "/usr/share/applications/auto-remote.desktop";

    /// <inheritdoc />
    public string VersionFile => Path.Combine(Root, "version.txt");

    /// <inheritdoc />
    public string UnitText
        => $"""
            [Unit]
            Description=Auto-Remote server

            [Service]
            ExecStart={Server} %h/.config/auto-remote/config.json
            WorkingDirectory=%h
            Environment=PATH=%h/.local/bin:/usr/lib/dotnet:%h/.dotnet/tools:/usr/local/bin:/usr/bin:/bin
            Restart=on-failure
            RestartSec=5

            [Install]
            WantedBy=default.target

            """;

    /// <inheritdoc />
    public string LocalModel { get; } = "gemma4:26b";

    /// <inheritdoc />
    public string EnvironmentText
        => new JsonObject
        {
            ["version"] = 1,
            ["defaultService"] = "Local",
            ["localModels"] = new JsonObject { ["fast"] = LocalModel, ["balanced"] = LocalModel, ["strong"] = LocalModel, ["max"] = LocalModel },
            ["localContexts"] = new JsonObject { ["fast"] = localContext, ["balanced"] = localContext, ["strong"] = localContext, ["max"] = localContext },
        }.ToJsonString(new() { WriteIndented = true }) + "\n";

    /// <inheritdoc />
    public string DesktopEntryText
        => $"""
            [Desktop Entry]
            Type=Application
            Name=Auto Remote
            Comment=Auto Remote desktop client
            Exec={Client}
            Icon={Icon}
            Terminal=false
            Categories=Development;
            StartupWMClass=Markwardt.AutoRemote.DesktopClient

            """;

    /// <inheritdoc />
    public int PortFor(int position)
        => defaultPort + position;

    /// <inheritdoc />
    public string ConfigText(string key, int? port)
    {
        JsonObject config = new() { ["key"] = key };
        if (port is int value && value != defaultPort)
        {
            config["port"] = value;
        }

        return config.ToJsonString(new() { WriteIndented = true }) + "\n";
    }

    /// <inheritdoc />
    public string NewKey()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
