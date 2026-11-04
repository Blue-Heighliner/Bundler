namespace BlueHeighliner.Bundler;

/// <summary>
/// One row of the services table of a bundle in <c>Bundles.md</c>.
/// </summary>
/// <param name="Service">The service, in a few words.</param>
/// <param name="Version">Its version, or a dash.</param>
/// <param name="Description">What it is, with the details.</param>
/// <param name="Licenses">The short names of its licenses; each has a text in <c>licenses/texts</c>.</param>
/// <param name="Usage">What the licenses allow.</param>
internal sealed record ServiceRow(string Service, string Version, string Description, IReadOnlyList<string> Licenses, string Usage)
{
    /// <summary>Gets one link per license, each knowing the others so a viewer can show them all.</summary>
    public IReadOnlyList<LicenseLink> Links { get; } = [.. Licenses.Select(license => new LicenseLink(Service, license, Licenses))];
}
