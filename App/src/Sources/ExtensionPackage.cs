namespace BlueHeighliner.Bundler;

/// <summary>
/// A downloaded VS Code extension.
/// </summary>
/// <param name="Id">The extension id, <c>publisher.name</c> in lower case.</param>
/// <param name="Version">The version.</param>
/// <param name="FileName">The <c>.vsix</c> file name in the output folder.</param>
internal sealed record ExtensionPackage(string Id, string Version, string FileName);
