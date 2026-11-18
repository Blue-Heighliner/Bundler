namespace BlueHeighliner.Bundler;

/// <summary>
/// Where the .NET SDKs are installed.
/// </summary>
internal interface IDotNetFiles
{
    /// <summary>Gets the folder every installed SDK and runtime lives in.</summary>
    string Root { get; }

    /// <summary>Gets the link that puts the <c>dotnet</c> command on the path.</summary>
    string Link { get; }

    /// <summary>Gets the machine-wide NuGet config that lists only the offline feed.</summary>
    string NuGetConfig { get; }
}

/// <inheritdoc />
internal sealed class DotNetFiles : IDotNetFiles
{
    /// <inheritdoc />
    public string Root { get; } = "/usr/share/dotnet";

    /// <inheritdoc />
    public string Link { get; } = "/usr/local/bin/dotnet";

    /// <inheritdoc />
    public string NuGetConfig { get; } = "/usr/local/share/NuGet/Config/bundler.config";
}
