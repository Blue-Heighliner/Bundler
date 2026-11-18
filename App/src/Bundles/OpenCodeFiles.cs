namespace BlueHeighliner.Bundler;

/// <summary>
/// Where OpenCode is installed.
/// </summary>
internal interface IOpenCodeFiles
{
    /// <summary>Gets the folder OpenCode and its license are installed in.</summary>
    string Root { get; }

    /// <summary>Gets the OpenCode executable.</summary>
    string Binary { get; }

    /// <summary>Gets the link that puts the <c>opencode</c> command on the path.</summary>
    string Link { get; }
}

/// <inheritdoc />
internal sealed class OpenCodeFiles : IOpenCodeFiles
{
    /// <inheritdoc />
    public string Root { get; } = "/opt/open-code";

    /// <inheritdoc />
    public string Binary => Path.Combine(Root, "opencode");

    /// <inheritdoc />
    public string Link { get; } = "/usr/local/bin/opencode";
}
