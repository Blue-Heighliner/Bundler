namespace BlueHeighliner.Bundler;

/// <summary>
/// A license shown in a services table that opens its text.
/// </summary>
/// <param name="Service">The service the license belongs to.</param>
/// <param name="Id">The short name of the license, such as <c>MIT</c>.</param>
/// <param name="All">The short names of all licenses of the service, which open together.</param>
internal sealed record LicenseLink(string Service, string Id, IReadOnlyList<string> All);
