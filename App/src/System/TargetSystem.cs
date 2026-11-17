namespace BlueHeighliner.Bundler;

/// <summary>
/// The one system every bundle is built for: the Ubuntu that the installer USB drive puts on a computer (Ubuntu 24.04.5 desktop with its
/// HWE kernel). It is known from the packages it installs, which are built into the app as a dpkg status file, so a build works out exactly what
/// that system lacks instead of guessing from a fresh release.
/// </summary>
internal interface ITargetSystem
{
    /// <summary>Gets the kernel release the target runs, such as <c>7.0.0-31-generic</c>. Packages with modules built for one kernel have to match it exactly.</summary>
    string Kernel { get; }

    /// <summary>Gets the packages the target has installed, in the format of <c>/var/lib/dpkg/status</c> (only the fields apt needs to plan an install).</summary>
    string PackageStatus { get; }

    /// <summary>
    /// Asks whether the target has a package installed.
    /// </summary>
    /// <param name="package">The package name.</param>
    /// <returns><see langword="true" /> when the target has it.</returns>
    bool IsInstalled(string package);
}

/// <inheritdoc />
internal sealed partial class TargetSystem(IResources resources) : ITargetSystem
{
    private string? status;

    private HashSet<string>? installed;

    /// <inheritdoc />
    public string Kernel
        => KernelPattern().Match(PackageStatus) is { Success: true } match
            ? match.Groups["kernel"].Value
            : throw new InvalidOperationException("the target package list has no installed kernel");

    /// <inheritdoc />
    public string PackageStatus
        => status ??= resources.Text("target/dpkg-status.txt");

    /// <inheritdoc />
    public bool IsInstalled(string package)
        => (installed ??= [.. NamePattern().Matches(PackageStatus).Select(match => match.Groups["name"].Value)]).Contains(package);

    [GeneratedRegex(@"^Package: linux-image-(?<kernel>[0-9]+\.[0-9]+\.[0-9]+-[0-9]+-generic)$", RegexOptions.Multiline)]
    private static partial Regex KernelPattern();

    [GeneratedRegex(@"^Package: (?<name>\S+)$", RegexOptions.Multiline)]
    private static partial Regex NamePattern();
}
