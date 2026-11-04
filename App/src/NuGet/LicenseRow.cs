namespace BlueHeighliner.Bundler;

/// <summary>
/// The license of one NuGet package.
/// </summary>
/// <param name="Id">The package id.</param>
/// <param name="Version">The package version.</param>
/// <param name="Status"><c>OK</c> when the license is permissive (or was read by a person), <c>REVIEW</c> when someone has to read its terms.</param>
/// <param name="License">The license as the package declares it.</param>
internal sealed record LicenseRow(string Id, string Version, string Status, string License)
{
    /// <summary>Gets a value indicating whether the package needs a person to read its license.</summary>
    public bool NeedsReview => Status == "REVIEW";
}
