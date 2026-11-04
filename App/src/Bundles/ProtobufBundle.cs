namespace BlueHeighliner.Bundler;

/// <summary>
/// The protobuf-net NuGet package.
/// </summary>
internal sealed class ProtobufBundle(INuGetBundleBuilder builder, INuGetBundleInstaller installer) : NuGetBundleBase(builder, installer)
{
    /// <inheritdoc />
    public override string Name => "protobuf";

    /// <inheritdoc />
    public override string Description => "Fast, compact data serialization for C# with Protocol Buffers.";

    /// <inheritdoc />
    public override int Order => 60;

    /// <inheritdoc />
    protected override NuGetBundleSpec Spec { get; } = new() { PackageList = "protobuf/packages.txt" };
}
