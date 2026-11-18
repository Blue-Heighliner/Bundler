namespace BlueHeighliner.Bundler;

/// <summary>
/// The Krita painting program with its libraries, from the Ubuntu 24.04 archive.
/// </summary>
internal sealed class KritaBundle(IAptBuilder apt, IAptInstaller installer, ILog log) : AptProgramBundle(apt, installer, log)
{
    /// <inheritdoc />
    public override string Name => "krita";

    /// <inheritdoc />
    public override string Description => "Draw, paint and make textures with Krita.";

    /// <inheritdoc />
    public override int Order => 86;

    /// <inheritdoc />
    protected override string Package => "krita";
}
