namespace BlueHeighliner.Bundler;

/// <summary>
/// A package in a folder of <c>.deb</c> files.
/// </summary>
/// <param name="Package">The package name.</param>
/// <param name="Version">The package version.</param>
/// <param name="FileName">The <c>.deb</c> file name.</param>
internal sealed record DebPackage(string Package, string Version, string FileName);
