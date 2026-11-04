namespace BlueHeighliner.Bundler;

/// <summary>
/// Avalonia UI packages, the useful packages that go with them, and the <c>dotnet new</c> templates. Every project the stock
/// templates create references <c>AvaloniaUI.DiagnosticsSupport</c>, the bridge to AvaloniaUI's paid Developer Tools, which
/// has no stated license, and calls <c>WithDeveloperTools()</c> from <c>Program.cs</c>, so the template pack is installed with
/// those references and calls removed.
/// </summary>
internal sealed class AvaloniaBundle(INuGetBundleBuilder builder, INuGetBundleInstaller installer) : NuGetBundleBase(builder, installer)
{
    /// <inheritdoc />
    public override string Name => "avalonia";

    /// <inheritdoc />
    public override string Description => "Build cross-platform desktop apps in C# with Avalonia, with project templates.";

    /// <inheritdoc />
    public override int Order => 50;

    /// <inheritdoc />
    protected override NuGetBundleSpec Spec { get; } = new()
    {
        PackageList = "avalonia/packages.txt",
        TemplatePackages = ["Avalonia.Templates"],
        TemplateStrip = ["AvaloniaUI.DiagnosticsSupport"],
        TemplateStripCalls = ["WithDeveloperTools", "AttachDeveloperTools"],
        WarmTemplates = ["avalonia.app", "avalonia.mvvm"],
    };
}
