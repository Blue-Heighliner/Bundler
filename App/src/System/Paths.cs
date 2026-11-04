namespace BlueHeighliner.Bundler;

/// <summary>
/// The folders the app reads and writes. The bundle files are in <c>bundles</c> next to the executable unless told otherwise;
/// the cache and install records are in root's app data folder, because the app always runs as root.
/// </summary>
internal interface IPaths
{
    /// <summary>Gets the folder the app keeps its cache and install records in.</summary>
    string Data { get; }

    /// <summary>Gets the folder with the built bundle files.</summary>
    string Bundles { get; }

    /// <summary>Gets the build cache: downloads, the .NET SDK used to mirror NuGet packages, apt state.</summary>
    string Cache { get; }

    /// <summary>Gets the folder downloads are kept in between builds.</summary>
    string Downloads { get; }

    /// <summary>Gets the folder a bundle is assembled in before it is packed into its file.</summary>
    string Staging { get; }

    /// <summary>Gets the scratch folder for NuGet mirroring, outside any git repository.</summary>
    string Scratch { get; }

    /// <summary>Gets the folder a bundle is unpacked in while it installs.</summary>
    string Unpack { get; }

    /// <summary>Gets the folder that records what is installed.</summary>
    string State { get; }

    /// <summary>Gets the offline NuGet feed folder.</summary>
    string NuGetFeed { get; }
}

/// <inheritdoc />
internal sealed class Paths(IMachine machine, string? bundles = null) : IPaths
{
    /// <inheritdoc />
    public string Data { get; } = machine.DataDirectory;

    /// <inheritdoc />
    public string Bundles { get; } = Path.GetFullPath(bundles ?? Path.Combine(machine.ApplicationDirectory, "bundles"));

    /// <inheritdoc />
    public string Cache => Path.Combine(Data, "cache");

    /// <inheritdoc />
    public string Downloads => Path.Combine(Cache, "downloads");

    /// <inheritdoc />
    public string Staging => Path.Combine(Cache, "build");

    /// <inheritdoc />
    public string Scratch => "/var/tmp";

    /// <inheritdoc />
    public string Unpack => "/var/tmp";

    /// <inheritdoc />
    public string State => Path.Combine(Data, "state");

    /// <inheritdoc />
    public string NuGetFeed => "/opt/bundler/nuget-feed";
}
