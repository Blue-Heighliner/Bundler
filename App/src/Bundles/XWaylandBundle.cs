namespace BlueHeighliner.Bundler;

/// <summary>
/// Xwayland, the X server that lets X11 programs run on a Wayland desktop, from the Ubuntu 24.04 archive. The installer USB drive puts it on the
/// computer already; the bundle brings the archive's newest version and anything that version needs.
/// </summary>
internal sealed class XWaylandBundle(IAptBuilder apt, IAptInstaller installer, ILog log) : AptProgramBundle(apt, installer, log)
{
    /// <inheritdoc />
    public override string Name => "xwayland";

    /// <inheritdoc />
    public override string Description => "Run X11 programs on a Wayland desktop with Xwayland.";

    /// <inheritdoc />
    public override int Order => 95;

    /// <inheritdoc />
    protected override string Package => "xwayland";
}
