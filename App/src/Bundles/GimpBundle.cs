namespace BlueHeighliner.Bundler;

/// <summary>
/// The GIMP photo editor with its libraries, from the Ubuntu 24.04 archive.
/// </summary>
internal sealed class GimpBundle(IAptBuilder apt, IAptInstaller installer, ILog log) : AptProgramBundle(apt, installer, log)
{
    /// <inheritdoc />
    public override string Name => "gimp";

    /// <inheritdoc />
    public override string Description => "Retouch photos and make graphics with GIMP.";

    /// <inheritdoc />
    public override int Order => 85;

    /// <inheritdoc />
    protected override string Package => "gimp";
}
